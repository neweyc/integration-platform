import { useQuery, useMutation } from '@tanstack/react-query'
import { billingApi } from '@/api/billing'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'

// Self-serve plans an admin can switch to from the UI. Enterprise is sales-assisted; Free is the
// default with no charge, so neither appears as a checkout button.
const upgradePlans = [
  { key: 'Team', label: 'Team', hint: '10,000 executions / month' },
  { key: 'Business', label: 'Business', hint: '100,000 executions / month' },
]

export function BillingPage() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['billing-status'],
    queryFn: billingApi.current,
  })

  const checkout = useMutation({
    mutationFn: (plan: string) => billingApi.checkout(plan),
    onSuccess: result => {
      window.location.href = result.url
    },
  })

  const portal = useMutation({
    mutationFn: () => billingApi.portal(),
    onSuccess: result => {
      window.location.href = result.url
    },
  })

  const actionError = checkout.error ?? portal.error

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold">Billing</h2>
        <p className="text-sm text-muted-foreground mt-0.5">
          Manage your plan, usage, and payment details.
        </p>
      </div>

      {error && (
        <p className="text-sm text-destructive">
          {error instanceof Error ? error.message : 'Failed to load billing status.'}
        </p>
      )}

      {isLoading || !data ? (
        <Skeleton className="h-40 w-full" />
      ) : (
        <>
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle className="flex items-center gap-2">
                  Current plan
                  <Badge>{data.plan}</Badge>
                  {data.subscriptionStatus && (
                    <Badge variant="outline">{data.subscriptionStatus}</Badge>
                  )}
                </CardTitle>
                {data.hasBillingAccount && (
                  <Button
                    variant="outline"
                    onClick={() => portal.mutate()}
                    disabled={portal.isPending}
                  >
                    {portal.isPending ? 'Opening...' : 'Manage billing'}
                  </Button>
                )}
              </div>
              <CardDescription>
                {data.executionsUsed.toLocaleString()} of {data.executionLimit.toLocaleString()} monthly
                executions used.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
                <div
                  className="h-full bg-primary"
                  style={{
                    width: `${Math.min(100, Math.round((data.executionsUsed / Math.max(1, data.executionLimit)) * 100))}%`,
                  }}
                />
              </div>
            </CardContent>
          </Card>

          {!data.billingEnabled ? (
            <p className="text-sm text-muted-foreground">
              Self-serve billing isn't configured on this deployment. Contact your operator to change
              plans.
            </p>
          ) : (
            <Card>
              <CardHeader>
                <CardTitle>Change plan</CardTitle>
                <CardDescription>Upgrade to a higher monthly execution limit.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {upgradePlans.map(plan => (
                  <div key={plan.key} className="flex items-center justify-between rounded-md border p-3">
                    <div>
                      <p className="text-sm font-medium">{plan.label}</p>
                      <p className="text-sm text-muted-foreground">{plan.hint}</p>
                    </div>
                    <Button
                      variant={data.plan === plan.key ? 'outline' : 'default'}
                      disabled={data.plan === plan.key || checkout.isPending}
                      onClick={() => checkout.mutate(plan.key)}
                    >
                      {data.plan === plan.key ? 'Current' : 'Choose'}
                    </Button>
                  </div>
                ))}
              </CardContent>
            </Card>
          )}

          {actionError && (
            <p className="text-sm text-destructive">
              {actionError instanceof Error ? actionError.message : 'Billing action failed.'}
            </p>
          )}
        </>
      )}
    </div>
  )
}
