import { api } from './client'

export interface AgentTokenSummary {
  id: string
  name: string
  environment: string
  createdAt: string
}

export interface CreateAgentTokenRequest {
  name: string
  environment: string
}

// Token value is only present on creation — never returned again
export interface CreateAgentTokenResponse {
  id: string
  name: string
  environment: string
  token: string
  createdAt: string
}

export interface ListAgentTokensResponse {
  tokens: AgentTokenSummary[]
}

export const agentTokensApi = {
  list: () => api.get<ListAgentTokensResponse>('/agent-tokens'),
  create: (data: CreateAgentTokenRequest) =>
    api.post<CreateAgentTokenResponse>('/agent-tokens', data),
  revoke: (id: string) => api.delete<void>(`/agent-tokens/${id}`),
}
