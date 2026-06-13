import { api } from './client'

export interface InfoRequestForm {
  name: string
  email: string
  company?: string
  message: string
}

export const infoRequestApi = {
  submit: (form: InfoRequestForm) => api.post<{ sent: boolean }>('/info-request', form),
}
