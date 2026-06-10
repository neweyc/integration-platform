import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { userTokensApi, type UserTokenSummary } from '@/api/userTokens'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { EmptyState } from '@/components/ui/empty-state'
import { KeyRound } from 'lucide-react'
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

export function UserTokensPage() {
  const queryClient = useQueryClient()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [name, setName] = useState('')
  const [formError, setFormError] = useState<string | null>(null)
  const [newToken, setNewToken] = useState<string | null>(null)

  const { data, isLoading, error } = useQuery({
    queryKey: ['user-tokens'],
    queryFn: userTokensApi.list,
  })

  const createToken = useMutation({
    mutationFn: () => userTokensApi.create(name),
    onSuccess: result => {
      queryClient.invalidateQueries({ queryKey: ['user-tokens'] })
      setNewToken(result.plaintextToken)
      setName('')
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const revokeToken = useMutation({
    mutationFn: (id: string) => userTokensApi.revoke(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['user-tokens'] }),
  })

  function handleOpenSheet() {
    setName('')
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
          <h2 className="text-xl font-semibold">Access tokens</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Personal access tokens for authenticating the <code className="text-xs">serto</code> CLI and API calls.
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
          onCreate={handleOpenSheet}
        />
      )}

      <Sheet open={sheetOpen} onOpenChange={handleCloseSheet}>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>New access token</SheetTitle>
          </SheetHeader>

          {newToken ? (
            <div className="flex flex-col gap-4 px-4">
              <div className="rounded-md border border-green-200 bg-green-50 p-4 dark:border-green-900 dark:bg-green-950">
                <p className="text-sm font-medium text-green-800 dark:text-green-200 mb-2">
                  Token created — copy it now. It will not be shown again.
                </p>
                <code className="block text-xs break-all text-green-900 dark:text-green-100 select-all">
                  {newToken}
                </code>
              </div>
              <p className="text-xs text-muted-foreground">
                Use this token with the CLI:
              </p>
              <code className="block rounded bg-muted p-3 text-xs break-all select-all">
                SERTO_API_TOKEN={newToken} serto deploy --url http://your-control-plane
              </code>
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
                  placeholder="e.g. laptop, CI pipeline"
                  value={name}
                  onChange={e => setName(e.target.value)}
                  required
                />
                <p className="text-xs text-muted-foreground">
                  A label to help you identify where this token is used.
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
  onCreate,
}: {
  tokens: UserTokenSummary[]
  onRevoke: (id: string) => void
  onCreate: () => void
}) {
  if (tokens.length === 0) {
    return (
      <EmptyState
        icon={KeyRound}
        title="No access tokens yet"
        description="Personal access tokens let the serto CLI and API authenticate as you. Create one, then pass it to serto login or set SERTO_API_TOKEN."
        primaryAction={{ label: 'New token', onClick: onCreate }}
      />
    )
  }

  return (
    <div className="border rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Created</TableHead>
            <TableHead>Last used</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {tokens.map(token => (
            <TableRow key={token.id}>
              <TableCell className="font-medium">{token.name}</TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {new Date(token.createdAt).toLocaleString()}
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {token.lastUsedAt ? new Date(token.lastUsedAt).toLocaleString() : 'Never'}
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
            <TableHead>Created</TableHead>
            <TableHead>Last used</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 3 }).map((_, i) => (
            <TableRow key={i}>
              <TableCell><Skeleton className="h-4 w-32" /></TableCell>
              <TableCell><Skeleton className="h-4 w-32" /></TableCell>
              <TableCell><Skeleton className="h-4 w-24" /></TableCell>
              <TableCell />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
