import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { invitationsApi, type InviteUserResponse } from '@/api/invitations'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { getCurrentUser, hasPermission, type UserRole } from '@/lib/rbac'

const inviteRoles = ['Developer', 'Operator', 'Member'] satisfies UserRole[]

export function UsersPage() {
  const user = getCurrentUser()
  const canManageUsers = hasPermission('ManageUsers', user)
  const [form, setForm] = useState({ email: '', role: 'Developer' as UserRole })
  const [inviteResult, setInviteResult] = useState<InviteUserResponse | null>(null)
  const [inviteRole, setInviteRole] = useState<UserRole>('Developer')
  const [formError, setFormError] = useState<string | null>(null)

  const inviteUser = useMutation({
    mutationFn: () => invitationsApi.invite(form),
    onSuccess: result => {
      setInviteResult(result)
      setInviteRole(form.role)
      setForm({ email: '', role: 'Developer' })
      setFormError(null)
    },
    onError: (err: Error) => setFormError(err.message),
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
    <div className="max-w-3xl space-y-6">
      <div>
        <h2 className="text-xl font-semibold">Users</h2>
        <p className="mt-0.5 text-sm text-muted-foreground">
          Invite tenant users and assign their starting role.
        </p>
      </div>

      <div className="rounded-lg border">
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
        <div className="rounded-lg border p-4">
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

      <div className="rounded-lg border p-4">
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

function RoleSummary({ role, description }: { role: UserRole; description: string }) {
  return (
    <div className="rounded-md border p-3">
      <Badge variant="outline">{role}</Badge>
      <p className="mt-2 text-muted-foreground">{description}</p>
    </div>
  )
}
