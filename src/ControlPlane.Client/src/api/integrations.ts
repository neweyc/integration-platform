import { api } from './client'

export type TriggerType = 'Scheduled' | 'Webhook' | 'Manual' | 'Queue' | 'File'
export type TriggerSource = 'Scheduled' | 'Manual' | 'Webhook' | 'Retry' | 'Workflow' | 'Queue' | 'File'
export type IntegrationStatus = 'Enabled' | 'Disabled'

export interface IntegrationTrigger {
  id?: string
  name: string
  slug: string
  type: TriggerType
  enabled: boolean
  cronExpression?: string | null
  webhookUrl?: string | null
  webhookSecret?: string | null
}

export interface Integration {
  id: string
  name: string
  slug: string
  description?: string
  environment: string
  status: IntegrationStatus
  className: string
  triggers: IntegrationTrigger[]
  timeoutSeconds?: number | null
  lastExecution?: ExecutionSummary | null
}

export type ExecutionStatus = 'Running' | 'Succeeded' | 'Failed' | 'TimedOut'

export interface ExecutionSummary {
  id: string
  status: ExecutionStatus
  environment: string
  startedAt: string
  completedAt?: string | null
  durationMs?: number | null
  errorMessage?: string | null
}

export interface ExecutionLogItem {
  id: string
  timestamp: string
  level: string
  message: string
  exception?: string | null
  propertiesJson?: string | null
}

export interface CreateIntegrationRequest {
  name: string
  slug: string
  description?: string
  environment: string
  className: string
  triggers: IntegrationTrigger[]
  timeoutSeconds?: number
}

export interface UpdateIntegrationRequest {
  name: string
  description?: string
  status: IntegrationStatus
  triggers: IntegrationTrigger[]
  timeoutSeconds?: number
}

export interface ListIntegrationsResponse {
  integrations: Integration[]
}

export interface ListIntegrationExecutionsResponse {
  executions: ExecutionSummary[]
}

export interface ListExecutionLogsResponse {
  logs: ExecutionLogItem[]
}

export interface ManualRunResult {
  requestId: string
  integrationId: string
  integrationName: string
  environment: string
  requestedAt: string
}

export interface TriggerAdapterDescriptor {
  key: string
  displayName: string
  source: TriggerSource
  triggerType?: TriggerType | null
  requiresStoredTrigger: boolean
  supportsPayload: boolean
  supportsDeduplication: boolean
  description: string
}

export interface ListTriggerAdaptersResponse {
  adapters: TriggerAdapterDescriptor[]
}

export type TriggerEventOutcome =
  | 'Received'
  | 'Accepted'
  | 'Deduplicated'
  | 'Rejected'
  | 'ConvertedToWork'
  | 'Failed'

export interface TriggerEvent {
  id: string
  integrationId: string
  integrationTriggerId?: string | null
  adapterKey: string
  source: TriggerSource
  eventKey?: string | null
  outcome: TriggerEventOutcome
  workItemId?: string | null
  metadataJson?: string | null
  errorMessage?: string | null
  receivedAt: string
}

export interface ListTriggerEventsResponse {
  events: TriggerEvent[]
}

export const integrationsApi = {
  list: (environment?: string) => {
    const query = environment ? `?environment=${environment}` : ''
    return api.get<ListIntegrationsResponse>(`/integrations${query}`)
  },
  get: (id: string) => api.get<Integration>(`/integrations/${id}`),
  executions: (id: string, limit = 25) =>
    api.get<ListIntegrationExecutionsResponse>(`/integrations/${id}/executions?limit=${limit}`),
  logs: (integrationId: string, executionId: string) =>
    api.get<ListExecutionLogsResponse>(`/integrations/${integrationId}/executions/${executionId}/logs`),
  create: (data: CreateIntegrationRequest) => api.post<Integration>('/integrations', data),
  update: (id: string, data: UpdateIntegrationRequest) => api.put<Integration>(`/integrations/${id}`, data),
  delete: (id: string) => api.delete<void>(`/integrations/${id}`),
  runManual: (id: string) => api.post<ManualRunResult>(`/integrations/${id}/run`, {}),
  triggerAdapters: () => api.get<ListTriggerAdaptersResponse>('/trigger-adapters'),
  triggerEvents: (params?: {
    integrationId?: string
    triggerId?: string
    adapterKey?: string
    outcome?: TriggerEventOutcome
    limit?: number
  }) => {
    const query = new URLSearchParams()
    if (params?.integrationId) query.set('integrationId', params.integrationId)
    if (params?.triggerId) query.set('triggerId', params.triggerId)
    if (params?.adapterKey) query.set('adapterKey', params.adapterKey)
    if (params?.outcome) query.set('outcome', params.outcome)
    if (params?.limit) query.set('limit', String(params.limit))
    const queryString = query.toString()
    const suffix = queryString ? `?${queryString}` : ''
    return api.get<ListTriggerEventsResponse>(`/trigger-events${suffix}`)
  },
}
