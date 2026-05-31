import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { agentTokensApi, type AgentTokenSummary } from '@/api/agentTokens'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Badge } from '@/components/ui/badge'
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

export function AgentTokensPage() {
  const queryClient = useQueryClient()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [form, setForm] = useState({ name: '', environment: '' })
  const [formError, setFormError] = useState<string | null>(null)
  const [newToken, setNewToken] = useState<string | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['agent-tokens'],
    queryFn: agentTokensApi.list,
  })

  const createToken = useMutation({
    mutationFn: () => agentTokensApi.create(form),
    onSuccess: result => {
      queryClient.invalidateQueries({ queryKey: ['agent-tokens'] })
      setNewToken(result.token)
      setForm({ name: '', environment: '' })
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const revokeToken = useMutation({
    mutationFn: (id: string) => agentTokensApi.revoke(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['agent-tokens'] }),
  })

  function handleOpenSheet() {
    setForm({ name: '', environment: '' })
    setFormError(null)
    setNewToken(null)
    setSheetOpen(true)
  }

  function handleCloseSheet() {
    setSheetOpen(false)
    setNewToken(null)
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setFormError(null)
    createToken.mutate()
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold">Agent tokens</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Service tokens that allow a runtime agent to fetch secrets for a specific environment.
          </p>
        </div>
        <Button onClick={handleOpenSheet}>New token</Button>
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load tokens.'}
        </p>
      )}

      {isLoading ? (
        <TokensTableSkeleton />
      ) : (
        <TokensTable
          tokens={data?.tokens ?? []}
          onRevoke={id => revokeToken.mutate(id)}
        />
      )}

      <Sheet open={sheetOpen} onOpenChange={handleCloseSheet}>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>New agent token</SheetTitle>
          </SheetHeader>

          {newToken ? (
            // Show the plaintext token once after creation
            <div className="flex flex-col gap-4 px-4">
              <div className="rounded-md border border-green-200 bg-green-50 p-4 dark:border-green-900 dark:bg-green-950">
                <p className="text-sm font-medium text-green-800 dark:text-green-200 mb-2">
                  Token created — copy it now. It will not be shown again.
                </p>
                <code className="block text-xs break-all text-green-900 dark:text-green-100 select-all">
                  {newToken}
                </code>
              </div>
              <SheetFooter>
                <Button onClick={handleCloseSheet}>Done</Button>
              </SheetFooter>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="flex flex-col gap-4 px-4">
              <div className="space-y-2">
                <Label htmlFor="name">Name</Label>
                <Input
                  id="name"
                  placeholder="Production agent"
                  value={form.name}
                  onChange={e => setForm(prev => ({ ...prev, name: e.target.value }))}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="environment">Environment</Label>
                <Input
                  id="environment"
                  placeholder="production"
                  value={form.environment}
                  onChange={e =>
                    setForm(prev => ({
                      ...prev,
                      environment: e.target.value.toLowerCase().trim(),
                    }))
                  }
                  required
                />
                <p className="text-xs text-muted-foreground">
                  This token will only be able to fetch secrets for this environment.
                </p>
              </div>
              {formError && <p className="text-sm text-destructive">{formError}</p>}
              <SheetFooter>
                <Button type="button" variant="outline" onClick={handleCloseSheet}>
                  Cancel
                </Button>
                <Button type="submit" disabled={createToken.isPending}>
                  {createToken.isPending ? 'Creating...' : 'Create token'}
                </Button>
              </SheetFooter>
            </form>
          )}
        </SheetContent>
      </Sheet>
    </div>
  )
}

function TokensTable({
  tokens,
  onRevoke,
}: {
  tokens: AgentTokenSummary[]
  onRevoke: (id: string) => void
}) {
  if (tokens.length === 0) {
    return (
      <div className="text-center py-16 text-muted-foreground border rounded-lg">
        <p className="text-sm">No agent tokens yet.</p>
        <p className="text-sm mt-1">Create a token to allow a runtime agent to fetch secrets.</p>
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
            <TableHead>Created</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {tokens.map(token => (
            <TableRow key={token.id}>
              <TableCell className="font-medium">{token.name}</TableCell>
              <TableCell>
                <Badge variant="outline">{token.environment}</Badge>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {new Date(token.createdAt).toLocaleString()}
              </TableCell>
              <TableCell className="text-right">
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-destructive hover:text-destructive"
                  onClick={() => onRevoke(token.id)}
                >
                  Revoke
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function TokensTableSkeleton() {
  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Environment</TableHead>
            <TableHead>Created</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 3 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell><Skeleton className="h-4 w-32" /></TableCell>
              <TableCell><Skeleton className="h-4 w-20" /></TableCell>
              <TableCell><Skeleton className="h-4 w-32" /></TableCell>
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
