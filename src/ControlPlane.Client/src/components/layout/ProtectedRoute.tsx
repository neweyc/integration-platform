import { Navigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { isAuthenticated } from '@/api/client'
import { authApi } from '@/api/auth'

interface ProtectedRouteProps {
  children: React.ReactNode
}

// Guards protected routes. Redirects to /setup if setup is incomplete,
// or /login if the user is not authenticated.
// Shows nothing while loading to avoid flash-of-wrong-redirect.
export function ProtectedRoute({ children }: ProtectedRouteProps) {
  const { data, isLoading } = useQuery({
    queryKey: ['setup-status'],
    queryFn: authApi.setupStatus,
  })

  if (isLoading) return null

  if (!data?.isComplete) return <Navigate to="/setup" replace />
  if (!isAuthenticated()) return <Navigate to="/login" replace />

  return <>{children}</>
}
