import { useMemo, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Package } from 'lucide-react'
import {
  packagesApi,
  type PackageMetadata,
  type ActivatePackageVersionResult,
} from '@/api/packages'
import { integrationsApi, type Integration } from '@/api/integrations'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { EmptyState } from '@/components/ui/empty-state'
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

  // Integrations tell us which version each one runs, so we can mark a version active vs stale and
  // count how many integrations a package activation would move.
  const {
    data: integrationData,
    isLoading: integrationsLoading,
    isError: integrationsError,
  } = useQuery({
    queryKey: ['integrations'],
    queryFn: () => integrationsApi.list(),
    enabled: canView,
  })

  // Until integrations have loaded we can't tell active from stale, so we must not present a package
  // as stale (or enable its delete) on incomplete data — the server guard is authoritative, but the
  // UI should not invite an action that will 409.
  const pinsResolved = !integrationsLoading && !integrationsError

  const deletePackage = useMutation({
    mutationFn: (id: string) => packagesApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['packages'] }),
  })

  const activate = useMutation({
    mutationFn: (id: string) => packagesApi.activate(id),
    onSuccess: result => {
      setActivateResult(result)
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
      queryClient.invalidateQueries({ queryKey: ['packages'] })
    },
  })

  const [filter, setFilter] = useState('')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [activateResult, setActivateResult] = useState<ActivatePackageVersionResult | null>(null)

  // Memoized so the empty-array fallback keeps a stable identity and doesn't re-trigger the useMemos below.
  const packages = useMemo(() => packageData?.packages ?? [], [packageData])
  const integrations = useMemo(() => integrationData?.integrations ?? [], [integrationData])

  // packageId -> integrations running that exact version.
  const pinnedBy = useMemo(() => {
    const map = new Map<string, Integration[]>()
    for (const integration of integrations) {
      if (!integration.packageId) continue
      map.set(integration.packageId, [...(map.get(integration.packageId) ?? []), integration])
    }
    return map
  }, [integrations])

  const groups = useMemo(() => groupByName(packages), [packages])

  // packageName -> how many integrations run some version of it. Activating a version moves all of them.
  const integrationCountByName = useMemo(() => {
    const packageIdToName = new Map(packages.map(p => [p.id, p.name]))
    const counts = new Map<string, number>()
    for (const integration of integrations) {
      if (!integration.packageId) continue
      const name = packageIdToName.get(integration.packageId)
      if (!name) continue
      counts.set(name, (counts.get(name) ?? 0) + 1)
    }
    return counts
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
    // Never delete on incomplete pin data, or when the version is in use.
    if (!pinsResolved || inUse) return
    if (!window.confirm(`Delete ${pkg.name} ${pkg.version}? This cannot be undone.`)) return
    deletePackage.mutate(pkg.id)
  }

  function handleActivate(pkg: PackageMetadata, integrationCount: number) {
    setActivateResult(null)
    const plural = integrationCount === 1 ? 'integration' : 'integrations'
    if (
      !window.confirm(
        `Make ${pkg.version} the active version for ${pkg.name}? ` +
          `All ${integrationCount} ${plural} in this package will run it on their next run.`
      )
    )
      return
    activate.mutate(pkg.id)
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold">Packages</h2>
        <p className="text-sm text-muted-foreground mt-0.5">
          Uploaded integration packages, grouped by name. Integrations are versioned per package:
          activating a version moves every integration in the package to it together.
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
      {activate.error && (
        <p className="text-sm text-destructive">
          {activate.error instanceof Error ? activate.error.message : 'Failed to activate version.'}
        </p>
      )}
      {activateResult && (
        <div className="text-sm rounded-md border px-3 py-2 bg-muted/40">
          Activated <span className="font-mono">{activateResult.version}</span> of {activateResult.packageName} for{' '}
          {activateResult.activated.length} integration{activateResult.activated.length === 1 ? '' : 's'}.
          {activateResult.skipped.length > 0 && (
            <span className="text-destructive">
              {' '}
              Skipped {activateResult.skipped.join(', ')} — this version does not contain their class.
            </span>
          )}
        </div>
      )}
      {downloadError && <p className="text-sm text-destructive">{downloadError}</p>}

      {isLoading ? (
        <PackagesSkeleton />
      ) : groups.length === 0 ? (
        <EmptyState
          icon={Package}
          title="No packages uploaded yet"
          description="A package is one built integration project, version-pinned. Deploy from your project with the serto CLI to publish the first one — it's auto-provisioned here."
        >
          <code className="block rounded bg-muted p-3 text-left text-xs break-all select-all">
            serto deploy
          </code>
        </EmptyState>
      ) : visibleGroups.length === 0 ? (
        <EmptyState title="No packages match your filter." />
      ) : (
        <div className="border rounded-lg divide-y">
          {visibleGroups.map(group => {
            const integrationCount = integrationCountByName.get(group.name) ?? 0
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
                          // Nothing to activate when every integration in the package already runs this version.
                          const activeEverywhere = integrationCount > 0 && users.length === integrationCount
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
                                    <Button
                                      variant="outline"
                                      size="sm"
                                      disabled={activeEverywhere || integrationCount === 0 || activate.isPending}
                                      title={
                                        integrationCount === 0
                                          ? 'No integrations use this package yet.'
                                          : activeEverywhere
                                            ? 'Already the active version.'
                                            : `Activate for all ${integrationCount} integration(s) in this package.`
                                      }
                                      onClick={() => handleActivate(pkg, integrationCount)}
                                    >
                                      Activate
                                    </Button>
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
                                              ? `In use by: ${users.map(u => u.name).join(', ')}`
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
