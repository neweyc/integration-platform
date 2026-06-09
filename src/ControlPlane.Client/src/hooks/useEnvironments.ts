import { useQuery } from '@tanstack/react-query'
import { environmentsApi, type EnvironmentSummary } from '@/api/environments'

/**
 * The single source of truth for the tenant's environment list across the UI. Pages that need an
 * environment selector read from here instead of hardcoding names, so the registry and the dropdowns
 * never disagree.
 */
export function useEnvironments(enabled = true) {
  return useQuery({
    queryKey: ['environments'],
    queryFn: environmentsApi.list,
    enabled,
  })
}

/** The environment a new record should default to: the one flagged default, else the first. */
export function defaultEnvironmentName(environments: EnvironmentSummary[] | undefined): string {
  if (!environments || environments.length === 0) return ''
  return (environments.find(e => e.isDefault) ?? environments[0]).name
}
