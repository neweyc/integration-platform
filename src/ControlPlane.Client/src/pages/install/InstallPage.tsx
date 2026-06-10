import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRight, ArrowLeft, ServerCog, ShieldCheck, Workflow } from 'lucide-react'
import { buttonVariants } from '@/components/ui/button'
import { CodeBlock } from '@/components/ui/code-block'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

const composeFile = `services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: integration_platform
      POSTGRES_USER: \${POSTGRES_USER}
      POSTGRES_PASSWORD: \${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U \${POSTGRES_USER} -d integration_platform"]
      interval: 5s
      timeout: 5s
      retries: 10

  controlplane:
    image: craytech/serto.controlplane:latest
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__Default: Host=postgres;Database=integration_platform;Username=\${POSTGRES_USER};Password=\${POSTGRES_PASSWORD}
      Jwt__Secret: \${JWT_SECRET}
      Encryption__MasterKey: \${ENCRYPTION_MASTER_KEY}
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  postgres_data:`

const envFile = `cat > .env <<EOF
POSTGRES_USER=serto
POSTGRES_PASSWORD=$(openssl rand -base64 24)
JWT_SECRET=$(openssl rand -base64 32)
ENCRYPTION_MASTER_KEY=$(openssl rand -base64 32)
EOF`

const agentService = `  # add under "services:" in the same docker-compose.yml
  agent:
    image: craytech/serto.agent:latest
    environment:
      Agent__ControlPlaneUrl: http://controlplane:8080
      Agent__AgentToken: \${SERTO_AGENT_TOKEN}
      Agent__Environment: production
      Agent__IntegrationsPath: /app/packages
      Agent__PackagesPath: /app/packages
    volumes:
      - agent_packages:/app/packages
    depends_on:
      - controlplane

# and add this volume next to "postgres_data:"
#   agent_packages:`

export function InstallPage() {
  return (
    <main className="min-h-screen bg-background text-foreground">
      <header className="border-b">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-6 py-5">
          <Link to="/" className="flex items-center gap-2 text-sm font-semibold">
            <span className="flex size-8 items-center justify-center rounded-lg border bg-background">
              <Workflow className="size-4" />
            </span>
            <span>Serto</span>
          </Link>
          <nav className="flex items-center gap-2">
            <Link className={cn(buttonVariants({ variant: 'ghost' }))} to="/">
              <ArrowLeft className="size-4" />
              Home
            </Link>
            <Link className={cn(buttonVariants({ variant: 'default' }))} to="/app">
              Open app
              <ArrowRight className="size-4" />
            </Link>
          </nav>
        </div>
      </header>

      <div className="mx-auto max-w-3xl px-6 py-16">
        <Badge variant="outline" className="mb-5">
          Self-hosted setup
        </Badge>
        <h1 className="text-4xl font-semibold tracking-normal sm:text-5xl">Stand up Serto on your own infrastructure</h1>
        <p className="mt-5 text-lg leading-8 text-muted-foreground">
          This single-host setup gives you a working control plane in about five minutes. The only thing
          you need installed is <span className="text-foreground">Docker with Compose v2</span> — Serto and
          its database run as containers. Everything stays on your machine.
        </p>

        <div className="mt-12 space-y-12">
          <Step
            number={1}
            title="Create a folder and the compose file"
            body="Make a directory for the deployment and save this as docker-compose.yml. It runs PostgreSQL and the control plane together — nothing else to wire up."
          >
            <CodeBlock code="mkdir serto && cd serto" />
            <CodeBlock filename="docker-compose.yml" code={composeFile} />
          </Step>

          <Step
            number={2}
            title="Generate your secrets"
            body="Run this once to create a .env file with a database password and the two signing keys, each a fresh random value. Docker Compose reads .env automatically."
          >
            <CodeBlock filename="create .env" code={envFile} />
            <Callout icon={ShieldCheck} tone="warning" title="Set these once and back them up">
              <code className="text-foreground">ENCRYPTION_MASTER_KEY</code> encrypts every stored secret and{' '}
              <code className="text-foreground">JWT_SECRET</code> signs sign-in tokens. Changing them later
              makes existing secrets unreadable and signs everyone out. Keep the <code>.env</code> safe and
              never commit it.
            </Callout>
          </Step>

          <Step
            number={3}
            title="Start it"
            body="Pull the images and bring the stack up. The database schema is created automatically on first start — there are no migrations to run by hand."
          >
            <CodeBlock code={'docker compose up -d'} />
            <p className="text-sm text-muted-foreground">
              Check it&apos;s healthy with <code className="text-foreground">docker compose ps</code> — both
              services should report <span className="text-foreground">running</span> (the control plane
              waits for the database to pass its health check first).
            </p>
          </Step>

          <Step
            number={4}
            title="Create your admin account"
            body="Open the control plane in your browser. The first visit takes you to a one-time setup screen to create your organization and admin user."
          >
            <a
              href="http://localhost:8080"
              target="_blank"
              rel="noreferrer"
              className={cn(buttonVariants({ variant: 'outline' }))}
            >
              Open http://localhost:8080
              <ArrowRight className="size-4" />
            </a>
            <p className="text-sm text-muted-foreground">
              That&apos;s a working control plane. You can author integrations, store secrets, and manage
              environments right away on the free Community edition.
            </p>
          </Step>

          <Step
            number={5}
            title="Connect a runtime agent (optional)"
            body="The control plane schedules work; a runtime agent runs it close to your systems and only needs an outbound connection — your credentials never leave your network. In the app, open Agent tokens → New token (environment production), then add this service to the same compose file and put the token in .env as SERTO_AGENT_TOKEN."
          >
            <CodeBlock filename="docker-compose.yml (additions)" code={agentService} />
            <CodeBlock code={'docker compose up -d'} />
          </Step>
        </div>

        <div className="mt-16 grid gap-4 sm:grid-cols-2">
          <Callout icon={ServerCog} title="Going to production">
            This single-host stack is ideal for trials and small deployments. For production, point the
            control plane at a managed PostgreSQL, terminate TLS in front of it, and run agents on separate
            hosts inside the networks they need to reach.
          </Callout>
          <Callout icon={ShieldCheck} title="Free, then licensed">
            The Community edition is the full product, capped by estate size (10 integrations, 2
            environments). A commercial license lifts the caps — drop the key into{' '}
            <code className="text-foreground">License__Key</code> and the deployment upgrades in place.
          </Callout>
        </div>

        <div className="mt-16 rounded-lg border bg-muted/35 p-6">
          <h2 className="text-lg font-semibold">Next: author your first integration</h2>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            With the control plane up, scaffold an integration with the <code>serto</code> CLI, then deploy
            it — your code is the manifest, so the control plane provisions the schedule and triggers for
            you.
          </p>
          <CodeBlock className="mt-4" code={'serto init my-integrations\ncd my-integrations\nserto deploy --url http://localhost:8080'} />
        </div>
      </div>
    </main>
  )
}

function Step({
  number,
  title,
  body,
  children,
}: {
  number: number
  title: string
  body: string
  children: ReactNode
}) {
  return (
    <section className="grid gap-4 sm:grid-cols-[auto_1fr] sm:gap-6">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-full border bg-muted text-sm font-semibold">
        {number}
      </span>
      <div className="space-y-4">
        <div>
          <h2 className="text-xl font-semibold tracking-normal">{title}</h2>
          <p className="mt-2 text-muted-foreground">{body}</p>
        </div>
        {children}
      </div>
    </section>
  )
}

function Callout({
  icon: Icon,
  title,
  tone = 'default',
  children,
}: {
  icon: typeof ShieldCheck
  title: string
  tone?: 'default' | 'warning'
  children: ReactNode
}) {
  return (
    <div
      className={cn(
        'flex gap-3 rounded-lg border p-4',
        tone === 'warning' && 'border-amber-500/40 bg-amber-500/5',
      )}
    >
      <Icon className={cn('mt-0.5 size-5 shrink-0', tone === 'warning' ? 'text-amber-600' : 'text-muted-foreground')} />
      <div>
        <h3 className="text-sm font-semibold">{title}</h3>
        <p className="mt-1 text-sm leading-6 text-muted-foreground">{children}</p>
      </div>
    </div>
  )
}
