import { Link } from 'react-router-dom'
import {
  ArrowRight,
  CheckCircle2,
  Code2,
  GitBranch,
  KeyRound,
  LockKeyhole,
  RadioTower,
  ServerCog,
  ShieldCheck,
  Terminal,
  Unlock,
  Workflow,
} from 'lucide-react'
import heroImage from '@/assets/hero.png'
import { buttonVariants } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { CodeBlock } from '@/components/ui/code-block'
import { cn } from '@/lib/utils'

// The three things that actually make Serto different — code-first, runs on your own infra, secrets stay put.
const capabilities = [
  {
    icon: Code2,
    title: 'Your code is the manifest',
    description:
      'Write integrations as C# classes and decorate them with a trigger. `serto deploy` provisions the schedule and triggers — no click-ops, no proprietary designer.',
  },
  {
    icon: RadioTower,
    title: 'Runs on your infrastructure',
    description:
      'Runtime agents execute close to your systems and only need an outbound connection. Self-host the whole platform — nothing has to live in someone else’s cloud.',
  },
  {
    icon: LockKeyhole,
    title: 'Secrets never leave your network',
    description:
      'The agent resolves secrets locally; with the on-prem vault backend the control plane stores only references. Credentials stay inside your perimeter.',
  },
]

// What you actually get — relays the scope honestly.
const scope = [
  {
    icon: ServerCog,
    title: 'Control plane',
    description:
      'Scheduling, encrypted secrets, package storage, runtime state, execution history with structured logs, plus audit and role-based access.',
  },
  {
    icon: RadioTower,
    title: 'Runtime agent',
    description:
      'A stateless worker that polls for due work, syncs packages (SHA-256 verified), runs integrations in your environments, and reports results.',
  },
  {
    icon: Terminal,
    title: 'CLI (serto)',
    description:
      'Scaffold, scan, test, and deploy integration projects from your terminal or CI. The SDK, connectors, and CLI are MIT-licensed.',
  },
]

const runtimeFlow = [
  'Register the integration class',
  'Deploy the compiled package',
  'Agent claims and runs the job',
  'Review results and logs',
]

const deployCommands = `# 1. Save the compose file + generate your secrets
#    (one docker-compose.yml — see the full guide)
cat > .env <<EOF
POSTGRES_USER=serto
POSTGRES_PASSWORD=$(openssl rand -base64 24)
JWT_SECRET=$(openssl rand -base64 32)
ENCRYPTION_MASTER_KEY=$(openssl rand -base64 32)
EOF

# 2. Start the control plane + database
docker compose up -d

# 3. Open http://localhost:8080 and create your admin account`

// The hero snippet — a real integration (mirrors src/Examples/SqlToHttp). The attribute IS the
// manifest; RunAsync is plain C#; secrets are referenced by name, never inlined.
const heroSnippet = `// An integration is just a C# class.
[ScheduledIntegration("Order Sync", "order-sync", "*/15 * * * *")]
public class OrderSync : IIntegration
{
    public async Task RunAsync(IIntegrationContext ctx, CancellationToken ct)
    {
        var db  = ctx.SqlConnector("ORDERS_DB");
        var erp = ctx.HttpConnector("https://api.erp.com")
                     .WithBearerToken("ERP_API_KEY");

        var pending = await db.QueryAsync<Order>(
            "SELECT * FROM Orders WHERE Status = 'Pending'", ct: ct);

        foreach (var order in pending)
            await erp.PostJsonAsync("/orders", order, ct);
    }
}`

// The "why" — the low-code trap, and why owning your integrations as code is the way out.
const philosophyPoints = [
  {
    icon: GitBranch,
    title: 'It’s just code',
    description:
      'Integrations are C# in your repo. Diff them, review them in a PR, unit-test them, roll them back — with the tools you already use, not a proprietary canvas.',
  },
  {
    icon: ServerCog,
    title: 'You own the runtime',
    description:
      'They run on your infrastructure, against your systems, with your secrets. No data detour through a vendor’s cloud, no per-task metering.',
  },
  {
    icon: Unlock,
    title: 'No lock-in',
    description:
      'The SDK and CLI are MIT-licensed and your integrations are ordinary .NET. They’re portable because there’s nothing proprietary to escape.',
  },
]

export function LandingPage() {
  return (
    <main className="min-h-screen bg-background text-foreground">
      <HeroSection />
      <PhilosophySection />
      <CapabilitiesSection />
      <ScopeSection />
      <OperationsSection />
      <DeploySection />
      <SecuritySection />
      <FinalSection />
    </main>
  )
}

function HeroSection() {
  return (
    <section className="relative min-h-[92vh] overflow-hidden border-b bg-[linear-gradient(180deg,oklch(0.985_0_0),oklch(1_0_0))]">
      <div className="absolute inset-y-0 right-0 hidden w-[58%] bg-muted/35 lg:block">
        <div className="absolute inset-0 bg-[linear-gradient(90deg,oklch(1_0_0),oklch(0.985_0_0/0.2))]" />
        <img src={heroImage} alt="" className="absolute right-[14%] top-[8%] h-52 w-52 opacity-90" />
        <div className="absolute bottom-14 right-[8%] w-[34rem] rounded-lg bg-background shadow-xl">
          <CodeBlock filename="OrderSync.cs" code={heroSnippet} />
        </div>
      </div>

      <header className="relative z-10 mx-auto flex max-w-7xl items-center justify-between px-6 py-5">
        <Link to="/" className="flex items-center gap-2 text-sm font-semibold">
          <span className="flex size-8 items-center justify-center rounded-lg border bg-background">
            <Workflow className="size-4" />
          </span>
          <span>Serto</span>
        </Link>
        <nav className="flex items-center gap-2">
          <Link className={cn(buttonVariants({ variant: 'ghost' }), 'hidden sm:inline-flex')} to="/docs">
            Docs
          </Link>
          <Link className={cn(buttonVariants({ variant: 'ghost' }), 'hidden sm:inline-flex')} to="/install">
            Self-host
          </Link>
          <Link className={cn(buttonVariants({ variant: 'ghost' }), 'hidden sm:inline-flex')} to="/login">
            Sign in
          </Link>
          <Link className={cn(buttonVariants({ variant: 'default' }))} to="/app">
            Open app
            <ArrowRight className="size-4" />
          </Link>
        </nav>
      </header>

      <div className="relative z-10 mx-auto grid max-w-7xl px-6 pb-12 pt-20 lg:min-h-[calc(92vh-4.5rem)] lg:grid-cols-[0.9fr_1.1fr] lg:items-center lg:pt-0">
        <div className="max-w-3xl">
          <Badge variant="outline" className="mb-6">
            Self-hosted · Integration-as-Code
          </Badge>
          <h1 className="max-w-4xl text-5xl font-semibold leading-[1.02] tracking-normal text-foreground sm:text-6xl lg:text-7xl">
            Integrations as code, on your own infrastructure
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-muted-foreground">
            Write integrations as C# classes. Serto schedules them, stores their secrets and packages, and
            runs them on agents inside your own network — the control plane never has to see your
            credentials.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Link className={cn(buttonVariants({ size: 'lg' }))} to="/install">
              Deploy on your infrastructure
              <ArrowRight className="size-4" />
            </Link>
            <Link className={cn(buttonVariants({ variant: 'outline', size: 'lg' }))} to="/app">
              Open control plane
            </Link>
          </div>
          <p className="mt-4 text-sm text-muted-foreground">
            Free, self-hosted Community edition. Commercial licenses lift the caps.
          </p>
          <ul className="mt-6 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm text-muted-foreground">
            <li className="inline-flex items-center gap-1.5">
              <Code2 className="size-4" />
              Built for .NET
            </li>
            <li className="inline-flex items-center gap-1.5">
              <ShieldCheck className="size-4" />
              MIT-licensed SDK &amp; CLI
            </li>
            <li className="inline-flex items-center gap-1.5">
              <ServerCog className="size-4" />
              Runs on your infrastructure
            </li>
          </ul>
        </div>
      </div>
    </section>
  )
}

function PhilosophySection() {
  return (
    <section className="border-b bg-muted/35 py-24">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-3xl">
          <p className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
            The low-code trap
          </p>
          <h2 className="mt-4 text-3xl font-semibold tracking-normal sm:text-4xl">
            Stop fighting visual designers
          </h2>
          <p className="mt-5 text-lg leading-8 text-muted-foreground">
            Drag-and-drop platforms promise speed and quietly hand you lock-in. The moment an
            integration outgrows the happy path, you’re debugging a flowchart you can’t diff, can’t
            unit-test, and can’t run anywhere but their cloud — with your business logic stuck in a
            format only their designer can open. Serto is the opposite bet: your integrations are code
            you own, running where you choose.
          </p>
        </div>
        <div className="mt-12 grid gap-4 md:grid-cols-3">
          {philosophyPoints.map(item => (
            <article key={item.title} className="rounded-lg border bg-card p-6">
              <item.icon className="size-5 text-muted-foreground" />
              <h3 className="mt-5 text-base font-semibold">{item.title}</h3>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{item.description}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}

function CapabilitiesSection() {
  return (
    <section className="border-b bg-background py-20">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-2xl">
          <h2 className="text-3xl font-semibold tracking-normal">Why teams choose Serto</h2>
          <p className="mt-4 text-muted-foreground">
            The developer experience of a modern platform, without handing your systems or secrets to a
            third party.
          </p>
        </div>
        <div className="mt-10 grid gap-4 md:grid-cols-3">
          {capabilities.map(item => (
            <article key={item.title} className="rounded-lg border bg-card p-6">
              <item.icon className="size-5 text-muted-foreground" />
              <h3 className="mt-5 text-base font-semibold">{item.title}</h3>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{item.description}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}

function ScopeSection() {
  return (
    <section className="border-b bg-muted/35 py-20">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-2xl">
          <Badge variant="secondary">What&apos;s in the box</Badge>
          <h2 className="mt-5 text-3xl font-semibold tracking-normal">Three parts, one platform</h2>
          <p className="mt-4 text-muted-foreground">
            Serto is a control plane, a runtime agent, and a CLI. You author and deploy integrations; the
            platform handles scheduling, secrets, packaging, and observability around them.
          </p>
        </div>
        <div className="mt-10 grid gap-4 md:grid-cols-3">
          {scope.map(item => (
            <article key={item.title} className="rounded-lg border bg-background p-6">
              <item.icon className="size-5 text-muted-foreground" />
              <h3 className="mt-5 text-base font-semibold">{item.title}</h3>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{item.description}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}

function OperationsSection() {
  return (
    <section className="border-b bg-background py-20">
      <div className="mx-auto grid max-w-7xl gap-12 px-6 lg:grid-cols-[0.9fr_1.1fr] lg:items-center">
        <div>
          <Badge variant="secondary">Runtime workflow</Badge>
          <h2 className="mt-5 text-3xl font-semibold tracking-normal">From C# class to observed execution</h2>
          <p className="mt-4 text-muted-foreground">
            The control plane stores definitions and state. Runtime agents poll for work, fetch scoped
            secrets, run integrations, and report results — re-deploys preserve any operator changes.
          </p>
        </div>
        <div className="rounded-lg border bg-background p-4">
          {runtimeFlow.map((step, index) => (
            <div key={step} className="flex items-center gap-4 border-b py-4 last:border-b-0">
              <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-muted text-sm font-medium">
                {index + 1}
              </span>
              <span className="text-sm font-medium">{step}</span>
              {index === runtimeFlow.length - 1 && <CheckCircle2 className="ml-auto size-5 text-muted-foreground" />}
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

function DeploySection() {
  return (
    <section className="border-b bg-muted/35 py-20">
      <div className="mx-auto grid max-w-7xl gap-12 px-6 lg:grid-cols-[0.95fr_1.05fr] lg:items-center">
        <div>
          <Badge variant="secondary">Self-hosted in minutes</Badge>
          <h2 className="mt-5 text-3xl font-semibold tracking-normal">Run it on your own infrastructure</h2>
          <p className="mt-4 text-muted-foreground">
            All you need is Docker. One compose file runs the control plane and its database; you generate a
            couple of secrets and open the app. No account, no cloud dependency.
          </p>
          <Link className={cn(buttonVariants({ size: 'lg' }), 'mt-6')} to="/install">
            Full install guide
            <ArrowRight className="size-4" />
          </Link>
        </div>
        <CodeBlock filename="get started" code={deployCommands} />
      </div>
    </section>
  )
}

function SecuritySection() {
  return (
    <section className="border-b bg-background py-20">
      <div className="mx-auto max-w-7xl px-6">
        <div className="grid gap-4 lg:grid-cols-3">
          <FeatureBand
            icon={ShieldCheck}
            title="Tenant scoped"
            text="Resources are isolated by tenant across integrations, secrets, tokens, packages, and execution records."
          />
          <FeatureBand
            icon={KeyRound}
            title="Secrets stay on-prem"
            text="Values are encrypted at rest and only returned over agent-token endpoints; the external-vault backend keeps them off the control plane entirely."
          />
          <FeatureBand
            icon={ServerCog}
            title="Agent controlled"
            text="Agents run near internal systems and connect outbound only — no inbound access to the control plane required."
          />
        </div>
      </div>
    </section>
  )
}

function FeatureBand({ icon: Icon, title, text }: { icon: typeof ShieldCheck; title: string; text: string }) {
  return (
    <div className="flex gap-4 rounded-lg border p-5">
      <Icon className="mt-0.5 size-5 shrink-0 text-muted-foreground" />
      <div>
        <h3 className="font-semibold">{title}</h3>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">{text}</p>
      </div>
    </div>
  )
}

function FinalSection() {
  return (
    <section className="bg-muted/35 py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-6 md:flex-row md:items-center md:justify-between">
        <div>
          <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
            <Workflow className="size-4" />
            Free Community edition · self-hosted · your code is the manifest.
          </div>
          <h2 className="mt-3 text-2xl font-semibold tracking-normal">Stand up Serto on your own infrastructure.</h2>
        </div>
        <Link className={cn(buttonVariants({ size: 'lg' }))} to="/install">
          Get started
          <ArrowRight className="size-4" />
        </Link>
      </div>
    </section>
  )
}
