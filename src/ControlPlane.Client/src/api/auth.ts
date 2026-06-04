import { api } from './client'
import type { UserRole } from '@/lib/rbac'

export interface SetupRequest {
  tenantName: string
  tenantSlug: string
  adminEmail: string
  adminPassword: string
}

export interface SetupResponse {
  tenantId: string
  tenantName: string
  userId: string
  email: string
  token: string
}

export interface LoginRequest {
  email: string
  password: string
}

export interface LoginResponse {
  token: string
  email: string
  role: string
}

export interface UserSummary {
  id: string
  email: string
  role: UserRole
  createdAt: string
}

export interface ListUsersResponse {
  users: UserSummary[]
}

export const authApi = {
  setupStatus: () => api.get<{ isComplete: boolean }>('/setup/status'),
  setup: (data: SetupRequest) => api.post<SetupResponse>('/setup', data),
  login: (data: LoginRequest) => api.post<LoginResponse>('/auth/login', data),
  listUsers: () => api.get<ListUsersResponse>('/auth/users'),
}
