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
}

export interface CreateIntegrationRequest {
  name: string
  slug: string
  description?: string
  environment: string
  triggerType: TriggerType
  cronExpression?: string
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

export const integrationsApi = {
  list: (environment?: string) => {
    const query = environment ? `?environment=${environment}` : ''
    return api.get<ListIntegrationsResponse>(`/integrations${query}`)
  },
  get: (id: string) => api.get<Integration>(`/integrations/${id}`),
  create: (data: CreateIntegrationRequest) => api.post<Integration>('/integrations', data),
  update: (id: string, data: UpdateIntegrationRequest) => api.put<Integration>(`/integrations/${id}`, data),
  delete: (id: string) => api.delete<void>(`/integrations/${id}`),
}
