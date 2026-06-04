import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  integrationsApi,
  type Integration,
  type IntegrationTrigger,
  type ExecutionSummary,
  type ExecutionLogItem,
  type TriggerType,
  type CreateIntegrationRequest,
  type UpdateIntegrationRequest,
} from '@/api/integrations'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetFooter,
} from '@/components/ui/sheet'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { getCurrentUser, hasPermission } from '@/lib/rbac'

type FormMode = 'create' | 'edit'

interface FormState {
  name: string
  slug: string
  description: string
  environment: string
  triggerType: TriggerType
  cronExpression: string
  className: string
  timeoutSeconds: string
  status: 'Enabled' | 'Disabled'
}

const emptyForm: FormState = {
  name: '',
  slug: '',
  description: '',
  environment: 'production',
  triggerType: 'Scheduled',
  cronExpression: '',
  className: '',
  timeoutSeconds: '',
  status: 'Enabled',
}

export function IntegrationsPage() {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canViewIntegrations = hasPermission('ViewIntegrations', user)
  const canManageIntegrations = hasPermission('ManageIntegrations', user)
  const canTriggerManualRun = hasPermission('TriggerManualRun', user)
  const canViewExecutions = hasPermission('ViewExecutions', user)
  const [sheetOpen, setSheetOpen] = useState(false)
  const [formMode, setFormMode] = useState<FormMode>('create')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingTriggers, setEditingTriggers] = useState<IntegrationTrigger[]>([])
  const [historyIntegration, setHistoryIntegration] = useState<Integration | null>(null)
  const [selectedExecution, setSelectedExecution] = useState<ExecutionSummary | null>(null)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [runError, setRunError] = useState<string | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['integrations'],
    queryFn: () => integrationsApi.list(),
    enabled: canViewIntegrations,
  })

  const createIntegration = useMutation({
    mutationFn: (data: CreateIntegrationRequest) => integrationsApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
      setSheetOpen(false)
      setForm(emptyForm)
      setEditingTriggers([])
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const updateIntegration = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateIntegrationRequest }) =>
      integrationsApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
      setSheetOpen(false)
      setForm(emptyForm)
      setEditingId(null)
      setEditingTriggers([])
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const deleteIntegration = useMutation({
    mutationFn: (id: string) => integrationsApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['integrations'] }),
  })

  const runManual = useMutation({
    mutationFn: (id: string) => integrationsApi.runManual(id),
    onSuccess: () => {
      setRunError(null)
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
      queryClient.invalidateQueries({ queryKey: ['integration-executions'] })
    },
    onError: (err: Error) => setRunError(err.message),
  })

  const {
    data: executionHistory,
    isLoading: isHistoryLoading,
    error: historyError,
  } = useQuery({
    queryKey: ['integration-executions', historyIntegration?.id],
    queryFn: () => integrationsApi.executions(historyIntegration!.id),
    enabled: canViewExecutions && historyIntegration !== null,
  })

  const {
    data: executionLogs,
    isLoading: areLogsLoading,
    error: logsError,
  } = useQuery({
    queryKey: ['execution-logs', historyIntegration?.id, selectedExecution?.id],
    queryFn: () => integrationsApi.logs(historyIntegration!.id, selectedExecution!.id),
    enabled: canViewExecutions && historyIntegration !== null && selectedExecution !== null,
  })

  function handleOpenCreate() {
    if (!canManageIntegrations) return
    setFormMode('create')
    setForm(emptyForm)
    setFormError(null)
    setEditingId(null)
    setSheetOpen(true)
  }

  function handleOpenEdit(integration: Integration) {
    if (!canManageIntegrations) return
    const primaryTrigger = integration.triggers[0]
    setFormMode('edit')
    setForm({
      name: integration.name,
      slug: integration.slug,
      description: integration.description ?? '',
      environment: integration.environment,
      triggerType: primaryTrigger?.type ?? 'Manual',
      cronExpression: primaryTrigger?.cronExpression ?? '',
      className: integration.className,
      timeoutSeconds: integration.timeoutSeconds?.toString() ?? '',
      status: integration.status,
    })
    setFormError(null)
    setEditingId(integration.id)
    setEditingTriggers(integration.triggers)
    setSheetOpen(true)
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!canManageIntegrations) return
    setFormError(null)

    if (formMode === 'create') {
      createIntegration.mutate({
        name: form.name,
        slug: form.slug,
        description: form.description || undefined,
        environment: form.environment,
        className: form.className,
        triggers: buildSubmittedTriggers(),
        timeoutSeconds: form.timeoutSeconds ? Number(form.timeoutSeconds) : undefined,
      })
    } else if (editingId) {
      updateIntegration.mutate({
        id: editingId,
        data: {
          name: form.name,
          description: form.description || undefined,
          status: form.status,
          triggers: buildSubmittedTriggers(editingTriggers),
          timeoutSeconds: form.timeoutSeconds ? Number(form.timeoutSeconds) : undefined,
        },
      })
    }
  }

  function buildSubmittedTriggers(existing: IntegrationTrigger[] = []): IntegrationTrigger[] {
    if (form.triggerType === 'Manual') {
      return existing.filter(t => t.type !== 'Scheduled' && t.type !== 'Webhook')
    }

    const slug = form.triggerType.toLowerCase()
    const trigger: IntegrationTrigger = {
      name: form.triggerType === 'Scheduled' ? 'Schedule' : 'Webhook',
      slug,
      type: form.triggerType,
      enabled: form.status === 'Enabled',
      cronExpression: form.triggerType === 'Scheduled' ? form.cronExpression : undefined,
    }

    return [
      ...existing.filter(t => t.slug !== slug && t.type !== form.triggerType),
      trigger,
    ]
  }

  function generateSlug(name: string): string {
    return name
      .toLowerCase()
      .replace(/[^a-z0-9\s-]/g, '')
      .replace(/\s+/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '')
  }

  const isPending = createIntegration.isPending || updateIntegration.isPending

  if (!canViewIntegrations) {
    return <AccessDenied title="Integrations unavailable" />
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Integrations</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Manage your integration definitions.
          </p>
        </div>
        {canManageIntegrations && <Button onClick={handleOpenCreate}>New integration</Button>}
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load integrations.'}
        </p>
      )}

      {runError && (
        <div className="flex items-center justify-between rounded-md border border-destructive bg-destructive/10 px-4 py-3">
          <p className="text-sm text-destructive">{runError}</p>
          <Button variant="ghost" size="sm" onClick={() => setRunError(null)}>
            Dismiss
          </Button>
        </div>
      )}

      {isLoading ? (
        <IntegrationsTableSkeleton />
      ) : (
        <IntegrationsTable
          integrations={data?.integrations ?? []}
          onEdit={handleOpenEdit}
          onViewHistory={setHistoryIntegration}
          onRun={id => runManual.mutate(id)}
          isRunPending={runManual.isPending}
          onDelete={id => deleteIntegration.mutate(id)}
          canManageIntegrations={canManageIntegrations}
          canTriggerManualRun={canTriggerManualRun}
          canViewExecutions={canViewExecutions}
        />
      )}

      <Sheet open={sheetOpen} onOpenChange={setSheetOpen}>
        <SheetContent className="sm:max-w-md overflow-y-auto">
          <SheetHeader>
            <SheetTitle>
              {formMode === 'create' ? 'New integration' : 'Edit integration'}
            </SheetTitle>
            <SheetDescription>
              {formMode === 'create'
                ? 'Define a new integration to be executed by the runtime agent.'
                : 'Update the integration configuration.'}
            </SheetDescription>
          </SheetHeader>

          <form onSubmit={handleSubmit} className="flex flex-col gap-4 px-4 pb-4">
            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                placeholder="Sync Orders"
                value={form.name}
                onChange={e => {
                  const name = e.target.value
                  setForm(prev => ({
                    ...prev,
                    name,
                    ...(formMode === 'create' ? { slug: generateSlug(name) } : {}),
                  }))
                }}
                required
              />
            </div>

            {formMode === 'create' && (
              <div className="space-y-2">
                <Label htmlFor="slug">Slug</Label>
                <Input
                  id="slug"
                  placeholder="sync-orders"
                  value={form.slug}
                  onChange={e =>
                    setForm(prev => ({ ...prev, slug: e.target.value.toLowerCase() }))
                  }
                  pattern="^[a-z0-9-]+$"
                  required
                />
                <p className="text-xs text-muted-foreground">
                  Lowercase letters, numbers, and hyphens only.
                </p>
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="description">Description</Label>
              <Input
                id="description"
                placeholder="Optional description"
                value={form.description}
                onChange={e => setForm(prev => ({ ...prev, description: e.target.value }))}
              />
            </div>

            {formMode === 'create' && (
              <div className="space-y-2">
                <Label htmlFor="environment">Environment</Label>
                <Select
                  id="environment"
                  value={form.environment}
                  onChange={e =>
                    setForm(prev => ({ ...prev, environment: e.target.value }))
                  }
                  required
                >
                  <option value="production">production</option>
                  <option value="staging">staging</option>
                  <option value="development">development</option>
                </Select>
              </div>
            )}

            {formMode === 'create' && (
              <div className="space-y-2">
                <Label htmlFor="triggerType">Trigger type</Label>
                <Select
                  id="triggerType"
                  value={form.triggerType}
                  onChange={e =>
                    setForm(prev => ({
                      ...prev,
                      triggerType: e.target.value as TriggerType,
                    }))
                  }
                  required
                >
                  <option value="Scheduled">Scheduled</option>
                  <option value="Webhook">Webhook</option>
                  <option value="Manual">Manual</option>
                </Select>
              </div>
            )}

            {form.triggerType === 'Scheduled' && (
              <div className="space-y-2">
                <Label htmlFor="cronExpression">Cron expression</Label>
                <Input
                  id="cronExpression"
                  placeholder="0 * * * *"
                  value={form.cronExpression}
                  onChange={e =>
                    setForm(prev => ({ ...prev, cronExpression: e.target.value }))
                  }
                  required={form.triggerType === 'Scheduled'}
                />
                <p className="text-xs text-muted-foreground">
                  Standard 5-field cron format. Example: <code>0 * * * *</code> runs hourly.
                </p>
              </div>
            )}

            {formMode === 'create' && (
              <div className="space-y-2">
                <Label htmlFor="className">Class name</Label>
                <Input
                  id="className"
                  placeholder="MyCompany.Integrations.SyncOrdersIntegration"
                  value={form.className}
                  onChange={e => setForm(prev => ({ ...prev, className: e.target.value }))}
                  required
                />
                <p className="text-xs text-muted-foreground">
                  Fully qualified .NET type name that implements IIntegration.
                </p>
              </div>
            )}

            {formMode === 'edit' && (
              <div className="space-y-2">
                <Label htmlFor="status">Status</Label>
                <Select
                  id="status"
                  value={form.status}
                  onChange={e =>
                    setForm(prev => ({
                      ...prev,
                      status: e.target.value as 'Enabled' | 'Disabled',
                    }))
                  }
                  required
                >
                  <option value="Enabled">Enabled</option>
                  <option value="Disabled">Disabled</option>
                </Select>
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="timeoutSeconds">Timeout seconds</Label>
              <Input
                id="timeoutSeconds"
                type="number"
                min="1"
                step="1"
                placeholder="No timeout"
                value={form.timeoutSeconds}
                onChange={e => setForm(prev => ({ ...prev, timeoutSeconds: e.target.value }))}
              />
            </div>

            {formError && <p className="text-sm text-destructive">{formError}</p>}

            <SheetFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => setSheetOpen(false)}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={isPending}>
                {isPending
                  ? 'Saving...'
                  : formMode === 'create'
                    ? 'Create integration'
                    : 'Save changes'}
              </Button>
            </SheetFooter>
          </form>
        </SheetContent>
      </Sheet>

      <Sheet
        open={historyIntegration !== null}
        onOpenChange={open => {
          if (!open) {
            setHistoryIntegration(null)
            setSelectedExecution(null)
          }
        }}
      >
        <SheetContent className="sm:max-w-2xl overflow-y-auto">
          <SheetHeader>
            <SheetTitle>{historyIntegration?.name ?? 'Execution history'}</SheetTitle>
            <SheetDescription>
              Recent runtime agent executions for this integration.
            </SheetDescription>
          </SheetHeader>

          <div className="px-4 pb-4">
            {historyError && (
              <p className="text-sm text-destructive">
                {historyError instanceof Error
                  ? historyError.message
                  : 'Failed to load execution history.'}
              </p>
            )}

            {isHistoryLoading ? (
              <ExecutionHistorySkeleton />
            ) : (
              <ExecutionHistoryList
                executions={executionHistory?.executions ?? []}
                selectedExecutionId={selectedExecution?.id ?? null}
                onSelectExecution={setSelectedExecution}
              />
            )}

            {selectedExecution && (
              <div className="mt-6 space-y-3">
                <div>
                  <h3 className="text-sm font-medium">Logs</h3>
                  <p className="text-xs text-muted-foreground">
                    {formatDateTime(selectedExecution.startedAt)}
                  </p>
                </div>

                {logsError && (
                  <p className="text-sm text-destructive">
                    {logsError instanceof Error ? logsError.message : 'Failed to load logs.'}
                  </p>
                )}

                {areLogsLoading ? (
                  <ExecutionLogsSkeleton />
                ) : (
                  <ExecutionLogsList logs={executionLogs?.logs ?? []} />
                )}
              </div>
            )}
          </div>
        </SheetContent>
      </Sheet>
    </div>
  )
}

function IntegrationsTable({
  integrations,
  onEdit,
  onViewHistory,
  onRun,
  isRunPending,
  onDelete,
  canManageIntegrations,
  canTriggerManualRun,
  canViewExecutions,
}: {
  integrations: Integration[]
  onEdit: (integration: Integration) => void
  onViewHistory: (integration: Integration) => void
  onRun: (id: string) => void
  isRunPending: boolean
  onDelete: (id: string) => void
  canManageIntegrations: boolean
  canTriggerManualRun: boolean
  canViewExecutions: boolean
}) {
  if (integrations.length === 0) {
    return (
      <div className="text-center py-16 text-muted-foreground border rounded-lg">
        <p className="text-sm">No integrations yet.</p>
        {canManageIntegrations && (
          <p className="text-sm mt-1">Create your first one to get started.</p>
        )}
      </div>
    )
  }

  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Environment</TableHead>
            <TableHead>Trigger</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Last run</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {integrations.map(integration => (
            <TableRow key={integration.id}>
              <TableCell>
                <div>
                  <p className="font-medium">{integration.name}</p>
                  <p className="text-xs text-muted-foreground font-mono">
                    {integration.className}
                  </p>
                </div>
              </TableCell>
              <TableCell>
                <Badge variant="outline">{integration.environment}</Badge>
              </TableCell>
              <TableCell>
                <div className="flex flex-wrap gap-1.5">
                  {integration.triggers.length === 0 ? (
                    <Badge variant="outline">Manual</Badge>
                  ) : (
                    integration.triggers.map(trigger => (
                      <span key={trigger.id ?? trigger.slug} className="inline-flex items-center gap-1.5">
                        <Badge variant="outline">{trigger.type}</Badge>
                        {trigger.cronExpression && (
                          <code className="text-xs text-muted-foreground bg-muted px-1.5 py-0.5 rounded">
                            {trigger.cronExpression}
                          </code>
                        )}
                      </span>
                    ))
                  )}
                </div>
              </TableCell>
              <TableCell>
                <StatusBadge status={integration.status} />
              </TableCell>
              <TableCell>
                <LastRun execution={integration.lastExecution} />
              </TableCell>
              <TableCell className="text-right space-x-2">
                {canTriggerManualRun && (
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={integration.status === 'Disabled' || isRunPending}
                    onClick={() => onRun(integration.id)}
                  >
                    Run now
                  </Button>
                )}
                {canViewExecutions && (
                  <Button variant="ghost" size="sm" onClick={() => onViewHistory(integration)}>
                    History
                  </Button>
                )}
                {canManageIntegrations && (
                  <>
                    <Button variant="ghost" size="sm" onClick={() => onEdit(integration)}>
                      Edit
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="text-destructive hover:text-destructive"
                      onClick={() => onDelete(integration.id)}
                    >
                      Delete
                    </Button>
                  </>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function StatusBadge({ status }: { status: Integration['status'] }) {
  return (
    <Badge variant={status === 'Enabled' ? 'default' : 'secondary'}>{status}</Badge>
  )
}

function ExecutionStatusBadge({ status }: { status: ExecutionSummary['status'] }) {
  const variant = status === 'Failed' ? 'destructive' : status === 'Running' ? 'secondary' : 'default'

  return <Badge variant={variant}>{status}</Badge>
}

function LastRun({ execution }: { execution?: ExecutionSummary | null }) {
  if (!execution) {
    return <span className="text-sm text-muted-foreground">Never</span>
  }

  return (
    <div className="space-y-1">
      <ExecutionStatusBadge status={execution.status} />
      <p className="text-xs text-muted-foreground">{formatDateTime(execution.startedAt)}</p>
    </div>
  )
}

function ExecutionHistoryList({
  executions,
  selectedExecutionId,
  onSelectExecution,
}: {
  executions: ExecutionSummary[]
  selectedExecutionId: string | null
  onSelectExecution: (execution: ExecutionSummary) => void
}) {
  if (executions.length === 0) {
    return (
      <div className="text-center py-12 text-muted-foreground border rounded-lg">
        <p className="text-sm">No executions recorded yet.</p>
      </div>
    )
  }

  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Started</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Duration</TableHead>
            <TableHead>Error</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {executions.map(execution => (
            <TableRow key={execution.id}>
              <TableCell>
                <div>
                  <p className="text-sm">{formatDateTime(execution.startedAt)}</p>
                  <p className="text-xs text-muted-foreground">{execution.environment}</p>
                </div>
              </TableCell>
              <TableCell>
                <ExecutionStatusBadge status={execution.status} />
              </TableCell>
              <TableCell>
                <span className="text-sm">{formatDuration(execution)}</span>
              </TableCell>
              <TableCell>
                {execution.errorMessage ? (
                  <p className="max-w-64 truncate text-sm text-destructive" title={execution.errorMessage}>
                    {execution.errorMessage}
                  </p>
                ) : (
                  <span className="text-sm text-muted-foreground">None</span>
                )}
              </TableCell>
              <TableCell className="text-right">
                <Button
                  variant={selectedExecutionId === execution.id ? 'secondary' : 'ghost'}
                  size="sm"
                  onClick={() => onSelectExecution(execution)}
                >
                  Logs
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function ExecutionLogsList({ logs }: { logs: ExecutionLogItem[] }) {
  if (logs.length === 0) {
    return (
      <div className="text-center py-10 text-muted-foreground border rounded-lg">
        <p className="text-sm">No logs recorded for this execution.</p>
      </div>
    )
  }

  return (
    <div className="border rounded-lg divide-y">
      {logs.map(log => (
        <div key={log.id} className="grid gap-2 p-3 sm:grid-cols-[8rem_6rem_1fr]">
          <p className="text-xs text-muted-foreground">{formatLogTime(log.timestamp)}</p>
          <ExecutionLogLevel level={log.level} />
          <div className="min-w-0 space-y-1">
            <p className="text-sm break-words">{log.message}</p>
            {log.exception && (
              <pre className="max-h-40 overflow-auto rounded bg-muted p-2 text-xs text-destructive">
                {log.exception}
              </pre>
            )}
            {log.propertiesJson && (
              <pre className="max-h-32 overflow-auto rounded bg-muted p-2 text-xs text-muted-foreground">
                {formatJson(log.propertiesJson)}
              </pre>
            )}
          </div>
        </div>
      ))}
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

function ExecutionLogsSkeleton() {
  return (
    <div className="border rounded-lg divide-y">
      {Array.from({ length: 4 }).map((_, i) => (
        <div key={i} className="grid gap-2 p-3 sm:grid-cols-[8rem_6rem_1fr]">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-4 w-16" />
          <Skeleton className="h-4 w-full" />
        </div>
      ))}
    </div>
  )
}

function ExecutionHistorySkeleton() {
  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Started</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Duration</TableHead>
            <TableHead>Error</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 5 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell>
                <Skeleton className="h-4 w-36" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-20" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-16" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-32" />
              </TableCell>
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
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
  if (execution.durationMs == null) return '-'

  if (execution.durationMs < 1000) return `${execution.durationMs} ms`

  const seconds = execution.durationMs / 1000
  if (seconds < 60) return `${seconds.toFixed(1)} s`

  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = Math.round(seconds % 60)
  return `${minutes}m ${remainingSeconds}s`
}

function IntegrationsTableSkeleton() {
  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Environment</TableHead>
            <TableHead>Trigger</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Last run</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 4 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell>
                <Skeleton className="h-4 w-40" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-20" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-24" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-16" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-24" />
              </TableCell>
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
