import { Link } from 'react-router-dom'
import {
  ArrowRight,
  Boxes,
  CheckCircle2,
  Code2,
  KeyRound,
  LockKeyhole,
  RadioTower,
  ServerCog,
  ShieldCheck,
  Workflow,
} from 'lucide-react'
import heroImage from '@/assets/hero.png'
import { buttonVariants } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

const capabilities = [
  {
    icon: Code2,
    title: 'Code-first integrations',
    description: 'Ship C# classes instead of maintaining brittle low-code workflows.',
  },
  {
    icon: KeyRound,
    title: 'Scoped secrets',
    description: 'Keep encrypted secrets tenant- and environment-scoped with agent-only retrieval.',
  },
  {
    icon: RadioTower,
    title: 'On-prem execution',
    description: 'Run agents near private systems while the control plane owns configuration.',
  },
]

const workflow = [
  'Register the integration class',
  'Deploy the compiled package',
  'Agent runs the scheduled job',
  'Review results and logs',
]

export function LandingPage() {
  return (
    <main className="min-h-screen bg-background text-foreground">
      <HeroSection />
      <CapabilitiesSection />
      <OperationsSection />
      <SecuritySection />
      <FinalSection />
    </main>
  )
}

function HeroSection() {
  return (
    <section className="relative min-h-[92vh] overflow-hidden border-b bg-[linear-gradient(180deg,oklch(0.985_0_0),oklch(1_0_0))]">
      <div className="absolute inset-y-0 right-0 hidden w-[58%] border-l bg-muted/35 lg:block">
        <div className="absolute inset-0 bg-[linear-gradient(90deg,oklch(1_0_0),oklch(0.985_0_0/0.2))]" />
        <img
          src={heroImage}
          alt=""
          className="absolute right-[14%] top-[18%] h-64 w-64 opacity-90"
        />
        <div className="absolute bottom-16 right-[12%] w-[38rem] rounded-lg border bg-background/90 p-4 shadow-sm">
          <div className="grid grid-cols-[1.4fr_0.9fr_0.9fr_0.8fr] gap-3 border-b pb-3 text-xs font-medium text-muted-foreground">
            <span>Integration</span>
            <span>Environment</span>
            <span>Last run</span>
            <span>Status</span>
          </div>
          <HeroTableRow name="Sync orders" env="production" run="2 min ago" status="Succeeded" />
          <HeroTableRow name="Refresh inventory" env="staging" run="12 min ago" status="Running" />
          <HeroTableRow name="Export invoices" env="production" run="1 hr ago" status="Succeeded" />
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
            Product name TBD
          </Badge>
          <h1 className="max-w-4xl text-5xl font-semibold leading-[1.02] tracking-normal text-foreground sm:text-6xl lg:text-7xl">
            Code-first integration infrastructure
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-muted-foreground">
            A control plane for scheduling, secrets, package storage, runtime agents, execution history, and structured logs.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Link className={cn(buttonVariants({ size: 'lg' }))} to="/app">
              Open control plane
              <ArrowRight className="size-4" />
            </Link>
            <Link className={cn(buttonVariants({ variant: 'outline', size: 'lg' }))} to="/setup">
              First-time setup
            </Link>
          </div>
        </div>
      </div>
    </section>
  )
}

function HeroTableRow({
  name,
  env,
  run,
  status,
}: {
  name: string
  env: string
  run: string
  status: string
}) {
  return (
    <div className="grid grid-cols-[1.4fr_0.9fr_0.9fr_0.8fr] gap-3 border-b py-3 text-sm last:border-b-0">
      <span className="font-medium">{name}</span>
      <span className="text-muted-foreground">{env}</span>
      <span className="text-muted-foreground">{run}</span>
      <span className="font-medium">{status}</span>
    </div>
  )
}

function CapabilitiesSection() {
  return (
    <section className="border-b bg-background py-20">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-2xl">
          <h2 className="text-3xl font-semibold tracking-normal">Built for operational integrations</h2>
          <p className="mt-4 text-muted-foreground">
            Keep integration logic in source control while the platform handles the surrounding operational work.
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

function OperationsSection() {
  return (
    <section className="border-b bg-muted/35 py-20">
      <div className="mx-auto grid max-w-7xl gap-12 px-6 lg:grid-cols-[0.9fr_1.1fr] lg:items-center">
        <div>
          <Badge variant="secondary">Runtime workflow</Badge>
          <h2 className="mt-5 text-3xl font-semibold tracking-normal">From C# class to observed execution</h2>
          <p className="mt-4 text-muted-foreground">
            The control plane stores definitions and state. Runtime agents poll for work, fetch scoped secrets, run integrations, and report results.
          </p>
        </div>
        <div className="rounded-lg border bg-background p-4">
          {workflow.map((step, index) => (
            <div key={step} className="flex items-center gap-4 border-b py-4 last:border-b-0">
              <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-muted text-sm font-medium">
                {index + 1}
              </span>
              <span className="text-sm font-medium">{step}</span>
              {index === workflow.length - 1 && <CheckCircle2 className="ml-auto size-5 text-muted-foreground" />}
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

function SecuritySection() {
  return (
    <section className="border-b bg-background py-20">
      <div className="mx-auto max-w-7xl px-6">
        <div className="grid gap-4 lg:grid-cols-3">
          <FeatureBand icon={ShieldCheck} title="Tenant scoped" text="Resources are isolated by tenant across integrations, secrets, tokens, packages, and execution records." />
          <FeatureBand icon={LockKeyhole} title="Encrypted secrets" text="Secret values are encrypted at rest and only returned through agent-token protected runtime endpoints." />
          <FeatureBand icon={ServerCog} title="Agent controlled" text="Agents run near internal systems without exposing inbound access to the control plane." />
        </div>
      </div>
    </section>
  )
}

function FeatureBand({
  icon: Icon,
  title,
  text,
}: {
  icon: typeof ShieldCheck
  title: string
  text: string
}) {
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
            <Boxes className="size-4" />
            Package storage, execution logs, and agent polling are available now.
          </div>
          <h2 className="mt-3 text-2xl font-semibold tracking-normal">Open the control plane to configure your first integration.</h2>
        </div>
        <Link className={cn(buttonVariants({ size: 'lg' }))} to="/app">
          Continue
          <ArrowRight className="size-4" />
        </Link>
      </div>
    </section>
  )
}
