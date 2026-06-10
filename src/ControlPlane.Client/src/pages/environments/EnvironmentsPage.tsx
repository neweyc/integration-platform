import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { environmentsApi, type EnvironmentSummary } from '@/api/environments'
import { useEnvironments } from '@/hooks/useEnvironments'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
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
  SheetFooter,
} from '@/components/ui/sheet'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { EmptyState } from '@/components/ui/empty-state'
import { Layers } from 'lucide-react'
import { getCurrentUser, hasPermission } from '@/lib/rbac'

export function EnvironmentsPage() {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canView = hasPermission('ViewEnvironments', user)
  const canManage = hasPermission('ManageEnvironments', user)

  const [sheetOpen, setSheetOpen] = useState(false)
  const [formMode, setFormMode] = useState<'create' | 'edit'>('create')
  const [form, setForm] = useState({ name: '', displayName: '', description: '', sortOrder: 0, isDefault: false })
  const [formError, setFormError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const { data, isLoading, error } = useEnvironments(canView)

  // The plan caps how many environments a tenant may create (null = unlimited).
  const maxEnvironments = data?.maxEnvironments ?? null
  const atEnvironmentLimit =
    maxEnvironments != null && (data?.environments.length ?? 0) >= maxEnvironments

  const createEnvironment = useMutation({
    mutationFn: () =>
      environmentsApi.create({
        name: form.name,
        displayName: form.displayName || null,
        description: form.description || null,
        sortOrder: form.sortOrder,
        isDefault: form.isDefault,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['environments'] })
      setSheetOpen(false)
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const updateEnvironment = useMutation({
    mutationFn: () =>
      environmentsApi.update(form.name, {
        displayName: form.displayName || null,
        description: form.description || null,
        sortOrder: form.sortOrder,
        isDefault: form.isDefault,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['environments'] })
      setSheetOpen(false)
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const deleteEnvironment = useMutation({
    mutationFn: (name: string) => environmentsApi.delete(name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['environments'] }),
    onError: (err: Error) => setActionError(err.message),
  })

  function handleOpenCreate() {
    setFormMode('create')
    setForm({ name: '', displayName: '', description: '', sortOrder: 0, isDefault: false })
    setFormError(null)
    setSheetOpen(true)
  }

  function handleOpenEdit(env: EnvironmentSummary) {
    setFormMode('edit')
    setForm({
      name: env.name,
      displayName: env.displayName,
      description: env.description ?? '',
      sortOrder: env.sortOrder,
      isDefault: env.isDefault,
    })
    setFormError(null)
    setSheetOpen(true)
  }

  const saving = formMode === 'create' ? createEnvironment.isPending : updateEnvironment.isPending

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setFormError(null)
    if (formMode === 'create') createEnvironment.mutate()
    else updateEnvironment.mutate()
  }

  if (!canView) {
    return <AccessDenied title="Environments unavailable" />
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Environments</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            The deployment environments integrations, secrets, agent tokens, and workflows are scoped to.
            Names are canonicalized to lowercase.
          </p>
        </div>
        {canManage && (
          <div className="flex flex-col items-end gap-1">
            <Button onClick={handleOpenCreate} disabled={atEnvironmentLimit}>New environment</Button>
            {atEnvironmentLimit && (
              <p className="text-xs text-muted-foreground">
                {data!.environments.length} / {maxEnvironments} on your plan — upgrade to add more.
              </p>
            )}
          </div>
        )}
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load environments.'}
        </p>
      )}
      {actionError && <p className="text-sm text-destructive">{actionError}</p>}

      {isLoading ? (
        <EnvironmentsTableSkeleton />
      ) : (
        <EnvironmentsTable
          environments={data?.environments ?? []}
          canManage={canManage}
          onCreate={handleOpenCreate}
          onEdit={handleOpenEdit}
          onDelete={name => {
            setActionError(null)
            deleteEnvironment.mutate(name)
          }}
        />
      )}

      <Sheet open={sheetOpen} onOpenChange={setSheetOpen}>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>{formMode === 'create' ? 'New environment' : 'Edit environment'}</SheetTitle>
          </SheetHeader>
          <form onSubmit={handleSubmit} className="flex flex-col gap-4 px-4">
            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                placeholder="staging"
                value={form.name}
                onChange={e =>
                  setForm(prev => ({ ...prev, name: e.target.value.toLowerCase().trim() }))
                }
                disabled={formMode === 'edit'}
                required
              />
              <p className="text-xs text-muted-foreground">
                {formMode === 'edit'
                  ? 'The name is the key other records reference and cannot be changed.'
                  : 'Lowercase letters, numbers, and hyphens. This is the value used everywhere else.'}
              </p>
            </div>
            <div className="space-y-2">
              <Label htmlFor="displayName">Display name</Label>
              <Input
                id="displayName"
                placeholder="Staging"
                value={form.displayName}
                onChange={e => setForm(prev => ({ ...prev, displayName: e.target.value }))}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="description">Description</Label>
              <Input
                id="description"
                placeholder="Optional"
                value={form.description}
                onChange={e => setForm(prev => ({ ...prev, description: e.target.value }))}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="sortOrder">Sort order</Label>
              <Input
                id="sortOrder"
                type="number"
                value={form.sortOrder}
                onChange={e => setForm(prev => ({ ...prev, sortOrder: Number(e.target.value) || 0 }))}
              />
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={form.isDefault}
                disabled={formMode === 'edit' && form.isDefault}
                onChange={e => setForm(prev => ({ ...prev, isDefault: e.target.checked }))}
              />
              Make this the default environment
            </label>
            {formMode === 'edit' && form.isDefault && (
              <p className="text-xs text-muted-foreground">
                To change the default, edit another environment and make it the default instead.
              </p>
            )}
            {formError && <p className="text-sm text-destructive">{formError}</p>}
            <SheetFooter>
              <Button type="button" variant="outline" onClick={() => setSheetOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Saving...' : formMode === 'create' ? 'Create environment' : 'Save changes'}
              </Button>
            </SheetFooter>
          </form>
        </SheetContent>
      </Sheet>
    </div>
  )
}

function EnvironmentsTable({
  environments,
  canManage,
  onCreate,
  onEdit,
  onDelete,
}: {
  environments: EnvironmentSummary[]
  canManage: boolean
  onCreate: () => void
  onEdit: (env: EnvironmentSummary) => void
  onDelete: (name: string) => void
}) {
  if (environments.length === 0) {
    return (
      <EmptyState
        icon={Layers}
        title="No environments yet"
        description="Environments (like production and staging) scope your integrations, secrets, and agent tokens. Every tenant starts with a production environment — add more to separate your deployments."
        primaryAction={canManage ? { label: 'New environment', onClick: onCreate } : undefined}
      />
    )
  }

  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Display name</TableHead>
            <TableHead>Description</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {environments.map(environment => (
            <TableRow key={environment.name}>
              <TableCell>
                <code className="text-sm font-mono">{environment.name}</code>
                {environment.isDefault && (
                  <span className="ml-2 text-xs text-muted-foreground">(default)</span>
                )}
              </TableCell>
              <TableCell className="text-sm">{environment.displayName}</TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {environment.description ?? '—'}
              </TableCell>
              <TableCell className="text-right">
                {canManage && (
                  <>
                    <Button variant="ghost" size="sm" onClick={() => onEdit(environment)}>
                      Edit
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="text-destructive hover:text-destructive"
                      disabled={environment.isDefault}
                      title={environment.isDefault ? 'The default environment cannot be deleted' : undefined}
                      onClick={() => onDelete(environment.name)}
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

function EnvironmentsTableSkeleton() {
  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Display name</TableHead>
            <TableHead>Description</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 3 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell><Skeleton className="h-4 w-24" /></TableCell>
              <TableCell><Skeleton className="h-4 w-24" /></TableCell>
              <TableCell><Skeleton className="h-4 w-40" /></TableCell>
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
