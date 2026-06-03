import { api } from './client'
import type { UserRole } from '@/lib/rbac'

export interface InviteUserRequest {
  email: string
  role: UserRole
}

export interface InviteUserResponse {
  invitationId: string
  email: string
  token: string
  expiresAt: string
}

export interface AcceptInvitationRequest {
  token: string
  password: string
}

export interface AcceptInvitationResponse {
  userId: string
  email: string
  token: string
}

export const invitationsApi = {
  invite: (data: InviteUserRequest) =>
    api.post<InviteUserResponse>('/invitations', data),
  accept: (data: AcceptInvitationRequest) =>
    api.post<AcceptInvitationResponse>('/invitations/accept', data),
}
