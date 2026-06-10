import type { LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'

// A call-to-action shown in an empty state. Provide either `to` (navigates) or `onClick` (e.g. opens
// a create sheet on the same page).
export interface EmptyStateAction {
  label: string
  to?: string
  onClick?: () => void
}

interface EmptyStateProps {
  icon?: LucideIcon
  title: string
  description?: string
  primaryAction?: EmptyStateAction
  secondaryAction?: EmptyStateAction
  // Optional extra content rendered under the description — e.g. a CLI snippet for code-first flows.
  children?: React.ReactNode
  className?: string
}

// A consistent, guiding empty state: says what the thing is, why it matters, and what to do next.
// Used across list pages so a brand-new tenant is never left staring at a blank table.
export function EmptyState({
  icon: Icon,
  title,
  description,
  primaryAction,
  secondaryAction,
  children,
  className,
}: EmptyStateProps) {
  return (
    <div
      className={
        'flex flex-col items-center justify-center rounded-lg border border-dashed px-6 py-16 text-center ' +
        (className ?? '')
      }
    >
      {Icon && (
        <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-muted">
          <Icon className="h-6 w-6 text-muted-foreground" />
        </div>
      )}
      <p className="text-sm font-medium">{title}</p>
      {description && <p className="mt-1 max-w-md text-sm text-muted-foreground">{description}</p>}
      {children && <div className="mt-4 w-full max-w-md">{children}</div>}
      {(primaryAction || secondaryAction) && (
        <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
          {primaryAction && <ActionButton action={primaryAction} variant="default" />}
          {secondaryAction && <ActionButton action={secondaryAction} variant="outline" />}
        </div>
      )}
    </div>
  )
}

function ActionButton({
  action,
  variant,
}: {
  action: EmptyStateAction
  variant: 'default' | 'outline'
}) {
  // Button has no asChild, so wrap with Link for navigation actions.
  if (action.to) {
    return (
      <Link to={action.to}>
        <Button variant={variant}>{action.label}</Button>
      </Link>
    )
  }
  return (
    <Button variant={variant} onClick={action.onClick}>
      {action.label}
    </Button>
  )
}
