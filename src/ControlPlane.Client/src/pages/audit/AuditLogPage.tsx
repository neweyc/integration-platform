import { useQuery } from '@tanstack/react-query'
import { auditLogApi, type AuditLogEntry } from '@/api/auditLog'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { EmptyState } from '@/components/ui/empty-state'
import { ScrollText } from 'lucide-react'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { getCurrentUser, hasPermission } from '@/lib/rbac'

export function AuditLogPage() {
  const user = getCurrentUser()
  const canViewAuditLog = hasPermission('ViewAuditLog', user)

  const { data, isLoading, error, refetch, isFetching } = useQuery({
    queryKey: ['audit-log'],
    queryFn: () => auditLogApi.list(100),
    enabled: canViewAuditLog,
  })

  if (!canViewAuditLog) {
    return <AccessDenied title="Audit log unavailable" />
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xl font-semibold">Audit log</h2>
          <p className="mt-0.5 text-sm text-muted-foreground">
            Tenant-scoped security and configuration changes.
          </p>
        </div>
        <Button variant="outline" onClick={() => refetch()} disabled={isFetching}>
          {isFetching ? 'Refreshing...' : 'Refresh'}
        </Button>
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load audit entries.'}
        </p>
      )}

      {isLoading ? (
        <AuditLogSkeleton />
      ) : (
        <AuditLogTable entries={data?.entries ?? []} />
      )}
    </div>
  )
}

function AuditLogTable({ entries }: { entries: AuditLogEntry[] }) {
  if (entries.length === 0) {
    return (
      <EmptyState
        icon={ScrollText}
        title="No audit entries yet"
        description="Security and configuration changes — logins, secret edits, deploys, role changes — are recorded here as they happen."
      />
    )
  }

  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="min-w-44">When</TableHead>
            <TableHead>Action</TableHead>
            <TableHead>Actor</TableHead>
            <TableHead>Target</TableHead>
            <TableHead className="min-w-80">Summary</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {entries.map(entry => (
            <TableRow key={entry.id}>
              <TableCell className="text-sm text-muted-foreground">
                {formatDate(entry.occurredAt)}
              </TableCell>
              <TableCell>
                <Badge variant="outline">{formatAction(entry.action)}</Badge>
              </TableCell>
              <TableCell>
                <div className="max-w-56 truncate font-medium">{entry.actorEmail || 'System'}</div>
              </TableCell>
              <TableCell>
                <div className="space-y-1">
                  <div className="text-sm font-medium">{entry.targetType}</div>
                  {entry.targetId && (
                    <div className="max-w-64 truncate text-xs text-muted-foreground">
                      {entry.targetId}
                    </div>
                  )}
                </div>
              </TableCell>
              <TableCell className="whitespace-normal text-sm text-muted-foreground">
                {entry.summary ?? '-'}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function AuditLogSkeleton() {
  return (
    <div className="rounded-lg border p-4">
      <div className="space-y-3">
        {Array.from({ length: 8 }).map((_, i) => (
          <Skeleton key={i} className="h-9 w-full" />
        ))}
      </div>
    </div>
  )
}

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}

function formatAction(action: string) {
  return action.replace(/([a-z])([A-Z])/g, '$1 $2')
}
