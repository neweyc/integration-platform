import { Navigate } from 'react-router-dom'
import { isAuthenticated } from '@/api/client'

interface ProtectedRouteProps {
  children: React.ReactNode
}

// Redirects to /login if no token is present in localStorage.
// This is a client-side guard only — the API enforces auth independently.
export function ProtectedRoute({ children }: ProtectedRouteProps) {
  if (!isAuthenticated()) {
    return <Navigate to="/login" replace />
  }

  return <>{children}</>
}
