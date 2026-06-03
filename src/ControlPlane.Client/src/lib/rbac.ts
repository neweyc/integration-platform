import { getToken } from '@/api/client'

export type UserRole = 'Admin' | 'Developer' | 'Operator' | 'Member'

export type Permission =
  | 'ViewIntegrations'
  | 'ManageIntegrations'
  | 'TriggerManualRun'
  | 'ViewExecutions'
  | 'ViewSecrets'
  | 'ManageSecrets'
  | 'ManagePackages'
  | 'ManageAgentTokens'
  | 'ManageUsers'
  | 'ManageBilling'

export interface CurrentUser {
  userId: string
  email: string
  tenantId: string
  role: UserRole
}

const allPermissions: Permission[] = [
  'ViewIntegrations',
  'ManageIntegrations',
  'TriggerManualRun',
  'ViewExecutions',
  'ViewSecrets',
  'ManageSecrets',
  'ManagePackages',
  'ManageAgentTokens',
  'ManageUsers',
  'ManageBilling',
]

export const rolePermissions: Record<UserRole, ReadonlySet<Permission>> = {
  Admin: new Set(allPermissions),
  Developer: new Set([
    'ViewIntegrations',
    'ManageIntegrations',
    'TriggerManualRun',
    'ViewExecutions',
    'ViewSecrets',
    'ManageSecrets',
    'ManagePackages',
    'ManageAgentTokens',
  ]),
  Operator: new Set(['ViewIntegrations', 'ViewExecutions', 'TriggerManualRun']),
  Member: new Set(['ViewIntegrations', 'ViewExecutions']),
}

export function getCurrentUser(): CurrentUser | null {
  const token = getToken()
  if (!token) return null

  const payload = decodeJwtPayload(token)
  if (!payload) return null

  const rawRole = stringClaim(payload.role) ?? stringClaim(payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'])
  const role = isUserRole(rawRole) ? rawRole : 'Member'

  return {
    userId: stringClaim(payload.sub) ?? stringClaim(payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']) ?? '',
    email: stringClaim(payload.email) ?? stringClaim(payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress']) ?? '',
    tenantId: stringClaim(payload.tenant_id) ?? '',
    role,
  }
}

export function hasPermission(permission: Permission, user = getCurrentUser()): boolean {
  if (!user) return false
  return rolePermissions[user.role].has(permission)
}

export function hasAnyPermission(permissions: Permission[], user = getCurrentUser()): boolean {
  return permissions.some(permission => hasPermission(permission, user))
}

function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const [, payload] = token.split('.')
  if (!payload) return null

  try {
    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/')
    const padded = normalized.padEnd(normalized.length + ((4 - normalized.length % 4) % 4), '=')
    const json = atob(padded)
    return JSON.parse(json) as Record<string, unknown>
  } catch {
    return null
  }
}

function stringClaim(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null
}

function isUserRole(value: string | null): value is UserRole {
  return value === 'Admin' || value === 'Developer' || value === 'Operator' || value === 'Member'
}
