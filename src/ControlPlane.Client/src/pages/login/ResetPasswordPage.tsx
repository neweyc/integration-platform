import { useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { authApi } from '@/api/auth'

export function ResetPasswordPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const tokenFromUrl = useMemo(() => searchParams.get('token') ?? '', [searchParams])

  const [token, setToken] = useState(tokenFromUrl)
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await authApi.resetPassword(token.trim(), password)
      // Reset revokes existing sessions, so send the user to sign in with their new password.
      navigate('/login', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Password reset failed.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <h1 className="text-2xl font-semibold tracking-tight">Serto</h1>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Choose a new password</CardTitle>
            <CardDescription>Enter a new password for your account.</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              {!tokenFromUrl && (
                <div className="space-y-2">
                  <Label htmlFor="token">Reset token</Label>
                  <Input
                    id="token"
                    value={token}
                    onChange={e => setToken(e.target.value)}
                    required
                  />
                </div>
              )}

              <div className="space-y-2">
                <Label htmlFor="password">New password</Label>
                <Input
                  id="password"
                  type="password"
                  placeholder="Min. 8 characters"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  minLength={8}
                  required
                />
              </div>

              {error && <p className="text-sm text-destructive">{error}</p>}

              <Button type="submit" className="w-full" disabled={loading}>
                {loading ? 'Updating...' : 'Update password'}
              </Button>

              <p className="text-center text-sm text-muted-foreground">
                <Link to="/login" className="underline">Back to sign in</Link>
              </p>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
