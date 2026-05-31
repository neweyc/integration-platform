import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  integrationsApi,
  type Integration,
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

type FormMode = 'create' | 'edit'

interface FormState {
  name: string
  slug: string
  description: string
  environment: string
  triggerType: TriggerType
  cronExpression: string
  className: string
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
  status: 'Enabled',
}

export function IntegrationsPage() {
  const queryClient = useQueryClient()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [formMode, setFormMode] = useState<FormMode>('create')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['integrations'],
    queryFn: () => integrationsApi.list(),
  })

  const createIntegration = useMutation({
    mutationFn: (data: CreateIntegrationRequest) => integrationsApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['integrations'] })
      setSheetOpen(false)
      setForm(emptyForm)
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
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const deleteIntegration = useMutation({
    mutationFn: (id: string) => integrationsApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['integrations'] }),
  })

  function handleOpenCreate() {
    setFormMode('create')
    setForm(emptyForm)
    setFormError(null)
    setEditingId(null)
    setSheetOpen(true)
  }

  function handleOpenEdit(integration: Integration) {
    setFormMode('edit')
    setForm({
      name: integration.name,
      slug: integration.slug,
      description: integration.description ?? '',
      environment: integration.environment,
      triggerType: integration.triggerType,
      cronExpression: integration.cronExpression ?? '',
      className: integration.className,
      status: integration.status,
    })
    setFormError(null)
    setEditingId(integration.id)
    setSheetOpen(true)
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setFormError(null)

    if (formMode === 'create') {
      createIntegration.mutate({
        name: form.name,
        slug: form.slug,
        description: form.description || undefined,
        environment: form.environment,
        triggerType: form.triggerType,
        cronExpression: form.triggerType === 'Scheduled' ? form.cronExpression : undefined,
        className: form.className,
      })
    } else if (editingId) {
      updateIntegration.mutate({
        id: editingId,
        data: {
          name: form.name,
          description: form.description || undefined,
          status: form.status,
          cronExpression: form.triggerType === 'Scheduled' ? form.cronExpression : undefined,
        },
      })
    }
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

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Integrations</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Manage your integration definitions.
          </p>
        </div>
        <Button onClick={handleOpenCreate}>New integration</Button>
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load integrations.'}
        </p>
      )}

      {isLoading ? (
        <IntegrationsTableSkeleton />
      ) : (
        <IntegrationsTable
          integrations={data?.integrations ?? []}
          onEdit={handleOpenEdit}
          onDelete={id => deleteIntegration.mutate(id)}
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
    </div>
  )
}

function IntegrationsTable({
  integrations,
  onEdit,
  onDelete,
}: {
  integrations: Integration[]
  onEdit: (integration: Integration) => void
  onDelete: (id: string) => void
}) {
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
              <TableCell className="text-right space-x-2">
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
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
