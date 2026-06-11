import { Link, useParams } from 'react-router-dom'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { ArrowLeft } from 'lucide-react'
import { buttonVariants } from '@/components/ui/button'
import { CodeBlock } from '@/components/ui/code-block'
import { cn } from '@/lib/utils'
import { docs, getDoc } from '@/content/docs/manifest'
import { DocsHeader } from './DocsPage'

export function DocPage() {
  const { slug } = useParams()
  const doc = getDoc(slug)

  if (!doc) {
    return (
      <main className="min-h-screen bg-background text-foreground">
        <DocsHeader />
        <div className="mx-auto max-w-3xl px-6 py-24 text-center">
          <h1 className="text-2xl font-semibold">Page not found</h1>
          <p className="mt-3 text-muted-foreground">That doc doesn’t exist (or isn’t published yet).</p>
          <Link className={cn(buttonVariants({ variant: 'outline' }), 'mt-6')} to="/docs">
            <ArrowLeft className="size-4" />
            All docs
          </Link>
        </div>
      </main>
    )
  }

  return (
    <main className="min-h-screen bg-background text-foreground">
      <DocsHeader />

      <div className="mx-auto max-w-3xl px-6 py-16">
        <Link
          className="mb-8 inline-flex items-center gap-1.5 text-sm text-muted-foreground transition-colors hover:text-foreground"
          to="/docs"
        >
          <ArrowLeft className="size-4" />
          Docs
        </Link>

        <article>
          <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents}>
            {doc.content}
          </ReactMarkdown>
        </article>

        <DocFooterNav slug={doc.slug} />
      </div>
    </main>
  )
}

function DocFooterNav({ slug }: { slug: string }) {
  const index = docs.findIndex(d => d.slug === slug)
  const next = index >= 0 ? docs[index + 1] : undefined
  if (!next) return null
  return (
    <div className="mt-16 border-t pt-6">
      <Link
        to={`/docs/${next.slug}`}
        className="flex items-center justify-between rounded-lg border bg-card p-4 transition-colors hover:bg-muted/40"
      >
        <span>
          <span className="block text-xs text-muted-foreground">Next</span>
          <span className="font-semibold">{next.title}</span>
        </span>
      </Link>
    </div>
  )
}

// Styled renderers for the markdown elements our docs use. All doc content is authored in-repo, so
// links are trusted; internal links go through the SPA router, external links open in a new tab.
const markdownComponents = {
  h1: (props: { children?: React.ReactNode }) => (
    <h1 className="mb-4 text-4xl font-semibold tracking-normal sm:text-5xl">{props.children}</h1>
  ),
  h2: (props: { children?: React.ReactNode }) => (
    <h2 className="mb-3 mt-12 text-2xl font-semibold tracking-normal">{props.children}</h2>
  ),
  h3: (props: { children?: React.ReactNode }) => (
    <h3 className="mb-2 mt-8 text-lg font-semibold">{props.children}</h3>
  ),
  p: (props: { children?: React.ReactNode }) => (
    <p className="mb-4 leading-7 text-muted-foreground">{props.children}</p>
  ),
  ul: (props: { children?: React.ReactNode }) => (
    <ul className="mb-4 ml-5 list-disc space-y-2 leading-7 text-muted-foreground">{props.children}</ul>
  ),
  ol: (props: { children?: React.ReactNode }) => (
    <ol className="mb-4 ml-5 list-decimal space-y-2 leading-7 text-muted-foreground">{props.children}</ol>
  ),
  li: (props: { children?: React.ReactNode }) => <li className="pl-1">{props.children}</li>,
  strong: (props: { children?: React.ReactNode }) => (
    <strong className="font-semibold text-foreground">{props.children}</strong>
  ),
  hr: () => <hr className="my-10 border-border" />,
  a: (props: { href?: string; children?: React.ReactNode }) => {
    const href = props.href ?? '#'
    const className = 'font-medium text-foreground underline underline-offset-4 hover:no-underline'
    if (href.startsWith('/')) {
      return (
        <Link to={href} className={className}>
          {props.children}
        </Link>
      )
    }
    return (
      <a href={href} target="_blank" rel="noreferrer" className={className}>
        {props.children}
      </a>
    )
  },
  pre: (props: { children?: React.ReactNode }) => <>{props.children}</>,
  code: (props: { className?: string; children?: React.ReactNode }) => {
    const match = /language-(\w+)/.exec(props.className ?? '')
    if (match) {
      return (
        <CodeBlock
          className="my-5"
          filename={match[1]}
          code={String(props.children).replace(/\n$/, '')}
        />
      )
    }
    return (
      <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-[0.85em] text-foreground">
        {props.children}
      </code>
    )
  },
}
