import { api } from './client'

export interface OnboardingStep {
  key: string
  title: string
  description: string
  complete: boolean
  actionPath: string
}

export interface OnboardingStatus {
  steps: OnboardingStep[]
  complete: boolean
}

export const onboardingApi = {
  status: () => api.get<OnboardingStatus>('/onboarding/status'),
}
