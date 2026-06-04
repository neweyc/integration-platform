import { api } from './client'

export interface AuditLogEntry {
  id: string
  actorUserId: string | null
  actorEmail: string
  action: string
  targetType: string
  targetId: string | null
  summary: string | null
  occurredAt: string
}

export interface ListAuditLogResponse {
  entries: AuditLogEntry[]
}

export const auditLogApi = {
  list: (limit = 100) => api.get<ListAuditLogResponse>(`/audit-log?limit=${limit}`),
}
