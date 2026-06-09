import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  alertsApi,
  type TenantAlertSettings,
  type UpdateTenantAlertSettingsRequest,
  type AlertSendOutcome,
} from '@/api/alerts'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { AccessDenied } from '@/components/layout/AccessDenied'
import { hasPermission } from '@/lib/rbac'

// Local form shape: the persisted settings minus the masked "secret is set" flags, plus write-only
// secret inputs that are only sent when the operator types a new value.
interface FormState {
  emailEnabled: boolean
  emailRecipients: string
  smtpHost: string
  smtpPort: number
  smtpUseStartTls: boolean
  smtpUsername: string
  smtpPassword: string
  clearSmtpPassword: boolean
  smtpFromAddress: string
  smtpFromName: string
  webhookEnabled: boolean
  webhookUrl: string
  webhookSecret: string
  clearWebhookSecret: boolean
}

function toForm(settings: TenantAlertSettings): FormState {
  return {
    emailEnabled: settings.emailEnabled,
    emailRecipients: settings.emailRecipients ?? '',
    smtpHost: settings.smtpHost ?? '',
    smtpPort: settings.smtpPort,
    smtpUseStartTls: settings.smtpUseStartTls,
    smtpUsername: settings.smtpUsername ?? '',
    smtpPassword: '',
    clearSmtpPassword: false,
    smtpFromAddress: settings.smtpFromAddress ?? '',
    smtpFromName: settings.smtpFromName ?? '',
    webhookEnabled: settings.webhookEnabled,
    webhookUrl: settings.webhookUrl ?? '',
    webhookSecret: '',
    clearWebhookSecret: false,
  }
}

// Secret fields: clear flag wins ('' clears the stored value), then a typed value sets it, otherwise
// omit the field so the stored value is left unchanged.
function secretField(value: string, clear: boolean): string | undefined {
  if (clear) return ''
  return value ? value : undefined
}

function toRequest(form: FormState): UpdateTenantAlertSettingsRequest {
  return {
    emailEnabled: form.emailEnabled,
    emailRecipients: form.emailRecipients.trim() || null,
    smtpHost: form.smtpHost.trim() || null,
    smtpPort: form.smtpPort,
    smtpUseStartTls: form.smtpUseStartTls,
    smtpUsername: form.smtpUsername.trim() || null,
    smtpPassword: secretField(form.smtpPassword, form.clearSmtpPassword),
    smtpFromAddress: form.smtpFromAddress.trim() || null,
    smtpFromName: form.smtpFromName.trim() || null,
    webhookEnabled: form.webhookEnabled,
    webhookUrl: form.webhookUrl.trim() || null,
    webhookSecret: secretField(form.webhookSecret, form.clearWebhookSecret),
  }
}

export function AlertsPage() {
  const canView = hasPermission('ViewAlerts')

  const { data, isLoading, error } = useQuery({
    queryKey: ['alert-settings'],
    queryFn: alertsApi.getTenantSettings,
    enabled: canView,
  })

  if (!canView) return <AccessDenied />

  if (isLoading) {
    return (
      <div className="space-y-4 p-6">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (error) {
    return <div className="p-6 text-destructive">Failed to load alert settings: {(error as Error).message}</div>
  }

  if (!data) return null

  return <AlertSettingsForm initial={data} />
}

// Form state is seeded once from the loaded settings, so this is only rendered after they exist. On
// save we refresh both the cache and local state directly — no effect syncing query data into state.
function AlertSettingsForm({ initial }: { initial: TenantAlertSettings }) {
  const queryClient = useQueryClient()
  const canManage = hasPermission('ManageAlerts')

  const [form, setForm] = useState<FormState>(() => toForm(initial))
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [testResult, setTestResult] = useState<AlertSendOutcome | null>(null)

  const save = useMutation({
    mutationFn: () => alertsApi.updateTenantSettings(toRequest(form)),
    onSuccess: (updated) => {
      queryClient.setQueryData(['alert-settings'], updated)
      setForm(toForm(updated))
      setError(null)
      setSaved(true)
      setTestResult(null)
    },
    onError: (err: Error) => {
      setError(err.message)
      setSaved(false)
    },
  })

  const sendTest = useMutation({
    mutationFn: alertsApi.sendTenantTest,
    onSuccess: (outcome) => {
      setTestResult(outcome)
      setError(null)
    },
    onError: (err: Error) => {
      setError(err.message)
      setTestResult(null)
    },
  })

  const update = (patch: Partial<FormState>) => {
    setForm({ ...form, ...patch })
    setSaved(false)
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    save.mutate()
  }

  const usingZeptoForEmail = form.emailEnabled && !form.smtpHost.trim()

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Failure alerts</h1>
        <p className="text-muted-foreground">
          Get notified when an integration fails and no retry remains. These are the tenant defaults;
          any integration can override them on its history page.
        </p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-6">
        {/* Email channel */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-3">
              <input
                type="checkbox"
                className="size-4"
                checked={form.emailEnabled}
                disabled={!canManage}
                onChange={(e) => update({ emailEnabled: e.target.checked })}
              />
              Email
            </CardTitle>
            <CardDescription>
              {initial.zeptoConfigured
                ? `By default, alerts are sent via the platform mailer${initial.zeptoFromAddress ? ` (${initial.zeptoFromAddress})` : ''}. Configure an SMTP server below to send from your own domain.`
                : 'The platform mailer is not configured, so email requires your own SMTP server below.'}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="recipients">Recipients</Label>
              <Input
                id="recipients"
                placeholder="ops@acme.com, oncall@acme.com"
                value={form.emailRecipients}
                disabled={!canManage || !form.emailEnabled}
                onChange={(e) => update({ emailRecipients: e.target.value })}
              />
              <p className="text-xs text-muted-foreground">Comma-separated email addresses.</p>
            </div>

            <div className="rounded-md border p-4 space-y-4">
              <div className="text-sm font-medium">
                SMTP server <span className="font-normal text-muted-foreground">(optional — overrides the platform mailer)</span>
              </div>
              {usingZeptoForEmail && (
                <p className="text-xs text-muted-foreground">
                  Leave blank to use the platform mailer.
                </p>
              )}
              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="smtpHost">Host</Label>
                  <Input
                    id="smtpHost"
                    placeholder="smtp.acme.com"
                    value={form.smtpHost}
                    disabled={!canManage}
                    onChange={(e) => update({ smtpHost: e.target.value })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="smtpPort">Port</Label>
                  <Input
                    id="smtpPort"
                    type="number"
                    value={form.smtpPort}
                    disabled={!canManage}
                    onChange={(e) => update({ smtpPort: Number(e.target.value) })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="smtpFromAddress">From address</Label>
                  <Input
                    id="smtpFromAddress"
                    placeholder="alerts@acme.com"
                    value={form.smtpFromAddress}
                    disabled={!canManage}
                    onChange={(e) => update({ smtpFromAddress: e.target.value })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="smtpFromName">From name</Label>
                  <Input
                    id="smtpFromName"
                    placeholder="Acme Alerts"
                    value={form.smtpFromName}
                    disabled={!canManage}
                    onChange={(e) => update({ smtpFromName: e.target.value })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="smtpUsername">Username</Label>
                  <Input
                    id="smtpUsername"
                    value={form.smtpUsername}
                    disabled={!canManage}
                    onChange={(e) => update({ smtpUsername: e.target.value })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="smtpPassword">Password</Label>
                  <Input
                    id="smtpPassword"
                    type="password"
                    placeholder={initial.smtpPasswordSet ? '•••••••• (unchanged)' : ''}
                    value={form.smtpPassword}
                    disabled={!canManage || form.clearSmtpPassword}
                    onChange={(e) => update({ smtpPassword: e.target.value })}
                  />
                  {initial.smtpPasswordSet && (
                    <label className="flex items-center gap-2 text-xs text-muted-foreground">
                      <input
                        type="checkbox"
                        className="size-3.5"
                        checked={form.clearSmtpPassword}
                        disabled={!canManage}
                        onChange={(e) => update({ clearSmtpPassword: e.target.checked, smtpPassword: '' })}
                      />
                      Clear saved password
                    </label>
                  )}
                </div>
              </div>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="size-4"
                  checked={form.smtpUseStartTls}
                  disabled={!canManage}
                  onChange={(e) => update({ smtpUseStartTls: e.target.checked })}
                />
                Use STARTTLS (port 587). Uncheck for implicit TLS (port 465). Plaintext SMTP (no TLS) is not supported.
              </label>
            </div>
          </CardContent>
        </Card>

        {/* Webhook channel */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-3">
              <input
                type="checkbox"
                className="size-4"
                checked={form.webhookEnabled}
                disabled={!canManage}
                onChange={(e) => update({ webhookEnabled: e.target.checked })}
              />
              Webhook
            </CardTitle>
            <CardDescription>
              POST a JSON payload to a URL — works with Slack, Teams, Discord, or PagerDuty incoming webhooks.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="webhookUrl">URL</Label>
              <Input
                id="webhookUrl"
                placeholder="https://hooks.slack.com/services/…"
                value={form.webhookUrl}
                disabled={!canManage || !form.webhookEnabled}
                onChange={(e) => update({ webhookUrl: e.target.value })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="webhookSecret">Signing secret (optional)</Label>
              <Input
                id="webhookSecret"
                type="password"
                placeholder={initial.webhookSecretSet ? '•••••••• (unchanged)' : ''}
                value={form.webhookSecret}
                disabled={!canManage || !form.webhookEnabled || form.clearWebhookSecret}
                onChange={(e) => update({ webhookSecret: e.target.value })}
              />
              {initial.webhookSecretSet && (
                <label className="flex items-center gap-2 text-xs text-muted-foreground">
                  <input
                    type="checkbox"
                    className="size-3.5"
                    checked={form.clearWebhookSecret}
                    disabled={!canManage}
                    onChange={(e) => update({ clearWebhookSecret: e.target.checked, webhookSecret: '' })}
                  />
                  Clear saved secret
                </label>
              )}
              <p className="text-xs text-muted-foreground">
                If set, the payload is signed with HMAC-SHA256 in the <code>X-Serto-Signature</code> header.
              </p>
            </div>
          </CardContent>
        </Card>

        {error && <div className="text-sm text-destructive">{error}</div>}
        {saved && <div className="text-sm text-green-600">Saved.</div>}
        {testResult && <TestOutcome outcome={testResult} />}

        {canManage && (
          <div className="flex gap-3">
            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={sendTest.isPending}
              onClick={() => sendTest.mutate()}
            >
              {sendTest.isPending ? 'Sending…' : 'Send test alert'}
            </Button>
          </div>
        )}
      </form>
    </div>
  )
}

function TestOutcome({ outcome }: { outcome: AlertSendOutcome }) {
  const lines: string[] = []
  if (outcome.emailAttempted)
    lines.push(outcome.emailSucceeded ? '✓ Email sent' : `✗ Email failed: ${outcome.emailError}`)
  if (outcome.webhookAttempted)
    lines.push(outcome.webhookSucceeded ? '✓ Webhook sent' : `✗ Webhook failed: ${outcome.webhookError}`)

  return (
    <div className="space-y-1 text-sm">
      {lines.map((line) => (
        <div key={line} className={line.startsWith('✓') ? 'text-green-600' : 'text-destructive'}>
          {line}
        </div>
      ))}
    </div>
  )
}
