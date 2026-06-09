import { api, downloadFile } from './client'

export interface PackageMetadata {
  id: string
  name: string
  version: string
  fileName: string
  sizeBytes: number
  sha256Hash: string
  createdAt: string
}

export interface ListPackagesResponse {
  packages: PackageMetadata[]
}

export const packagesApi = {
  list: () => api.get<ListPackagesResponse>('/integration-packages'),
  delete: (id: string) => api.delete<void>(`/integration-packages/${id}`),
  download: (id: string, fallbackName: string) =>
    downloadFile(`/integration-packages/${id}/download`, fallbackName),
}
