import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { cn } from '@/lib/utils'
import {
  integrationsApi,
  type Integration,
  type ExecutionSummary,
  type ExecutionLogItem,
  type TriggerEvent,
} from '@/api/integrations'
import { packagesApi } from '@/api/packages'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { IntegrationAlertSettingsCard } from './IntegrationAlertSettingsCard'
import { getCurrentUser, hasPermission } from '@/lib/rbac'

// A single row in the merged history timeline. Executions are the primary rows; trigger events that
// did not (yet) produce an execution appear as lighter, non-selectable diagnostic rows.
type TimelineRow =
  | { kind: 'execution'; key: string; at: string; execution: ExecutionSummary }
  | { kind: 'trigger'; key: string; at: string; event: TriggerEvent }

type RowFilter = 'all' | 'runs'

export function IntegrationHistoryPage() {
  const { id } = useParams<{ id: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const selectedExecutionId = searchParams.get('execution')
  const [rowFilter, setRowFilter] = useState<RowFilter>('all')

  const user = getCurrentUser()
  const canViewExecutions = hasPermission('ViewExecutions', user)

  const { data: integration } = useQuery({
    queryKey: ['integration', id],
    queryFn: () => integrationsApi.get(id!),
    enabled: canViewExecutions && !!id,
  })

  const { data: executionData, isLoading: executionsLoading, error: executionsError } = useQuery({
    queryKey: ['integration-executions', id],
    queryFn: () => integrationsApi.executions(id!, 100),
    enabled: canViewExecutions && !!id,
    // Keep the timeline fresh while anything is mid-flight.
    refetchInterval: query =>
      (query.state.data?.executions ?? []).some(e => e.status === 'Running') ? 5_000 : false,
  })

  const { data: triggerEventData, isLoading: triggerEventsLoading } = useQuery({
    queryKey: ['trigger-events', id],
    queryFn: () => integrationsApi.triggerEvents({ integrationId: id!, limit: 100 }),
    enabled: canViewExecutions && !!id,
  })

  const executions = executionData?.executions ?? []
  const triggerEvents = triggerEventData?.events ?? []
  const selectedExecution = executions.find(e => e.id === selectedExecutionId) ?? null

  const { data: logData, isLoading: logsLoading, error: logsError } = useQuery({
    queryKey: ['execution-logs', id, selectedExecutionId],
    queryFn: () => integrationsApi.logs(id!, selectedExecutionId!),
    enabled: canViewExecutions && !!id && !!selectedExecutionId,
    refetchInterval: selectedExecution?.status === 'Running' ? 3_000 : false,
  })

  function selectExecution(executionId: string) {
    setSearchParams(prev => {
      const next = new URLSearchParams(prev)
      next.set('execution', executionId)
      return next
    })
  }

  if (!canViewExecutions) {
    return <AccessDenied title="Execution history unavailable" />
  }

  const rows = buildTimeline(executions, triggerEvents, rowFilter)
  const isTimelineLoading = executionsLoading || triggerEventsLoading

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Link to="/integrations" className="text-sm text-muted-foreground hover:text-foreground">
          ← Integrations
        </Link>
        <h2 className="text-xl font-semibold">{integration?.name ?? 'Execution history'}</h2>
        <p className="text-sm text-muted-foreground">
          Trigger events and runtime executions, newest first. Select a run to view its logs.
        </p>
        {integration && <ActiveVersionSelector integration={integration} />}
      </div>

      {integration && (
        <div className="max-w-2xl">
          <IntegrationAlertSettingsCard integrationId={integration.id} />
        </div>
      )}

      {executionsError && (
        <p className="text-sm text-destructive">
          {executionsError instanceof Error ? executionsError.message : 'Failed to load history.'}
        </p>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
        {/* Left: the merged timeline */}
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <RowFilterButton active={rowFilter === 'all'} onClick={() => setRowFilter('all')}>
              All activity
            </RowFilterButton>
            <RowFilterButton active={rowFilter === 'runs'} onClick={() => setRowFilter('runs')}>
              Runs only
            </RowFilterButton>
          </div>

          {isTimelineLoading ? (
            <TimelineSkeleton />
          ) : rows.length === 0 ? (
            <div className="rounded-lg border py-16 text-center text-muted-foreground">
              <p className="text-sm">No activity recorded yet.</p>
            </div>
          ) : (
            <ol className="overflow-hidden rounded-lg border divide-y">
              {rows.map(row =>
                row.kind === 'execution' ? (
                  <ExecutionRow
                    key={row.key}
                    execution={row.execution}
                    selected={row.execution.id === selectedExecutionId}
                    onSelect={() => selectExecution(row.execution.id)}
                  />
                ) : (
                  <TriggerRow key={row.key} event={row.event} hasExecution={false} />
                )
              )}
            </ol>
          )}
        </div>

        {/* Right: logs for the selected run */}
        <div className="space-y-3">
          {selectedExecution ? (
            <>
              <div>
                <h3 className="text-sm font-medium">Logs</h3>
                <p className="text-xs text-muted-foreground">
                  {formatDateTime(selectedExecution.startedAt)} · {selectedExecution.status}
                  {selectedExecution.packageVersion
                    ? ` · package ${selectedExecution.packageName ?? ''}${selectedExecution.packageName ? ' ' : ''}${selectedExecution.packageVersion}`
                    : ''}
                </p>
              </div>
              {logsError && (
                <p className="text-sm text-destructive">
                  {logsError instanceof Error ? logsError.message : 'Failed to load logs.'}
                </p>
              )}
              {logsLoading ? (
                <ExecutionLogsSkeleton />
              ) : (
                <ExecutionLogsPanel
                  logs={logData?.logs ?? []}
                  isRunning={selectedExecution.status === 'Running'}
                />
              )}
            </>
          ) : (
            <div className="flex h-full min-h-48 items-center justify-center rounded-lg border text-center text-muted-foreground">
              <p className="text-sm">Select a run on the left to view its logs.</p>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// Shows the integration's active package version and, for users who can manage integrations, lets
// them roll the package back/forward to another version. Integrations are versioned per package, so
// activating a version moves every integration in the package — not just this one — and takes effect
// on the next run (the agent loads the selected version's isolated assembly).
function ActiveVersionSelector({ integration }: { integration: Integration }) {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canManage = hasPermission('ManageIntegrations', user)
  const [error, setError] = useState<string | null>(null)

  const { data: packageData } = useQuery({
    queryKey: ['packages'],
    queryFn: packagesApi.list,
  })

  const activate = useMutation({
    mutationFn: (packageId: string) => packagesApi.activate(packageId),
    onSuccess: () => {
      setError(null)
      queryClient.invalidateQueries({ queryKey: ['integration', integration.id] })
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
    },
    onError: (err: Error) => setError(err.message),
  })

  if (!integration.packageId) {
    return (
      <p className="text-xs text-muted-foreground">
        Active version: <span className="font-medium">local agent path</span> (no package pinned)
      </p>
    )
  }

  const packages = packageData?.packages ?? []
  const current = packages.find(p => p.id === integration.packageId)

  // Other versions of the same package, newest first.
  const versions = current
    ? packages
        .filter(p => p.name === current.name)
        .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    : []

  function handleChange(packageId: string) {
    if (packageId === integration.packageId) return
    const target = versions.find(v => v.id === packageId)
    if (
      !window.confirm(
        `Make ${target?.version ?? 'this version'} the active version for this package? ` +
          `Every integration in the package moves to it, effective on the next run.`
      )
    )
      return
    activate.mutate(packageId)
  }

  return (
    <div className="flex flex-wrap items-center gap-2 pt-1">
      <span className="text-xs text-muted-foreground">Active version</span>
      {canManage && versions.length > 0 ? (
        <Select
          className="h-7 w-auto text-xs"
          value={integration.packageId}
          onChange={e => handleChange(e.target.value)}
          disabled={activate.isPending}
        >
          {versions.map(v => (
            <option key={v.id} value={v.id}>
              {v.version}
              {v.id === integration.packageId ? ' (current)' : ''}
            </option>
          ))}
        </Select>
      ) : (
        <Badge variant="outline" className="font-mono">
          {current?.version ?? 'unknown'}
        </Badge>
      )}
      {activate.isPending && <span className="text-xs text-muted-foreground">Activating…</span>}
      {error && <span className="text-xs text-destructive">{error}</span>}
    </div>
  )
}

// Merges executions and trigger events into one time-ordered list. A ConvertedToWork trigger event
// whose work item already has an execution is dropped — the execution row represents it — so the
// timeline shows each run once while still surfacing rejected/deduplicated/queued trigger activity.
function buildTimeline(
  executions: ExecutionSummary[],
  triggerEvents: TriggerEvent[],
  filter: RowFilter
): TimelineRow[] {
  const executionWorkItemIds = new Set(
    executions.map(e => e.workItemId).filter((w): w is string => !!w)
  )

  const rows: TimelineRow[] = executions.map(execution => ({
    kind: 'execution',
    key: execution.id,
    at: execution.startedAt,
    execution,
  }))

  if (filter === 'all') {
    for (const event of triggerEvents) {
      const representedByExecution =
        event.outcome === 'ConvertedToWork' &&
        !!event.workItemId &&
        executionWorkItemIds.has(event.workItemId)

      if (!representedByExecution)
        rows.push({ kind: 'trigger', key: event.id, at: event.receivedAt, event })
    }
  }

  return rows.sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime())
}

function RowFilterButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'rounded-md px-2.5 py-1 text-xs font-medium transition-colors',
        active ? 'bg-foreground text-background' : 'bg-muted text-muted-foreground hover:text-foreground'
      )}
    >
      {children}
    </button>
  )
}

function ExecutionRow({
  execution,
  selected,
  onSelect,
}: {
  execution: ExecutionSummary
  selected: boolean
  onSelect: () => void
}) {
  return (
    <li>
      <button
        onClick={onSelect}
        className={cn(
          'flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-muted/50',
          selected && 'bg-muted'
        )}
      >
        <ExecutionStatusBadge status={execution.status} />
        <div className="min-w-0 flex-1">
          <p className="text-sm">
            {execution.triggerSource ?? 'Run'} · {formatDateTime(execution.startedAt)}
          </p>
          {execution.packageVersion && (
            <p className="truncate text-xs text-muted-foreground">
              Package {execution.packageName ? `${execution.packageName} ` : ''}{execution.packageVersion}
            </p>
          )}
          {execution.errorMessage && (
            <p className="truncate text-xs text-destructive" title={execution.errorMessage}>
              {execution.errorMessage}
            </p>
          )}
        </div>
        <span className="shrink-0 text-xs text-muted-foreground">{formatDuration(execution)}</span>
      </button>
    </li>
  )
}

function TriggerRow({ event, hasExecution }: { event: TriggerEvent; hasExecution: boolean }) {
  // A ConvertedToWork event with no execution means work was created but has not run yet.
  const queued = event.outcome === 'ConvertedToWork' && !hasExecution
  const label = queued ? 'Queued' : event.outcome === 'ConvertedToWork' ? 'Converted' : event.outcome

  return (
    <li className="flex items-center gap-3 px-4 py-2.5">
      <Badge variant={triggerRowVariant(event.outcome, queued)} className="shrink-0">
        {label}
      </Badge>
      <div className="min-w-0 flex-1">
        <p className="text-xs text-muted-foreground">
          {event.adapterKey} · {formatDateTime(event.receivedAt)}
        </p>
        {event.errorMessage && (
          <p className="truncate text-xs text-destructive" title={event.errorMessage}>
            {event.errorMessage}
          </p>
        )}
      </div>
    </li>
  )
}

function triggerRowVariant(
  outcome: TriggerEvent['outcome'],
  queued: boolean
): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (outcome === 'Rejected' || outcome === 'Failed') return 'destructive'
  if (outcome === 'Deduplicated') return 'secondary'
  if (queued) return 'secondary'
  return 'outline'
}

function ExecutionStatusBadge({ status }: { status: ExecutionSummary['status'] }) {
  const variant = status === 'Failed' || status === 'TimedOut'
    ? 'destructive'
    : status === 'Running'
      ? 'secondary'
      : 'default'

  return <Badge variant={variant}>{status}</Badge>
}

const LOG_LEVEL_FILTERS = ['All', 'Error', 'Warning', 'Info', 'Debug'] as const
type LogLevelFilter = (typeof LOG_LEVEL_FILTERS)[number]

function matchesLevelFilter(logLevel: string, filter: LogLevelFilter): boolean {
  if (filter === 'All') return true
  if (filter === 'Error') return logLevel === 'Error' || logLevel === 'Critical'
  if (filter === 'Warning') return logLevel === 'Warning'
  if (filter === 'Info') return logLevel === 'Information' || logLevel === 'Info'
  if (filter === 'Debug') return logLevel === 'Debug' || logLevel === 'Trace'
  return true
}

function ExecutionLogsPanel({ logs, isRunning }: { logs: ExecutionLogItem[]; isRunning: boolean }) {
  const [levelFilter, setLevelFilter] = useState<LogLevelFilter>('All')
  const [search, setSearch] = useState('')
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set())

  function toggleExpand(logId: string) {
    setExpandedIds(prev => {
      const next = new Set(prev)
      if (next.has(logId)) next.delete(logId)
      else next.add(logId)
      return next
    })
  }

  const filtered = logs.filter(
    log =>
      matchesLevelFilter(log.level, levelFilter) &&
      (search === '' || log.message.toLowerCase().includes(search.toLowerCase()))
  )

  if (logs.length === 0) {
    return (
      <div className="rounded-lg border py-10 text-center text-muted-foreground">
        <p className="text-sm">{isRunning ? 'Waiting for logs…' : 'No logs recorded for this execution.'}</p>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        {LOG_LEVEL_FILTERS.map(lvl => (
          <button
            key={lvl}
            onClick={() => setLevelFilter(lvl)}
            className={cn(
              'rounded-md px-2.5 py-1 text-xs font-medium transition-colors',
              levelFilter === lvl
                ? 'bg-foreground text-background'
                : 'bg-muted text-muted-foreground hover:text-foreground'
            )}
          >
            {lvl}
          </button>
        ))}
        <Input
          className="h-7 w-44 text-xs"
          placeholder="Search messages…"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
        {isRunning && (
          <span className="ml-auto flex items-center gap-1.5 text-xs text-muted-foreground">
            <span className="h-1.5 w-1.5 rounded-full bg-green-500 animate-pulse" />
            Live
          </span>
        )}
      </div>

      {filtered.length === 0 ? (
        <div className="rounded-lg border py-8 text-center text-muted-foreground">
          <p className="text-sm">No logs match the current filter.</p>
        </div>
      ) : (
        <div className="divide-y rounded-lg border">
          {filtered.map(log => {
            const isExpanded = expandedIds.has(log.id)
            return (
              <div key={log.id} className="grid gap-2 p-3 sm:grid-cols-[7rem_6rem_1fr]">
                <p className="text-xs text-muted-foreground">{formatLogTime(log.timestamp)}</p>
                <ExecutionLogLevel level={log.level} />
                <div className="min-w-0 space-y-1">
                  <p className="break-words text-sm">{log.message}</p>
                  {log.exception && (
                    <div className="space-y-1">
                      <button
                        onClick={() => toggleExpand(log.id)}
                        className="text-xs text-muted-foreground hover:text-foreground"
                      >
                        {isExpanded ? '▲ Hide exception' : '▼ Show exception'}
                      </button>
                      {isExpanded && (
                        <pre className="overflow-auto rounded bg-muted p-2 text-xs text-destructive">
                          {log.exception}
                        </pre>
                      )}
                    </div>
                  )}
                  {log.propertiesJson && (
                    <pre className="max-h-32 overflow-auto rounded bg-muted p-2 text-xs text-muted-foreground">
                      {formatJson(log.propertiesJson)}
                    </pre>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function ExecutionLogLevel({ level }: { level: string }) {
  const variant = level === 'Error' || level === 'Critical'
    ? 'destructive'
    : level === 'Warning'
      ? 'secondary'
      : 'outline'

  return <Badge variant={variant}>{level}</Badge>
}

function TimelineSkeleton() {
  return (
    <div className="divide-y rounded-lg border">
      {Array.from({ length: 6 }).map((_, i) => (
        <div key={i} className="flex items-center gap-3 px-4 py-3">
          <Skeleton className="h-5 w-16" />
          <Skeleton className="h-4 flex-1" />
          <Skeleton className="h-4 w-12" />
        </div>
      ))}
    </div>
  )
}

function ExecutionLogsSkeleton() {
  return (
    <div className="divide-y rounded-lg border">
      {Array.from({ length: 4 }).map((_, i) => (
        <div key={i} className="grid gap-2 p-3 sm:grid-cols-[7rem_6rem_1fr]">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-4 w-16" />
          <Skeleton className="h-4 w-full" />
        </div>
      ))}
    </div>
  )
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(value))
}

function formatLogTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}

function formatJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function formatDuration(execution: ExecutionSummary) {
  if (execution.status === 'Running') return 'Running'
  if (execution.durationMs == null) return '–'
  if (execution.durationMs < 1000) return `${execution.durationMs} ms`

  const seconds = execution.durationMs / 1000
  if (seconds < 60) return `${seconds.toFixed(1)} s`

  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = Math.round(seconds % 60)
  return `${minutes}m ${remainingSeconds}s`
}
