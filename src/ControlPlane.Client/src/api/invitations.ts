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

export interface InvitationSummary {
  id: string
  email: string
  role: UserRole
  expiresAt: string
  acceptedAt: string | null
}

export interface ListInvitationsResponse {
  invitations: InvitationSummary[]
}

export interface ResendInvitationResponse {
  invitationId: string
  email: string
  role: UserRole
  token: string
  expiresAt: string
}

export const invitationsApi = {
  invite: (data: InviteUserRequest) =>
    api.post<InviteUserResponse>('/invitations', data),
  list: () => api.get<ListInvitationsResponse>('/invitations'),
  resend: (id: string) => api.post<ResendInvitationResponse>(`/invitations/${id}/resend`, {}),
  revoke: (id: string) => api.delete<void>(`/invitations/${id}`),
  accept: (data: AcceptInvitationRequest) =>
    api.post<AcceptInvitationResponse>('/invitations/accept', data),
}
