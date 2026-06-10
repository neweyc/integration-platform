import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { onboardingApi } from '@/api/onboarding'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

// A dismissable-by-completion checklist that nudges a new tenant through the first run. It hides
// itself once every step is done (or while the status hasn't loaded), so established tenants never
// see it.
export function GettingStartedChecklist() {
  const { data } = useQuery({
    queryKey: ['onboarding-status'],
    queryFn: onboardingApi.status,
  })

  if (!data || data.complete) return null

  const completed = data.steps.filter(step => step.complete).length

  return (
    <Card>
      <CardHeader>
        <CardTitle>Getting started</CardTitle>
        <CardDescription>
          {completed} of {data.steps.length} steps complete
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {data.steps.map(step => (
          <div key={step.key} className="flex items-start gap-3">
            <span
              className={
                'mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border text-xs ' +
                (step.complete
                  ? 'border-primary bg-primary text-primary-foreground'
                  : 'border-muted-foreground/40 text-muted-foreground')
              }
              aria-hidden
            >
              {step.complete ? '✓' : ''}
            </span>
            <div className="space-y-0.5">
              <Link
                to={step.actionPath}
                className={
                  'text-sm font-medium hover:underline ' +
                  (step.complete ? 'text-muted-foreground line-through' : '')
                }
              >
                {step.title}
              </Link>
              <p className="text-sm text-muted-foreground">{step.description}</p>
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
