import { useQuery } from '@tanstack/react-query'
import { integrationsApi, type Integration } from '@/api/integrations'
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

export function IntegrationsPage() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['integrations'],
    queryFn: () => integrationsApi.list(),
  })

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Integrations</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Manage your integration definitions.
          </p>
        </div>
        <Button>New integration</Button>
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load integrations.'}
        </p>
      )}

      {isLoading ? (
        <IntegrationsTableSkeleton />
      ) : (
        <IntegrationsTable integrations={data?.integrations ?? []} />
      )}
    </div>
  )
}

function IntegrationsTable({ integrations }: { integrations: Integration[] }) {
  if (integrations.length === 0) {
    return (
      <div className="text-center py-16 text-muted-foreground border rounded-lg">
        <p className="text-sm">No integrations yet.</p>
        <p className="text-sm mt-1">Create your first one to get started.</p>
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
          </TableRow>
        </TableHeader>
        <TableBody>
          {integrations.map(integration => (
            <TableRow key={integration.id}>
              <TableCell>
                <div>
                  <p className="font-medium">{integration.name}</p>
                  {integration.description && (
                    <p className="text-xs text-muted-foreground mt-0.5">
                      {integration.description}
                    </p>
                  )}
                </div>
              </TableCell>
              <TableCell>
                <Badge variant="outline">{integration.environment}</Badge>
              </TableCell>
              <TableCell>
                <span className="text-sm">{integration.triggerType}</span>
                {integration.cronExpression && (
                  <code className="ml-2 text-xs text-muted-foreground bg-muted px-1.5 py-0.5 rounded">
                    {integration.cronExpression}
                  </code>
                )}
              </TableCell>
              <TableCell>
                <StatusBadge status={integration.status} />
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
    <Badge variant={status === 'Enabled' ? 'default' : 'secondary'}>
      {status}
    </Badge>
  )
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
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 4 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell><Skeleton className="h-4 w-40" /></TableCell>
              <TableCell><Skeleton className="h-4 w-20" /></TableCell>
              <TableCell><Skeleton className="h-4 w-24" /></TableCell>
              <TableCell><Skeleton className="h-4 w-16" /></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
