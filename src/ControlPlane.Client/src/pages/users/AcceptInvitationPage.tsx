import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { invitationsApi } from '@/api/invitations'
import { saveToken } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export function AcceptInvitationPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const initialToken = useMemo(() => searchParams.get('token') ?? '', [searchParams])
  const [form, setForm] = useState({ token: initialToken, password: '' })
  const [formError, setFormError] = useState<string | null>(null)

  const acceptInvitation = useMutation({
    mutationFn: () => invitationsApi.accept(form),
    onSuccess: result => {
      saveToken(result.token)
      navigate('/integrations')
    },
    onError: (err: Error) => setFormError(err.message),
  })

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setFormError(null)
    acceptInvitation.mutate()
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/30 p-4">
      <div className="w-full max-w-md rounded-lg border bg-background p-6 shadow-sm">
        <h1 className="text-xl font-semibold">Accept invitation</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Create your password to join this tenant.
        </p>

        <form onSubmit={handleSubmit} className="mt-6 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="token">Invitation token</Label>
            <Input
              id="token"
              value={form.token}
              onChange={e => setForm(prev => ({ ...prev, token: e.target.value.trim() }))}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              value={form.password}
              onChange={e => setForm(prev => ({ ...prev, password: e.target.value }))}
              minLength={8}
              required
            />
          </div>

          {formError && <p className="text-sm text-destructive">{formError}</p>}

          <Button type="submit" className="w-full" disabled={acceptInvitation.isPending}>
            {acceptInvitation.isPending ? 'Accepting...' : 'Accept invitation'}
          </Button>
        </form>
      </div>
    </div>
  )
}
