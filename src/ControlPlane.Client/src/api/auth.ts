import { api } from './client'

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

export const authApi = {
  setup: (data: SetupRequest) => api.post<SetupResponse>('/setup', data),
  login: (data: LoginRequest) => api.post<LoginResponse>('/auth/login', data),
}
