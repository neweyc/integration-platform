import { api } from './client'

export interface BillingStatus {
  plan: string
  subscriptionStatus: string | null
  executionsUsed: number
  executionLimit: number
  billingEnabled: boolean
  hasBillingAccount: boolean
}

export interface BillingUrl {
  url: string
}

export const billingApi = {
  current: () => api.get<BillingStatus>('/billing/current'),
  checkout: (plan: string) => api.post<BillingUrl>('/billing/checkout', { plan }),
  portal: () => api.post<BillingUrl>('/billing/portal', {}),
}
