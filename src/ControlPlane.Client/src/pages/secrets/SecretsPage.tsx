import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { secretsApi, type SecretSummary } from '@/api/secrets'
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
import { getCurrentUser, hasPermission } from '@/lib/rbac'

const ENVIRONMENT = 'production'

export function SecretsPage() {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canViewSecrets = hasPermission('ViewSecrets', user)
  const canManageSecrets = hasPermission('ManageSecrets', user)
  const [sheetOpen, setSheetOpen] = useState(false)
  const [form, setForm] = useState({ key: '', value: '' })
  const [formError, setFormError] = useState<string | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['secrets', ENVIRONMENT],
    queryFn: () => secretsApi.list(ENVIRONMENT),
    enabled: canViewSecrets,
  })

  const setSecret = useMutation({
    mutationFn: () => secretsApi.set(ENVIRONMENT, form.key, form.value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['secrets', ENVIRONMENT] })
      setSheetOpen(false)
      setForm({ key: '', value: '' })
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const deleteSecret = useMutation({
    mutationFn: (key: string) => secretsApi.delete(ENVIRONMENT, key),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['secrets', ENVIRONMENT] }),
  })

  function handleOpenSheet() {
    setForm({ key: '', value: '' })
    setFormError(null)
    setSheetOpen(true)
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setFormError(null)
    setSecret.mutate()
  }

  if (!canViewSecrets) {
    return <AccessDenied title="Secrets unavailable" />
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Secrets</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Encrypted key/value pairs. Values are write-only and never returned by the API.
          </p>
        </div>
        {canManageSecrets && <Button onClick={handleOpenSheet}>New secret</Button>}
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load secrets.'}
        </p>
      )}

      {isLoading ? (
        <SecretsTableSkeleton />
      ) : (
        <SecretsTable
          secrets={data?.secrets ?? []}
          onDelete={key => deleteSecret.mutate(key)}
          canManageSecrets={canManageSecrets}
        />
      )}

      <Sheet open={sheetOpen} onOpenChange={setSheetOpen}>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>New secret</SheetTitle>
          </SheetHeader>
          <form onSubmit={handleSubmit} className="flex flex-col gap-4 px-4">
            <div className="space-y-2">
              <Label htmlFor="key">Key</Label>
              <Input
                id="key"
                placeholder="MY_SECRET_KEY"
                value={form.key}
                onChange={e =>
                  setForm(prev => ({
                    ...prev,
                    key: e.target.value.toUpperCase().replace(/\s+/g, '_'),
                  }))
                }
                required
              />
              <p className="text-xs text-muted-foreground">
                Uppercase letters, numbers, and underscores.
              </p>
            </div>
            <div className="space-y-2">
              <Label htmlFor="value">Value</Label>
              <Input
                id="value"
                type="password"
                placeholder="Secret value"
                value={form.value}
                onChange={e => setForm(prev => ({ ...prev, value: e.target.value }))}
                required
              />
            </div>
            {formError && <p className="text-sm text-destructive">{formError}</p>}
            <SheetFooter>
              <Button type="button" variant="outline" onClick={() => setSheetOpen(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={setSecret.isPending}>
                {setSecret.isPending ? 'Saving...' : 'Save secret'}
              </Button>
            </SheetFooter>
          </form>
        </SheetContent>
      </Sheet>
    </div>
  )
}

function SecretsTable({
  secrets,
  onDelete,
  canManageSecrets,
}: {
  secrets: SecretSummary[]
  onDelete: (key: string) => void
  canManageSecrets: boolean
}) {
  if (secrets.length === 0) {
    return (
      <div className="text-center py-16 text-muted-foreground border rounded-lg">
        <p className="text-sm">No secrets yet.</p>
        <p className="text-sm mt-1">Add your first secret to get started.</p>
      </div>
    )
  }

  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Key</TableHead>
            <TableHead>Last updated</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {secrets.map(secret => (
            <TableRow key={secret.id}>
              <TableCell>
                <code className="text-sm font-mono">{secret.key}</code>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {new Date(secret.updatedAt).toLocaleString()}
              </TableCell>
              <TableCell className="text-right">
                {canManageSecrets && (
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-destructive hover:text-destructive"
                    onClick={() => onDelete(secret.key)}
                  >
                    Delete
                  </Button>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function SecretsTableSkeleton() {
  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Key</TableHead>
            <TableHead>Last updated</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 4 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell><Skeleton className="h-4 w-40" /></TableCell>
              <TableCell><Skeleton className="h-4 w-32" /></TableCell>
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
