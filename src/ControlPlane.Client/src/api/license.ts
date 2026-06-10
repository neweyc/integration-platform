import { api } from './client'

export type LicenseState = 'Unlicensed' | 'Invalid' | 'Valid' | 'Grace' | 'Expired'

export interface LicenseInfo {
  // The edition that currently governs caps ("Community" or the paid plan name).
  edition: string
  state: LicenseState
  licensee: string | null
  // The plan named on the license token (may differ from edition once expired).
  licensedPlan: string
  expiry: string | null
  graceUntil: string | null
  // null = unlimited.
  maxIntegrations: number | null
  maxEnvironments: number | null
}

export const licenseApi = {
  current: () => api.get<LicenseInfo>('/license'),
}
