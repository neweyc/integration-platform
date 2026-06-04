import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi, type UserSummary } from '@/api/auth'
import {
  invitationsApi,
  type InvitationSummary,
  type InviteUserResponse,
} from '@/api/invitations'
import { AccessDenied } from '@/components/layout/AccessDenied'
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
import { getCurrentUser, hasPermission, type UserRole } from '@/lib/rbac'

const inviteRoles = ['Developer', 'Operator', 'Member'] satisfies UserRole[]

export function UsersPage() {
  const queryClient = useQueryClient()
  const user = getCurrentUser()
  const canManageUsers = hasPermission('ManageUsers', user)
  const [form, setForm] = useState({ email: '', role: 'Developer' as UserRole })
  const [inviteResult, setInviteResult] = useState<InviteUserResponse | null>(null)
  const [inviteRole, setInviteRole] = useState<UserRole>('Developer')
  const [formError, setFormError] = useState<string | null>(null)
  const [invitationActionError, setInvitationActionError] = useState<string | null>(null)

  const inviteUser = useMutation({
    mutationFn: () => invitationsApi.invite(form),
    onSuccess: result => {
      queryClient.invalidateQueries({ queryKey: ['pending-invitations'] })
      setInviteResult(result)
      setInviteRole(form.role)
      setForm({ email: '', role: 'Developer' })
      setFormError(null)
    },
    onError: (err: Error) => setFormError(err.message),
  })

  const resendInvitation = useMutation({
    mutationFn: (invitation: InvitationSummary) => invitationsApi.resend(invitation.id),
    onSuccess: result => {
      queryClient.invalidateQueries({ queryKey: ['pending-invitations'] })
      setInviteResult(result)
      setInviteRole(result.role)
      setInvitationActionError(null)
    },
    onError: (err: Error) => setInvitationActionError(err.message),
  })

  const revokeInvitation = useMutation({
    mutationFn: (id: string) => invitationsApi.revoke(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pending-invitations'] })
      setInvitationActionError(null)
    },
    onError: (err: Error) => setInvitationActionError(err.message),
  })

  const users = useQuery({
    queryKey: ['users'],
    queryFn: authApi.listUsers,
    enabled: canManageUsers,
  })

  const invitations = useQuery({
    queryKey: ['pending-invitations'],
    queryFn: invitationsApi.list,
    enabled: canManageUsers,
  })

  if (!canManageUsers) {
    return <AccessDenied title="Users unavailable" />
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setFormError(null)
    setInviteResult(null)
    inviteUser.mutate()
  }

  const acceptUrl = inviteResult
    ? `${window.location.origin}/invitations/accept?token=${encodeURIComponent(inviteResult.token)}`
    : null

  return (
    <div className="space-y-6">
      <div className="max-w-4xl">
        <h2 className="text-xl font-semibold">Users</h2>
        <p className="mt-0.5 text-sm text-muted-foreground">
          Manage tenant access and pending invitations.
        </p>
      </div>

      <div className="max-w-4xl rounded-lg border">
        <form onSubmit={handleSubmit} className="grid gap-4 p-4 sm:grid-cols-[1fr_12rem_auto] sm:items-end">
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              placeholder="user@example.com"
              value={form.email}
              onChange={e => setForm(prev => ({ ...prev, email: e.target.value }))}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="role">Role</Label>
            <Select
              id="role"
              value={form.role}
              onChange={e => setForm(prev => ({ ...prev, role: e.target.value as UserRole }))}
            >
              {inviteRoles.map(role => (
                <option key={role} value={role}>{role}</option>
              ))}
            </Select>
          </div>

          <Button type="submit" disabled={inviteUser.isPending}>
            {inviteUser.isPending ? 'Inviting...' : 'Invite user'}
          </Button>
        </form>

        {formError && (
          <p className="border-t px-4 py-3 text-sm text-destructive">{formError}</p>
        )}
      </div>

      {inviteResult && acceptUrl && (
        <div className="max-w-4xl rounded-lg border p-4">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-sm font-medium">Invitation created</h3>
            <Badge variant="outline">{inviteRole}</Badge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            Send the accept link or token to {inviteResult.email}. It expires {new Date(inviteResult.expiresAt).toLocaleString()}.
          </p>
          <div className="mt-4 space-y-2">
            <Label htmlFor="accept-link">Accept link</Label>
            <code
              id="accept-link"
              className="block rounded-md bg-muted p-3 text-xs break-all select-all"
            >
              {acceptUrl}
            </code>
          </div>
          <div className="mt-4 space-y-2">
            <Label htmlFor="invite-token">Invitation token</Label>
            <code
              id="invite-token"
              className="block rounded-md bg-muted p-3 text-xs break-all select-all"
            >
              {inviteResult.token}
            </code>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() => navigator.clipboard.writeText(inviteResult.token)}
            >
              Copy token
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigator.clipboard.writeText(acceptUrl)}
            >
              Copy accept link
            </Button>
          </div>
        </div>
      )}

      <section className="space-y-3">
        <div>
          <h3 className="text-sm font-medium">Active users</h3>
          <p className="mt-0.5 text-sm text-muted-foreground">
            Users with accepted access to this tenant.
          </p>
        </div>
        {users.error && (
          <p className="text-sm text-destructive">
            {users.error instanceof Error ? users.error.message : 'Failed to load users.'}
          </p>
        )}
        {users.isLoading ? (
          <TableSkeleton rows={4} />
        ) : (
          <UsersTable users={users.data?.users ?? []} />
        )}
      </section>

      <section className="space-y-3">
        <div>
          <h3 className="text-sm font-medium">Pending invitations</h3>
          <p className="mt-0.5 text-sm text-muted-foreground">
            Invites that have not been accepted and have not expired.
          </p>
        </div>
        {invitations.error && (
          <p className="text-sm text-destructive">
            {invitations.error instanceof Error ? invitations.error.message : 'Failed to load invitations.'}
          </p>
        )}
        {invitationActionError && (
          <p className="text-sm text-destructive">{invitationActionError}</p>
        )}
        {invitations.isLoading ? (
          <TableSkeleton rows={3} />
        ) : (
          <InvitationsTable
            invitations={invitations.data?.invitations ?? []}
            onResend={invitation => resendInvitation.mutate(invitation)}
            onRevoke={id => revokeInvitation.mutate(id)}
            resendPendingId={resendInvitation.variables?.id}
            revokePendingId={revokeInvitation.variables}
          />
        )}
      </section>

      <div className="max-w-4xl rounded-lg border p-4">
        <h3 className="text-sm font-medium">Roles</h3>
        <div className="mt-3 grid gap-3 text-sm sm:grid-cols-3">
          <RoleSummary role="Developer" description="Deploys and operates integrations, secrets, packages, and agent tokens." />
          <RoleSummary role="Operator" description="Views operations and can trigger manual runs without secret or deploy access." />
          <RoleSummary role="Member" description="Read-only legacy role for integrations and execution history." />
        </div>
      </div>
    </div>
  )
}

function UsersTable({ users }: { users: UserSummary[] }) {
  if (users.length === 0) {
    return (
      <div className="rounded-lg border py-10 text-center text-sm text-muted-foreground">
        No active users.
      </div>
    )
  }

  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Email</TableHead>
            <TableHead>Role</TableHead>
            <TableHead>Created</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {users.map(account => (
            <TableRow key={account.id}>
              <TableCell className="font-medium">{account.email}</TableCell>
              <TableCell>
                <Badge variant="outline">{account.role}</Badge>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {formatDate(account.createdAt)}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function InvitationsTable({
  invitations,
  onResend,
  onRevoke,
  resendPendingId,
  revokePendingId,
}: {
  invitations: InvitationSummary[]
  onResend: (invitation: InvitationSummary) => void
  onRevoke: (id: string) => void
  resendPendingId?: string
  revokePendingId?: string
}) {
  if (invitations.length === 0) {
    return (
      <div className="rounded-lg border py-10 text-center text-sm text-muted-foreground">
        No pending invitations.
      </div>
    )
  }

  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Email</TableHead>
            <TableHead>Role</TableHead>
            <TableHead>Expires</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {invitations.map(invitation => (
            <TableRow key={invitation.id}>
              <TableCell className="font-medium">{invitation.email}</TableCell>
              <TableCell>
                <Badge variant="outline">{invitation.role}</Badge>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {formatDate(invitation.expiresAt)}
              </TableCell>
              <TableCell className="text-right">
                <div className="flex justify-end gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={resendPendingId === invitation.id || revokePendingId === invitation.id}
                    onClick={() => onResend(invitation)}
                  >
                    {resendPendingId === invitation.id ? 'Resending...' : 'Resend'}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="text-destructive hover:text-destructive"
                    disabled={resendPendingId === invitation.id || revokePendingId === invitation.id}
                    onClick={() => onRevoke(invitation.id)}
                  >
                    {revokePendingId === invitation.id ? 'Revoking...' : 'Revoke'}
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function TableSkeleton({ rows }: { rows: number }) {
  return (
    <div className="rounded-lg border p-4">
      <div className="space-y-3">
        {Array.from({ length: rows }).map((_, i) => (
          <Skeleton key={i} className="h-9 w-full" />
        ))}
      </div>
    </div>
  )
}

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}

function RoleSummary({ role, description }: { role: UserRole; description: string }) {
  return (
    <div className="rounded-md border p-3">
      <Badge variant="outline">{role}</Badge>
      <p className="mt-2 text-muted-foreground">{description}</p>
    </div>
  )
}
