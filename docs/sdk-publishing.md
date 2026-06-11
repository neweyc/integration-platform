# SDK publishing

The platform ships SDKs in four languages. Three publish automatically from the tag-driven release
workflow (`.github/workflows/publish.yml`, fires on `v*` tags); Go publishes from its own repo.

| SDK | Registry | Package | Source | Released by |
|-----|----------|---------|--------|-------------|
| .NET | NuGet | `Serto.Sdk`, `Serto.Connectors`, `Serto.Testing`, `Serto.Cli` | `src/` | `publish.yml` (nuget job) |
| Python | PyPI | **`serto-sdk`** (import: `serto`) | `sdks/python` | `publish.yml` (pypi job) |
| Node | npm | **`serto`** | `sdks/node/serto` | `publish.yml` (npm job) |
| Go | Go module proxy | **`github.com/neweyc/integration-platform/sdks/go/serto`** | `sdks/go/serto` | `publish.yml` (go-module-tag job) |

> The bare `serto` name was already taken on PyPI, so the Python distribution is `serto-sdk` while the
> import package stays `serto` (`pip install serto-sdk` → `import serto`).

All three registry jobs stamp the version from the git tag (`v1.2.3` → `1.2.3`) into the manifest before
building, so SDK versions track the platform release.

## One-time setup

**PyPI (Trusted Publishing — no stored token).** On PyPI, add a *pending publisher* for the project
`serto-sdk`: publisher = GitHub Actions, owner `neweyc`, repo `integration-platform`, workflow
`publish.yml`. The `pypi` job authenticates via OIDC (`id-token: write`); no secret needed.

**npm.** Create an npm **automation** token and add it as the repo secret `NPM_TOKEN`. The `npm` job
publishes with `--provenance` (requires `id-token: write` and a public repo).

**Go.** None — the SDK lives in this monorepo and releases automatically (see below). No registry account
or token is needed; the module proxy fetches from the repo's tags.

## Cutting a release

Push a `vX.Y.Z` tag to this repo — `publish.yml` does everything: NuGet, PyPI, npm, Docker, and the Go
module. Go resolves a module in a subdirectory via a path-prefixed tag, so the `go-module-tag` job mirrors
the release to a `sdks/go/serto/vX.Y.Z` tag on the same commit. After that,
`go get github.com/neweyc/integration-platform/sdks/go/serto@vX.Y.Z` works.

## CI

`ci.yml` runs each SDK's tests on every PR (`python-sdk`, `node-sdk`, `go-sdk` jobs) so they can't rot
between releases.
