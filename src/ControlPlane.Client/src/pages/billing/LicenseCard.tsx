import { useQuery } from '@tanstack/react-query'
import { licenseApi, type LicenseInfo } from '@/api/license'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'

// Shows the deployment's commercial edition, license state, expiry, and entitled caps. Rendered on the
// Billing page for self-hosted deployments (where Stripe billing is inert and the license is the upgrade
// path). Backed by GET /api/license. See docs/licensing.md.
export function LicenseCard() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['license'],
    queryFn: licenseApi.current,
  })

  if (isLoading || !data) {
    return error ? (
      <p className="text-sm text-destructive">
        {error instanceof Error ? error.message : 'Failed to load license.'}
      </p>
    ) : (
      <Skeleton className="h-40 w-full" />
    )
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          Edition
          <Badge>{data.edition}</Badge>
          {stateBadge(data)}
        </CardTitle>
        <CardDescription>{capsLine(data)}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-1 text-sm text-muted-foreground">
        {data.licensee && (
          <p>
            Licensed to <span className="text-foreground">{data.licensee}</span>.
          </p>
        )}
        <p>{statusLine(data)}</p>
      </CardContent>
    </Card>
  )
}

// A coloured badge for any state that needs attention; valid/unlicensed need none.
function stateBadge(data: LicenseInfo) {
  switch (data.state) {
    case 'Grace':
      return <Badge variant="outline">In grace period</Badge>
    case 'Expired':
      return <Badge variant="destructive">License expired</Badge>
    case 'Invalid':
      return <Badge variant="destructive">Invalid license</Badge>
    default:
      return null
  }
}

function capsLine(data: LicenseInfo) {
  const integrations = data.maxIntegrations === null ? 'Unlimited' : data.maxIntegrations.toLocaleString()
  const environments = data.maxEnvironments === null ? 'Unlimited' : data.maxEnvironments.toLocaleString()
  return `${integrations} integrations · ${environments} environments`
}

function statusLine(data: LicenseInfo): string {
  switch (data.state) {
    case 'Valid':
      return data.expiry
        ? `Active commercial license, expires ${formatDate(data.expiry)}.`
        : 'Active commercial license.'
    case 'Grace':
      return (
        `License expired ${formatDate(data.expiry)} and is running on a grace period` +
        `${data.graceUntil ? ` until ${formatDate(data.graceUntil)}` : ''}. ` +
        'Renew to avoid dropping to Community caps.'
      )
    case 'Expired':
      return `License expired ${formatDate(data.expiry)}; the deployment has degraded to Community caps. Contact sales to renew.`
    case 'Invalid':
      return 'The configured license token failed verification. The deployment is running on Community caps — check that the token is intact.'
    default:
      return 'Running the free Community edition. A commercial license lifts these caps — contact sales to upgrade.'
  }
}

function formatDate(value: string | null): string {
  return value ? new Date(value).toLocaleDateString() : 'an unknown date'
}
