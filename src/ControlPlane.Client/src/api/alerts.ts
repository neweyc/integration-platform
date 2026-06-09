import { api } from './client'

export type AlertMode = 'Inherit' | 'Off' | 'Custom'

export interface TenantAlertSettings {
  emailEnabled: boolean
  emailRecipients: string | null
  smtpHost: string | null
  smtpPort: number
  smtpUseStartTls: boolean
  smtpUsername: string | null
  smtpPasswordSet: boolean
  smtpFromAddress: string | null
  smtpFromName: string | null
  webhookEnabled: boolean
  webhookUrl: string | null
  webhookSecretSet: boolean
  zeptoConfigured: boolean
  zeptoFromAddress: string | null
}

// Secret fields use: undefined = leave unchanged, '' = clear, value = set.
export interface UpdateTenantAlertSettingsRequest {
  emailEnabled: boolean
  emailRecipients: string | null
  smtpHost: string | null
  smtpPort: number | null
  smtpUseStartTls: boolean | null
  smtpUsername: string | null
  smtpPassword?: string
  smtpFromAddress: string | null
  smtpFromName: string | null
  webhookEnabled: boolean
  webhookUrl: string | null
  webhookSecret?: string
}

export interface IntegrationAlertSettings {
  integrationId: string
  mode: AlertMode
  emailEnabled: boolean
  emailRecipients: string | null
  webhookEnabled: boolean
  webhookUrl: string | null
  webhookSecretSet: boolean
}

export interface UpdateIntegrationAlertSettingsRequest {
  mode: AlertMode
  emailEnabled: boolean
  emailRecipients: string | null
  webhookEnabled: boolean
  webhookUrl: string | null
  webhookSecret?: string
}

export interface AlertSendOutcome {
  emailAttempted: boolean
  emailSucceeded: boolean
  emailError: string | null
  webhookAttempted: boolean
  webhookSucceeded: boolean
  webhookError: string | null
}

export const alertsApi = {
  getTenantSettings: () =>
    api.get<TenantAlertSettings>('/alerts/settings'),
  updateTenantSettings: (request: UpdateTenantAlertSettingsRequest) =>
    api.put<TenantAlertSettings>('/alerts/settings', request),
  sendTenantTest: () =>
    api.post<AlertSendOutcome>('/alerts/settings/test', {}),

  getIntegrationSettings: (integrationId: string) =>
    api.get<IntegrationAlertSettings>(`/alerts/integrations/${integrationId}/settings`),
  updateIntegrationSettings: (integrationId: string, request: UpdateIntegrationAlertSettingsRequest) =>
    api.put<IntegrationAlertSettings>(`/alerts/integrations/${integrationId}/settings`, request),
  sendIntegrationTest: (integrationId: string) =>
    api.post<AlertSendOutcome>(`/alerts/integrations/${integrationId}/settings/test`, {}),
}
