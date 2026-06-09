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

// Result of activating a version for its whole package. Activated/skipped are integration names:
// skipped means the version no longer contains that integration's class, so it was left in place.
export interface ActivatePackageVersionResult {
  packageName: string
  version: string
  activated: string[]
  skipped: string[]
}

export const packagesApi = {
  list: () => api.get<ListPackagesResponse>('/integration-packages'),
  delete: (id: string) => api.delete<void>(`/integration-packages/${id}`),
  download: (id: string, fallbackName: string) =>
    downloadFile(`/integration-packages/${id}/download`, fallbackName),
  // Activates this version for its whole package — every integration in the package moves to it.
  activate: (id: string) =>
    api.put<ActivatePackageVersionResult>(`/integration-packages/${id}/activate`, {}),
}
