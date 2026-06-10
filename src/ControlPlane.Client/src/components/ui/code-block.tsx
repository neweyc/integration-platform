import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { cn } from '@/lib/utils'

// A copy-to-clipboard code block. Used on the public landing/install pages so setup commands are one
// click to copy — no manual selection.
export function CodeBlock({
  code,
  filename,
  className,
}: {
  code: string
  filename?: string
  className?: string
}) {
  const [copied, setCopied] = useState(false)

  function handleCopy() {
    navigator.clipboard.writeText(code)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <div className={cn('overflow-hidden rounded-lg border bg-muted/40', className)}>
      <div className="flex items-center justify-between border-b bg-muted/60 px-3 py-1.5">
        <span className="font-mono text-xs text-muted-foreground">{filename ?? 'shell'}</span>
        <button
          type="button"
          onClick={handleCopy}
          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
          aria-label="Copy to clipboard"
        >
          {copied ? <Check className="size-3.5" /> : <Copy className="size-3.5" />}
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <pre className="overflow-x-auto p-4 text-sm leading-6">
        <code className="font-mono">{code}</code>
      </pre>
    </div>
  )
}
