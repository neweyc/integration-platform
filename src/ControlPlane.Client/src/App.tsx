import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { SetupPage } from '@/pages/setup/SetupPage'
import { LoginPage } from '@/pages/login/LoginPage'
import { IntegrationsPage } from '@/pages/integrations/IntegrationsPage'
import { SecretsPage } from '@/pages/secrets/SecretsPage'
import { AgentTokensPage } from '@/pages/agentTokens/AgentTokensPage'
import { AppShell } from '@/components/layout/AppShell'
import { ProtectedRoute } from '@/components/layout/ProtectedRoute'
import { authApi } from '@/api/auth'
import { isAuthenticated } from '@/api/client'

const queryClient = new QueryClient()

// Determines where to send the user based on setup state and auth state.
// Shows nothing while loading to avoid flash-of-wrong-redirect.
function RootRedirect() {
  const { data, isLoading } = useQuery({
    queryKey: ['setup-status'],
    queryFn: authApi.setupStatus,
  })

  if (isLoading) return null

  if (!data?.isComplete) return <Navigate to="/setup" replace />
  if (!isAuthenticated()) return <Navigate to="/login" replace />
  return <Navigate to="/integrations" replace />
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <BrowserRouter>
          <Routes>
            {/* Public routes */}
            <Route path="/setup" element={<SetupPage />} />
            <Route path="/login" element={<LoginPage />} />

            {/* Protected routes — wrapped in the app shell */}
            <Route
              element={
                <ProtectedRoute>
                  <AppShell />
                </ProtectedRoute>
              }
            >
              <Route path="/integrations" element={<IntegrationsPage />} />
              <Route path="/secrets" element={<SecretsPage />} />
              <Route path="/agent-tokens" element={<AgentTokensPage />} />
            </Route>

            {/* Root: check setup status, then route appropriately */}
            <Route path="/" element={<RootRedirect />} />
          </Routes>
        </BrowserRouter>
      </TooltipProvider>
    </QueryClientProvider>
  )
}
