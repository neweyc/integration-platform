import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { authApi } from '@/api/auth'
import { saveToken } from '@/api/client'

export function SetupPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const [form, setForm] = useState({
    tenantName: '',
    tenantSlug: '',
    adminEmail: '',
    adminPassword: '',
  })

  function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const { name, value } = e.target

    // Auto-generate slug from tenant name: lowercase, spaces become hyphens
    if (name === 'tenantName') {
      setForm(prev => ({
        ...prev,
        tenantName: value,
        tenantSlug: value.toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, ''),
      }))
    } else {
      setForm(prev => ({ ...prev, [name]: value }))
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)

    try {
      const result = await authApi.setup(form)
      saveToken(result.token)
      queryClient.setQueryData(['setup-status'], { isComplete: true })
      navigate('/integrations')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Setup failed.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <h1 className="text-2xl font-semibold tracking-tight">Serto</h1>
          <p className="text-muted-foreground mt-1">Set up your workspace</p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Welcome</CardTitle>
            <CardDescription>
              Create your tenant and admin account to get started.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="tenantName">Organisation name</Label>
                <Input
                  id="tenantName"
                  name="tenantName"
                  placeholder="Acme Corp"
                  value={form.tenantName}
                  onChange={handleChange}
                  required
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="tenantSlug">Slug</Label>
                <Input
                  id="tenantSlug"
                  name="tenantSlug"
                  placeholder="acme-corp"
                  value={form.tenantSlug}
                  onChange={handleChange}
                  required
                />
                <p className="text-xs text-muted-foreground">
                  Lowercase letters, numbers, and hyphens only.
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="adminEmail">Admin email</Label>
                <Input
                  id="adminEmail"
                  name="adminEmail"
                  type="email"
                  placeholder="admin@acme.com"
                  value={form.adminEmail}
                  onChange={handleChange}
                  required
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="adminPassword">Password</Label>
                <Input
                  id="adminPassword"
                  name="adminPassword"
                  type="password"
                  placeholder="Min. 8 characters"
                  value={form.adminPassword}
                  onChange={handleChange}
                  required
                />
              </div>

              {error && (
                <p className="text-sm text-destructive">{error}</p>
              )}

              <Button type="submit" className="w-full" disabled={loading}>
                {loading ? 'Setting up...' : 'Create workspace'}
              </Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
