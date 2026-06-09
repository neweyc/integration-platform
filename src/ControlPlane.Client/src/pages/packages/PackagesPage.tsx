import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { packagesApi, type PackageMetadata } from '@/api/packages'
import { integrationsApi } from '@/api/integrations'
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
import { AccessDenied } from '@/components/layout/AccessDenied'
import { getCurrentUser, hasPermission } from '@/lib/rbac'

export function PackagesPage() {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canView = hasPermission('ViewIntegrations', user)
  const canManage = hasPermission('ManagePackages', user)

  const { data: packageData, isLoading, error } = useQuery({
    queryKey: ['packages'],
    queryFn: packagesApi.list,
    enabled: canView,
  })

  // Integrations are loaded to show which packages are in use (Integration.packageId join).
  const {
    data: integrationData,
    isLoading: integrationsLoading,
    isError: integrationsError,
  } = useQuery({
    queryKey: ['integrations'],
    queryFn: () => integrationsApi.list(),
    enabled: canView,
  })

  // Until integrations have loaded we can't tell in-use from stale, so we must not present a package
  // as stale (or enable its delete) on incomplete data — the server guard is authoritative, but the
  // UI should not invite an action that will 409.
  const inUseResolved = !integrationsLoading && !integrationsError

  const deletePackage = useMutation({
    mutationFn: (id: string) => packagesApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['packages'] }),
  })

  const [downloadError, setDownloadError] = useState<string | null>(null)

  async function handleDownload(pkg: PackageMetadata) {
    try {
      setDownloadError(null)
      await packagesApi.download(pkg.id, `${pkg.name}.${pkg.version}.zip`)
    } catch (err) {
      setDownloadError(err instanceof Error ? err.message : 'Download failed.')
    }
  }

  if (!canView) {
    return <AccessDenied title="Packages unavailable" />
  }

  const packages = packageData?.packages ?? []
  const integrations = integrationData?.integrations ?? []

  // packageId -> names of integrations pinned to it.
  const pinnedBy = new Map<string, string[]>()
  for (const integration of integrations) {
    if (!integration.packageId) continue
    pinnedBy.set(integration.packageId, [...(pinnedBy.get(integration.packageId) ?? []), integration.name])
  }

  const groups = groupByName(packages)

  function handleDelete(pkg: PackageMetadata, users: string[]) {
    // Never delete on incomplete in-use data, or when the version is pinned.
    if (!inUseResolved || users.length > 0) return
    if (!window.confirm(`Delete ${pkg.name} ${pkg.version}? This cannot be undone.`)) return
    deletePackage.mutate(pkg.id)
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold">Packages</h2>
        <p className="text-sm text-muted-foreground mt-0.5">
          Uploaded integration packages and their versions. Set the active version from an integration's
          history; delete stale versions that are no longer in use.
        </p>
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load packages.'}
        </p>
      )}

      {deletePackage.error && (
        <p className="text-sm text-destructive">
          {deletePackage.error instanceof Error ? deletePackage.error.message : 'Failed to delete package.'}
        </p>
      )}

      {downloadError && <p className="text-sm text-destructive">{downloadError}</p>}

      {isLoading ? (
        <PackagesSkeleton />
      ) : groups.length === 0 ? (
        <div className="text-center py-16 text-muted-foreground border rounded-lg">
          <p className="text-sm">No packages uploaded yet.</p>
          <p className="text-sm mt-1">Deploy an integration with the CLI to publish one.</p>
        </div>
      ) : (
        <div className="space-y-6">
          {groups.map(group => (
            <div key={group.name} className="space-y-2">
              <h3 className="text-sm font-medium">{group.name}</h3>
              <div className="border rounded-lg">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Version</TableHead>
                      <TableHead>Uploaded</TableHead>
                      <TableHead>Size</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {group.versions.map(pkg => {
                      const users = pinnedBy.get(pkg.id) ?? []
                      const inUse = users.length > 0
                      return (
                        <TableRow key={pkg.id}>
                          <TableCell className="font-mono text-sm">{pkg.version}</TableCell>
                          <TableCell className="text-sm text-muted-foreground">
                            {new Date(pkg.createdAt).toLocaleString()}
                          </TableCell>
                          <TableCell className="text-sm text-muted-foreground">
                            {formatBytes(pkg.sizeBytes)}
                          </TableCell>
                          <TableCell>
                            {!inUseResolved ? (
                              <Badge variant="outline">checking…</Badge>
                            ) : inUse ? (
                              <Badge variant="default" title={`In use by: ${users.join(', ')}`}>
                                in use{users.length > 1 ? ` ×${users.length}` : ''}
                              </Badge>
                            ) : (
                              <Badge variant="secondary">stale</Badge>
                            )}
                          </TableCell>
                          <TableCell className="text-right space-x-2">
                            {/* Download and delete both require ManagePackages on the backend, so they
                                are only shown to users who can actually perform them. */}
                            {canManage && (
                              <>
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="text-muted-foreground hover:text-foreground"
                                  onClick={() => handleDownload(pkg)}
                                >
                                  Download
                                </Button>
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="text-destructive hover:text-destructive disabled:opacity-40"
                                  disabled={!inUseResolved || inUse || deletePackage.isPending}
                                  title={
                                    !inUseResolved
                                      ? 'Checking which integrations use this version…'
                                      : inUse
                                        ? `Pinned by: ${users.join(', ')}`
                                        : undefined
                                  }
                                  onClick={() => handleDelete(pkg, users)}
                                >
                                  Delete
                                </Button>
                              </>
                            )}
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

interface PackageGroup {
  name: string
  versions: PackageMetadata[]
}

function groupByName(packages: PackageMetadata[]): PackageGroup[] {
  const byName = new Map<string, PackageMetadata[]>()
  for (const pkg of packages) {
    byName.set(pkg.name, [...(byName.get(pkg.name) ?? []), pkg])
  }

  return [...byName.entries()]
    .map(([name, versions]) => ({
      name,
      versions: versions.sort(
        (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      ),
    }))
    .sort((a, b) => a.name.localeCompare(b.name))
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function PackagesSkeleton() {
  return (
    <div className="space-y-2">
      <Skeleton className="h-4 w-48" />
      <div className="border rounded-lg">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Version</TableHead>
              <TableHead>Uploaded</TableHead>
              <TableHead>Size</TableHead>
              <TableHead>Status</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {Array.from({ length: 3 }).map((_, i) => (
              <TableRow key={i}>
                <TableCell><Skeleton className="h-4 w-40" /></TableCell>
                <TableCell><Skeleton className="h-4 w-32" /></TableCell>
                <TableCell><Skeleton className="h-4 w-16" /></TableCell>
                <TableCell><Skeleton className="h-4 w-16" /></TableCell>
                <TableCell />
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}
