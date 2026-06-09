import { Link, Outlet, useNavigate, useLocation } from 'react-router-dom'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
} from '@/components/ui/sidebar'
import { Separator } from '@/components/ui/separator'
import { Button } from '@/components/ui/button'
import { clearToken } from '@/api/client'
import { getCurrentUser, hasPermission, type Permission } from '@/lib/rbac'

const navItems = [
  { label: 'Integrations', path: '/integrations', permission: 'ViewIntegrations' },
  { label: 'Workflows', path: '/workflows', permission: 'ViewIntegrations' },
  { label: 'Packages', path: '/packages', permission: 'ViewIntegrations' },
  { label: 'Secrets', path: '/secrets', permission: 'ViewSecrets' },
  { label: 'Environments', path: '/environments', permission: 'ViewEnvironments' },
  { label: 'Agent tokens', path: '/agent-tokens', permission: 'ManageAgentTokens' },
  { label: 'Access tokens', path: '/access-tokens', permission: 'ViewIntegrations' },
  { label: 'Alerts', path: '/alerts', permission: 'ViewAlerts' },
  { label: 'Users', path: '/users', permission: 'ManageUsers' },
  { label: 'Audit log', path: '/audit-log', permission: 'ViewAuditLog' },
] satisfies {
  label: string
  path: string
  permission: Permission
}[]

export function AppShell() {
  const navigate = useNavigate()
  const location = useLocation()
  const user = getCurrentUser()
  const visibleNavItems = navItems.filter(item => hasPermission(item.permission, user))

  function handleSignOut() {
    clearToken()
    navigate('/login')
  }

  return (
    <SidebarProvider>
      <div className="flex min-h-screen w-full">
        <Sidebar>
          <SidebarHeader className="p-4">
            <span className="font-semibold text-sm">Serto</span>
          </SidebarHeader>

          <Separator />

          <SidebarContent>
            <SidebarGroup>
              <SidebarGroupContent>
                <SidebarMenu>
                  {visibleNavItems.map(item => (
                    <SidebarMenuItem key={item.path}>
                      <SidebarMenuButton
                        isActive={location.pathname.startsWith(item.path)}
                        render={<Link to={item.path} />}
                      >
                        {item.label}
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  ))}
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>
          </SidebarContent>

          <SidebarFooter className="p-4">
            {user && (
              <div className="mb-3 px-2">
                <p className="truncate text-xs text-muted-foreground">{user.email}</p>
                <p className="text-xs font-medium">{user.role}</p>
              </div>
            )}
            <Button variant="ghost" className="w-full justify-start" onClick={handleSignOut}>
              Sign out
            </Button>
          </SidebarFooter>
        </Sidebar>

        <div className="flex flex-col flex-1 min-w-0">
          <header className="flex items-center h-12 px-4 border-b shrink-0">
            <SidebarTrigger />
          </header>
          <main className="flex-1 p-6 overflow-auto">
            <Outlet />
          </main>
        </div>
      </div>
    </SidebarProvider>
  )
}
