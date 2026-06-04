import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  workflowsApi,
  type WorkflowDefinition,
  type WorkflowNodeRun,
  type WorkflowRun,
} from '@/api/workflows'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { getCurrentUser, hasPermission } from '@/lib/rbac'

export function WorkflowsPage() {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canView = hasPermission('ViewIntegrations', user)
  const canRun = hasPermission('TriggerManualRun', user)
  const [selectedWorkflow, setSelectedWorkflow] = useState<WorkflowDefinition | null>(null)
  const [runError, setRunError] = useState<string | null>(null)

  const workflows = useQuery({
    queryKey: ['workflows'],
    queryFn: workflowsApi.list,
    enabled: canView,
  })

  const selectedId = selectedWorkflow?.id ?? workflows.data?.workflows[0]?.id ?? null
  const activeWorkflow = selectedWorkflow ?? workflows.data?.workflows[0] ?? null

  const runs = useQuery({
    queryKey: ['workflow-runs', selectedId],
    queryFn: () => workflowsApi.runs(selectedId!),
    enabled: canView && selectedId !== null,
  })

  const runWorkflow = useMutation({
    mutationFn: (workflowId: string) => workflowsApi.run(workflowId),
    onSuccess: result => {
      setRunError(null)
      queryClient.invalidateQueries({ queryKey: ['workflow-runs', result.workflowDefinitionId] })
    },
    onError: (err: Error) => setRunError(err.message),
  })

  if (!canView) {
    return <AccessDenied title="Workflows unavailable" />
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">Workflows</h2>
          <p className="mt-0.5 text-sm text-muted-foreground">
            DAG runs and node state for integration workflows.
          </p>
        </div>
        {activeWorkflow && canRun && (
          <Button
            onClick={() => runWorkflow.mutate(activeWorkflow.id)}
            disabled={runWorkflow.isPending}
          >
            {runWorkflow.isPending ? 'Starting...' : 'Run workflow'}
          </Button>
        )}
      </div>

      {workflows.error && (
        <p className="text-sm text-destructive">
          {workflows.error instanceof Error ? workflows.error.message : 'Failed to load workflows.'}
        </p>
      )}
      {runError && <p className="text-sm text-destructive">{runError}</p>}

      {workflows.isLoading ? (
        <TableSkeleton rows={4} />
      ) : (
        <div className="grid gap-6 xl:grid-cols-[minmax(20rem,28rem)_1fr]">
          <WorkflowList
            workflows={workflows.data?.workflows ?? []}
            selectedId={selectedId}
            onSelect={workflow => setSelectedWorkflow(workflow)}
          />
          <WorkflowDetail
            workflow={activeWorkflow}
            runs={runs.data?.runs ?? []}
            loadingRuns={runs.isLoading}
            runsError={runs.error}
          />
        </div>
      )}
    </div>
  )
}

function WorkflowList({
  workflows,
  selectedId,
  onSelect,
}: {
  workflows: WorkflowDefinition[]
  selectedId: string | null
  onSelect: (workflow: WorkflowDefinition) => void
}) {
  if (workflows.length === 0) {
    return (
      <div className="rounded-lg border py-16 text-center text-sm text-muted-foreground">
        No workflows defined.
      </div>
    )
  }

  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Environment</TableHead>
            <TableHead>Nodes</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {workflows.map(workflow => (
            <TableRow
              key={workflow.id}
              className={selectedId === workflow.id ? 'bg-muted/60' : undefined}
              onClick={() => onSelect(workflow)}
            >
              <TableCell>
                <div className="font-medium">{workflow.name}</div>
                <div className="text-xs text-muted-foreground">{workflow.slug}</div>
              </TableCell>
              <TableCell>
                <Badge variant="outline">{workflow.environment}</Badge>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {workflow.nodes.length}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function WorkflowDetail({
  workflow,
  runs,
  loadingRuns,
  runsError,
}: {
  workflow: WorkflowDefinition | null
  runs: WorkflowRun[]
  loadingRuns: boolean
  runsError: unknown
}) {
  if (!workflow) {
    return null
  }

  const latest = runs[0]
  const hasRunsError = Boolean(runsError)
  const runsErrorMessage = runsError instanceof Error ? runsError.message : null

  return (
    <div className="space-y-5">
      <section className="rounded-lg border p-4">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="text-sm font-medium">{workflow.name}</h3>
          <Badge variant="outline">{workflow.status}</Badge>
        </div>
        <div className="mt-4 grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
          {workflow.nodes.map(node => (
            <div key={node.id} className="rounded-md border p-3">
              <div className="font-medium">{node.name}</div>
              <div className="mt-1 text-xs text-muted-foreground">{node.key}</div>
            </div>
          ))}
        </div>
        {workflow.edges.length > 0 && (
          <div className="mt-4 flex flex-wrap gap-2">
            {workflow.edges.map(edge => (
              <Badge key={`${edge.from}-${edge.to}`} variant="secondary">
                {edge.from} {'->'} {edge.to}
              </Badge>
            ))}
          </div>
        )}
      </section>

      {hasRunsError && (
        <p className="text-sm text-destructive">
          {runsErrorMessage ?? 'Failed to load workflow runs.'}
        </p>
      )}

      <section className="space-y-3">
        <h3 className="text-sm font-medium">Latest run</h3>
        {loadingRuns ? (
          <TableSkeleton rows={3} />
        ) : latest ? (
          <WorkflowRunPanel run={latest} />
        ) : (
          <div className="rounded-lg border py-10 text-center text-sm text-muted-foreground">
            No workflow runs yet.
          </div>
        )}
      </section>

      {runs.length > 1 && (
        <section className="space-y-3">
          <h3 className="text-sm font-medium">Recent runs</h3>
          <RunsTable runs={runs.slice(1)} />
        </section>
      )}
    </div>
  )
}

function WorkflowRunPanel({ run }: { run: WorkflowRun }) {
  return (
    <div className="rounded-lg border">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b p-4">
        <div>
          <div className="text-sm font-medium">{formatDate(run.startedAt)}</div>
          <div className="text-xs text-muted-foreground">{run.id}</div>
        </div>
        <Badge variant={run.status === 'Failed' ? 'destructive' : 'outline'}>{run.status}</Badge>
      </div>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Node</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Work item</TableHead>
            <TableHead>Execution</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {run.nodes.map(node => (
            <NodeRunRow key={node.id} node={node} />
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function NodeRunRow({ node }: { node: WorkflowNodeRun }) {
  return (
    <TableRow>
      <TableCell>
        <div className="font-medium">{node.nodeName}</div>
        <div className="text-xs text-muted-foreground">{node.nodeKey}</div>
      </TableCell>
      <TableCell>
        <Badge variant={node.status === 'Failed' ? 'destructive' : 'outline'}>{node.status}</Badge>
      </TableCell>
      <TableCell className="max-w-44 truncate text-xs text-muted-foreground">
        {node.workItemId ?? '-'}
      </TableCell>
      <TableCell className="max-w-44 truncate text-xs text-muted-foreground">
        {node.executionRecordId ?? '-'}
      </TableCell>
    </TableRow>
  )
}

function RunsTable({ runs }: { runs: WorkflowRun[] }) {
  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Started</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Nodes</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {runs.map(run => (
            <TableRow key={run.id}>
              <TableCell className="text-sm text-muted-foreground">
                {formatDate(run.startedAt)}
              </TableCell>
              <TableCell>
                <Badge variant={run.status === 'Failed' ? 'destructive' : 'outline'}>{run.status}</Badge>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {run.nodes.filter(n => n.status === 'Succeeded').length}/{run.nodes.length}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function TableSkeleton({ rows }: { rows: number }) {
  return (
    <div className="rounded-lg border p-4">
      <div className="space-y-3">
        {Array.from({ length: rows }).map((_, i) => (
          <Skeleton key={i} className="h-9 w-full" />
        ))}
      </div>
    </div>
  )
}

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}
