import { api } from './client'

export interface UserTokenSummary {
  id: string
  name: string
  createdAt: string
  lastUsedAt: string | null
}

export interface ListUserTokensResponse {
  tokens: UserTokenSummary[]
}

export interface CreateUserTokenResponse {
  id: string
  name: string
  plaintextToken: string
  createdAt: string
}

export const userTokensApi = {
  list: () => api.get<ListUserTokensResponse>('/user-tokens'),
  create: (name: string) => api.post<CreateUserTokenResponse>('/user-tokens', { name }),
  revoke: (id: string) => api.delete<void>(`/user-tokens/${id}`),
}
