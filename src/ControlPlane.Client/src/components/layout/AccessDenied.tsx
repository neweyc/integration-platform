import { Badge } from '@/components/ui/badge'
import { getCurrentUser } from '@/lib/rbac'

export function AccessDenied({ title = 'Access denied' }: { title?: string }) {
  const user = getCurrentUser()

  return (
    <div className="flex min-h-80 items-center justify-center rounded-lg border">
      <div className="max-w-md text-center">
        <Badge variant="outline">{user?.role ?? 'Unauthenticated'}</Badge>
        <h2 className="mt-3 text-xl font-semibold">{title}</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Your role does not allow this action.
        </p>
      </div>
    </div>
  )
}
