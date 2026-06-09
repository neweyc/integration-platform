import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  alertsApi,
  type AlertMode,
  type IntegrationAlertSettings,
  type UpdateIntegrationAlertSettingsRequest,
  type AlertSendOutcome,
} from '@/api/alerts'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Select } from '@/components/ui/select'
import { hasPermission } from '@/lib/rbac'

interface FormState {
  mode: AlertMode
  emailEnabled: boolean
  emailRecipients: string
  webhookEnabled: boolean
  webhookUrl: string
  webhookSecret: string
  clearWebhookSecret: boolean
}

function toForm(settings: IntegrationAlertSettings): FormState {
  return {
    mode: settings.mode,
    emailEnabled: settings.emailEnabled,
    emailRecipients: settings.emailRecipients ?? '',
    webhookEnabled: settings.webhookEnabled,
    webhookUrl: settings.webhookUrl ?? '',
    webhookSecret: '',
    clearWebhookSecret: false,
  }
}

function toRequest(form: FormState): UpdateIntegrationAlertSettingsRequest {
  return {
    mode: form.mode,
    emailEnabled: form.emailEnabled,
    emailRecipients: form.emailRecipients.trim() || null,
    webhookEnabled: form.webhookEnabled,
    webhookUrl: form.webhookUrl.trim() || null,
    // Clear flag wins ('' clears), then a typed value sets, otherwise omit to leave unchanged.
    webhookSecret: form.clearWebhookSecret ? '' : form.webhookSecret ? form.webhookSecret : undefined,
  }
}

const MODE_DESCRIPTION: Record<AlertMode, string> = {
  Inherit: 'Use the tenant-default alert destinations.',
  Off: 'Suppress all failure alerts for this integration.',
  Custom: 'Send to destinations specific to this integration.',
}

export function IntegrationAlertSettingsCard({ integrationId }: { integrationId: string }) {
  const canView = hasPermission('ViewAlerts')

  const { data } = useQuery({
    queryKey: ['integration-alert-settings', integrationId],
    queryFn: () => alertsApi.getIntegrationSettings(integrationId),
    enabled: canView,
  })

  if (!canView || !data) return null

  return <SettingsForm integrationId={integrationId} initial={data} />
}

// Seeded once from loaded settings (only rendered after they exist); save refreshes cache and state
// directly, so there is no effect copying query data into state.
function SettingsForm({ integrationId, initial }: { integrationId: string; initial: IntegrationAlertSettings }) {
  const queryClient = useQueryClient()
  const canManage = hasPermission('ManageAlerts')

  const [form, setForm] = useState<FormState>(() => toForm(initial))
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [testResult, setTestResult] = useState<AlertSendOutcome | null>(null)

  const save = useMutation({
    mutationFn: () => alertsApi.updateIntegrationSettings(integrationId, toRequest(form!)),
    onSuccess: (updated) => {
      queryClient.setQueryData(['integration-alert-settings', integrationId], updated)
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
    mutationFn: () => alertsApi.sendIntegrationTest(integrationId),
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

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Failure alerts</CardTitle>
        <CardDescription>How failures of this integration are alerted.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <Label>Mode</Label>
          <Select
            className="w-56"
            value={form.mode}
            disabled={!canManage}
            onChange={(e) => update({ mode: e.target.value as AlertMode })}
          >
            <option value="Inherit">Inherit tenant defaults</option>
            <option value="Custom">Custom</option>
            <option value="Off">Off</option>
          </Select>
          <p className="text-xs text-muted-foreground">{MODE_DESCRIPTION[form.mode]}</p>
        </div>

        {form.mode === 'Custom' && (
          <div className="space-y-4 rounded-md border p-4">
            <label className="flex items-center gap-2 text-sm font-medium">
              <input
                type="checkbox"
                className="size-4"
                checked={form.emailEnabled}
                disabled={!canManage}
                onChange={(e) => update({ emailEnabled: e.target.checked })}
              />
              Email
            </label>
            <div className="space-y-2">
              <Label htmlFor="int-recipients">Recipients</Label>
              <Input
                id="int-recipients"
                placeholder="team@acme.com, oncall@acme.com"
                value={form.emailRecipients}
                disabled={!canManage || !form.emailEnabled}
                onChange={(e) => update({ emailRecipients: e.target.value })}
              />
              <p className="text-xs text-muted-foreground">
                Sent through the tenant's configured email sender.
              </p>
            </div>

            <label className="flex items-center gap-2 text-sm font-medium">
              <input
                type="checkbox"
                className="size-4"
                checked={form.webhookEnabled}
                disabled={!canManage}
                onChange={(e) => update({ webhookEnabled: e.target.checked })}
              />
              Webhook
            </label>
            <div className="space-y-2">
              <Label htmlFor="int-webhook-url">URL</Label>
              <Input
                id="int-webhook-url"
                placeholder="https://hooks.slack.com/services/…"
                value={form.webhookUrl}
                disabled={!canManage || !form.webhookEnabled}
                onChange={(e) => update({ webhookUrl: e.target.value })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="int-webhook-secret">Signing secret (optional)</Label>
              <Input
                id="int-webhook-secret"
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
            </div>
          </div>
        )}

        {error && <div className="text-sm text-destructive">{error}</div>}
        {saved && <div className="text-sm text-green-600">Saved.</div>}
        {testResult && <TestOutcome outcome={testResult} />}

        {canManage && (
          <div className="flex gap-3">
            <Button type="button" size="sm" disabled={save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={sendTest.isPending}
              onClick={() => sendTest.mutate()}
            >
              {sendTest.isPending ? 'Sending…' : 'Send test alert'}
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
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
