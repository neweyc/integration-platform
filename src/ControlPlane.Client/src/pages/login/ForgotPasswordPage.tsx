import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { authApi } from '@/api/auth'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setLoading(true)
    try {
      await authApi.forgotPassword(email)
    } catch {
      // Intentionally ignored: the endpoint never reveals whether the email exists, so the UI
      // shows the same confirmation either way.
    } finally {
      setLoading(false)
      setSubmitted(true)
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
            <CardTitle>Reset your password</CardTitle>
            <CardDescription>
              Enter your email and we'll send you a link to reset your password.
            </CardDescription>
          </CardHeader>
          <CardContent>
            {submitted ? (
              <div className="space-y-4">
                <p className="text-sm text-muted-foreground">
                  If an account exists for <span className="font-medium">{email}</span>, a password
                  reset link is on its way. Check your inbox.
                </p>
                <p className="text-center text-sm text-muted-foreground">
                  <Link to="/login" className="underline">Back to sign in</Link>
                </p>
              </div>
            ) : (
              <form onSubmit={handleSubmit} className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="email">Email</Label>
                  <Input
                    id="email"
                    name="email"
                    type="email"
                    placeholder="admin@acme.com"
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                    required
                  />
                </div>

                <Button type="submit" className="w-full" disabled={loading}>
                  {loading ? 'Sending...' : 'Send reset link'}
                </Button>

                <p className="text-center text-sm text-muted-foreground">
                  <Link to="/login" className="underline">Back to sign in</Link>
                </p>
              </form>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
