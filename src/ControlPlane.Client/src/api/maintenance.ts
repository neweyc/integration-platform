import { api } from './client'

export interface MaintenanceInfo {
  // When true, the control plane is in soft-launch mode: it rejects all writes and the UI hides its
  // "open app / sign in" entry points.
  enabled: boolean
}

export const maintenanceApi = {
  status: () => api.get<MaintenanceInfo>('/maintenance'),
}
