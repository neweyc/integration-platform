import quickstart from './quickstart.md?raw'
import writingIntegrations from './writing-integrations.md?raw'
import connectors from './connectors.md?raw'
import secrets from './secrets.md?raw'
import architecture from './architecture.md?raw'
import cliReference from './cli-reference.md?raw'

export interface DocEntry {
  slug: string
  title: string
  description: string
  content: string
}

// The ONLY docs published to the public site, in nav order. These are authored for public
// consumption; the repo's internal docs/ folder is never imported here, so nothing internal can
// leak through this page. To publish a new doc, add a markdown file in this folder and list it here.
export const docs: DocEntry[] = [
  {
    slug: 'quickstart',
    title: 'Quick start',
    description: 'From zero to a running integration.',
    content: quickstart,
  },
  {
    slug: 'writing-integrations',
    title: 'Writing integrations',
    description: 'Triggers, the context, connectors, and secrets.',
    content: writingIntegrations,
  },
  {
    slug: 'connectors',
    title: 'Connectors',
    description: 'HTTP and SQL helpers — auth, retries, and queries.',
    content: connectors,
  },
  {
    slug: 'secrets',
    title: 'Secrets',
    description: 'Reference credentials by name; keep values off the control plane.',
    content: secrets,
  },
  {
    slug: 'architecture',
    title: 'Architecture',
    description: 'Control plane, runtime agents, and how secrets stay put.',
    content: architecture,
  },
  {
    slug: 'cli-reference',
    title: 'CLI reference',
    description: 'The serto commands: init, scan, test, deploy.',
    content: cliReference,
  },
]

export function getDoc(slug: string | undefined): DocEntry | undefined {
  return docs.find(d => d.slug === slug)
}
