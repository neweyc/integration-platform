import { api } from './client'

export type TriggerType = 'Scheduled' | 'Webhook' | 'Manual'
export type IntegrationStatus = 'Enabled' | 'Disabled'

export interface Integration {
  id: string
  name: string
  slug: string
  description?: string
  environment: string
  status: IntegrationStatus
  triggerType: TriggerType
  cronExpression?: string
  className: string
  lastExecution?: ExecutionSummary | null
}

export type ExecutionStatus = 'Running' | 'Succeeded' | 'Failed'

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
  triggerType: TriggerType
  cronExpression?: string
  className: string
}

export interface UpdateIntegrationRequest {
  name: string
  description?: string
  status: IntegrationStatus
  cronExpression?: string
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
}
