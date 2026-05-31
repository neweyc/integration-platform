import { api } from './client'

export interface SecretSummary {
  id: string
  key: string
  updatedAt: string
}

export interface ListSecretsResponse {
  secrets: SecretSummary[]
}

export const secretsApi = {
  list: (environment: string) =>
    api.get<ListSecretsResponse>(`/secrets/${environment}`),
  set: (environment: string, key: string, value: string) =>
    api.put<SecretSummary>(`/secrets/${environment}/${key}`, { value }),
  delete: (environment: string, key: string) =>
    api.delete<void>(`/secrets/${environment}/${key}`),
}
