import { useMemo, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { ChevronRight } from 'lucide-react'
import { packagesApi, type PackageMetadata } from '@/api/packages'
import { integrationsApi, type Integration } from '@/api/integrations'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
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
  const canManagePackages = hasPermission('ManagePackages', user)
  const canActivate = hasPermission('ManageIntegrations', user)

  const { data: packageData, isLoading, error } = useQuery({
    queryKey: ['packages'],
    queryFn: packagesApi.list,
    enabled: canView,
  })

  // Integrations tell us which package version each one is pinned to. We need this both to mark a
  // version "in use" and to know which integration(s) an "Activate" would repoint.
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
  const pinsResolved = !integrationsLoading && !integrationsError

  const deletePackage = useMutation({
    mutationFn: (id: string) => packagesApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['packages'] }),
  })

  const repoint = useMutation({
    mutationFn: ({ integrationId, packageId }: { integrationId: string; packageId: string }) =>
      integrationsApi.repoint(integrationId, packageId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
      queryClient.invalidateQueries({ queryKey: ['packages'] })
    },
  })

  const [filter, setFilter] = useState('')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const packages = packageData?.packages ?? []
  const integrations = integrationData?.integrations ?? []

  // packageId -> integrations pinned to that exact version.
  const pinnedBy = useMemo(() => {
    const map = new Map<string, Integration[]>()
    for (const integration of integrations) {
      if (!integration.packageId) continue
      map.set(integration.packageId, [...(map.get(integration.packageId) ?? []), integration])
    }
    return map
  }, [integrations])

  const groups = useMemo(() => groupByName(packages), [packages])

  // Integrations that use *some* version of a given package name. These are the candidates an
  // "Activate" can repoint — including for a stale version that nothing is currently pinned to.
  const candidatesByName = useMemo(() => {
    const packageIdToName = new Map(packages.map(p => [p.id, p.name]))
    const map = new Map<string, Integration[]>()
    for (const integration of integrations) {
      if (!integration.packageId) continue
      const name = packageIdToName.get(integration.packageId)
      if (!name) continue
      map.set(name, [...(map.get(name) ?? []), integration])
    }
    return map
  }, [packages, integrations])

  const visibleGroups = useMemo(() => {
    const needle = filter.trim().toLowerCase()
    if (!needle) return groups
    return groups.filter(g => g.name.toLowerCase().includes(needle))
  }, [groups, filter])

  if (!canView) {
    return <AccessDenied title="Packages unavailable" />
  }

  function toggle(name: string) {
    setExpanded(prev => {
      const next = new Set(prev)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })
  }

  async function handleDownload(pkg: PackageMetadata) {
    try {
      setDownloadError(null)
      await packagesApi.download(pkg.id, `${pkg.name}.${pkg.version}.zip`)
    } catch (err) {
      setDownloadError(err instanceof Error ? err.message : 'Download failed.')
    }
  }

  function handleDelete(pkg: PackageMetadata, inUse: boolean) {
    // Never delete on incomplete pin data, or when the version is pinned.
    if (!pinsResolved || inUse) return
    if (!window.confirm(`Delete ${pkg.name} ${pkg.version}? This cannot be undone.`)) return
    deletePackage.mutate(pkg.id)
  }

  function handleActivate(integrationId: string, pkg: PackageMetadata) {
    repoint.mutate({ integrationId, packageId: pkg.id })
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold">Packages</h2>
        <p className="text-sm text-muted-foreground mt-0.5">
          Uploaded integration packages, grouped by name. Expand a package to see every version,
          activate a version for an integration, or delete versions that are no longer in use.
        </p>
      </div>

      <Input
        placeholder="Filter by package name…"
        value={filter}
        onChange={e => setFilter(e.target.value)}
        className="max-w-sm"
      />

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
      {repoint.error && (
        <p className="text-sm text-destructive">
          {repoint.error instanceof Error ? repoint.error.message : 'Failed to activate version.'}
        </p>
      )}
      {downloadError && <p className="text-sm text-destructive">{downloadError}</p>}

      {isLoading ? (
        <PackagesSkeleton />
      ) : groups.length === 0 ? (
        <EmptyState message="No packages uploaded yet." hint="Deploy an integration with the CLI to publish one." />
      ) : visibleGroups.length === 0 ? (
        <EmptyState message="No packages match your filter." />
      ) : (
        <div className="border rounded-lg divide-y">
          {visibleGroups.map(group => {
            const candidates = candidatesByName.get(group.name) ?? []
            const activeVersions = group.versions.filter(v => (pinnedBy.get(v.id)?.length ?? 0) > 0)
            const isOpen = expanded.has(group.name)
            return (
              <div key={group.name}>
                <button
                  type="button"
                  onClick={() => toggle(group.name)}
                  className="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-muted/50"
                >
                  <ChevronRight
                    className={`h-4 w-4 text-muted-foreground transition-transform ${isOpen ? 'rotate-90' : ''}`}
                  />
                  <span className="font-medium">{group.name}</span>
                  <span className="text-sm text-muted-foreground">
                    {group.versions.length} version{group.versions.length === 1 ? '' : 's'}
                  </span>
                  <span className="flex-1" />
                  {pinsResolved && (
                    activeVersions.length > 0 ? (
                      <span className="flex items-center gap-1.5">
                        {activeVersions.map(v => (
                          <Badge key={v.id} variant="default" className="font-mono font-normal">
                            {v.version}
                          </Badge>
                        ))}
                      </span>
                    ) : (
                      <Badge variant="secondary">no active version</Badge>
                    )
                  )}
                </button>

                {isOpen && (
                  <div className="px-4 pb-3">
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
                                {!pinsResolved ? (
                                  <Badge variant="outline">checking…</Badge>
                                ) : inUse ? (
                                  <Badge variant="default" title={`In use by: ${users.map(u => u.name).join(', ')}`}>
                                    active{users.length > 1 ? ` ×${users.length}` : ''}
                                  </Badge>
                                ) : (
                                  <Badge variant="secondary">stale</Badge>
                                )}
                              </TableCell>
                              <TableCell className="text-right">
                                <div className="flex items-center justify-end gap-2">
                                  {canActivate && pinsResolved && (
                                    <ActivateControl
                                      pkg={pkg}
                                      candidates={candidates}
                                      pinnedIntegrationIds={new Set(users.map(u => u.id))}
                                      pending={repoint.isPending}
                                      onActivate={handleActivate}
                                    />
                                  )}
                                  {/* Download and delete require ManagePackages on the backend. */}
                                  {canManagePackages && (
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
                                        disabled={!pinsResolved || inUse || deletePackage.isPending}
                                        title={
                                          !pinsResolved
                                            ? 'Checking which integrations use this version…'
                                            : inUse
                                              ? `Pinned by: ${users.map(u => u.name).join(', ')}`
                                              : undefined
                                        }
                                        onClick={() => handleDelete(pkg, inUse)}
                                      >
                                        Delete
                                      </Button>
                                    </>
                                  )}
                                </div>
                              </TableCell>
                            </TableRow>
                          )
                        })}
                      </TableBody>
                    </Table>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

interface ActivateControlProps {
  pkg: PackageMetadata
  candidates: Integration[]
  pinnedIntegrationIds: Set<string>
  pending: boolean
  onActivate: (integrationId: string, pkg: PackageMetadata) => void
}

// Activating a version means repointing an integration to it. With a single candidate integration
// this is one click; with several, the user picks which integration to repoint. Integrations already
// pinned to this exact version are excluded — there is nothing to activate for them.
function ActivateControl({ pkg, candidates, pinnedIntegrationIds, pending, onActivate }: ActivateControlProps) {
  const [picking, setPicking] = useState(false)
  const targets = candidates.filter(c => !pinnedIntegrationIds.has(c.id))

  if (targets.length === 0) return null

  if (targets.length === 1) {
    return (
      <Button variant="outline" size="sm" disabled={pending} onClick={() => onActivate(targets[0].id, pkg)}>
        Activate
      </Button>
    )
  }

  if (!picking) {
    return (
      <Button variant="outline" size="sm" disabled={pending} onClick={() => setPicking(true)}>
        Activate…
      </Button>
    )
  }

  return (
    <div className="flex items-center gap-1 flex-wrap justify-end">
      <span className="text-xs text-muted-foreground">For:</span>
      {targets.map(target => (
        <Button
          key={target.id}
          variant="outline"
          size="sm"
          disabled={pending}
          onClick={() => {
            setPicking(false)
            onActivate(target.id, pkg)
          }}
        >
          {target.name}
        </Button>
      ))}
      <Button variant="ghost" size="sm" onClick={() => setPicking(false)}>
        Cancel
      </Button>
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

function EmptyState({ message, hint }: { message: string; hint?: string }) {
  return (
    <div className="text-center py-16 text-muted-foreground border rounded-lg">
      <p className="text-sm">{message}</p>
      {hint && <p className="text-sm mt-1">{hint}</p>}
    </div>
  )
}

function PackagesSkeleton() {
  return (
    <div className="border rounded-lg divide-y">
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="flex items-center gap-3 px-4 py-3">
          <Skeleton className="h-4 w-4" />
          <Skeleton className="h-4 w-40" />
          <Skeleton className="h-4 w-20" />
        </div>
      ))}
    </div>
  )
}
