import { api } from './client'

export interface EnvironmentSummary {
  name: string
  displayName: string
  description: string | null
  sortOrder: number
  isDefault: boolean
}

export interface ListEnvironmentsResponse {
  environments: EnvironmentSummary[]
}

export interface UpsertEnvironmentRequest {
  name?: string
  displayName?: string | null
  description?: string | null
  sortOrder?: number
  isDefault: boolean
}

export const environmentsApi = {
  list: () => api.get<ListEnvironmentsResponse>('/environments'),
  create: (request: UpsertEnvironmentRequest) =>
    api.post<EnvironmentSummary>('/environments', request),
  update: (name: string, request: UpsertEnvironmentRequest) =>
    api.put<EnvironmentSummary>(`/environments/${name}`, request),
  delete: (name: string) => api.delete<void>(`/environments/${name}`),
}
