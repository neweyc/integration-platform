import { Link } from 'react-router-dom'
import { ArrowRight, ArrowLeft, BookText, Workflow } from 'lucide-react'
import { buttonVariants } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import { docs } from '@/content/docs/manifest'

export function DocsPage() {
  return (
    <main className="min-h-screen bg-background text-foreground">
      <DocsHeader />

      <div className="mx-auto max-w-3xl px-6 py-16">
        <Badge variant="outline" className="mb-5">
          Documentation
        </Badge>
        <h1 className="text-4xl font-semibold tracking-normal sm:text-5xl">Docs</h1>
        <p className="mt-5 text-lg leading-8 text-muted-foreground">
          Everything you need to write integrations as code and run them on your own infrastructure.
        </p>

        <ul className="mt-12 space-y-4">
          {docs.map(doc => (
            <li key={doc.slug}>
              <Link
                to={`/docs/${doc.slug}`}
                className="flex items-start gap-4 rounded-lg border bg-card p-5 transition-colors hover:bg-muted/40"
              >
                <BookText className="mt-0.5 size-5 shrink-0 text-muted-foreground" />
                <span className="min-w-0">
                  <span className="block font-semibold">{doc.title}</span>
                  <span className="mt-1 block text-sm text-muted-foreground">{doc.description}</span>
                </span>
                <ArrowRight className="ml-auto mt-0.5 size-4 shrink-0 text-muted-foreground" />
              </Link>
            </li>
          ))}
        </ul>
      </div>
    </main>
  )
}

export function DocsHeader() {
  return (
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
  )
}
