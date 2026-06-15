# Walkthrough: a nightly ERP payment run (Oracle EBS → payments API)

A complete, real-world integration end to end — from creating secrets to writing the code, deploying it, and watching it run. The example is deliberately the kind of operational job that is painful to build with traditional tooling, so you can see where Serto earns its keep.

> Prerequisites: a running control plane and the `serto` CLI (see the [Quick Start](quickstart.md)), plus a runtime agent you can run inside the network that can reach your ERP database.

---

## The scenario

An accounts-payable team runs a **nightly payment run**. Supplier invoices that finance has approved sit in an Oracle E‑Business Suite database. Each one needs to be:

1. **paid** through the bank/payments provider's REST API, then
2. **reconciled** — the payment reference written back into EBS and the invoice marked paid.

### Why this hurts today

In a typical shop this is a chain of brittle parts: a Control‑M job kicks off a PL/SQL package that spools approved invoices to a CSV; an analyst SFTPs the file to the bank portal; the next morning someone downloads a confirmation file and keys the payment references back into EBS by hand. There's no real audit trail, a failure halfway through is a manual cleanup, and the bank's modern REST API sits unused because wiring it through a low‑code iPaaS is a licensed, multi‑week, change‑controlled project.

### What we'll build instead

About **70 lines of C#**, deployed in minutes. The runtime **agent runs inside the corporate network**, so EBS is never exposed to the internet — it only makes an outbound connection to the control plane. Payments are **idempotent**, failures are **isolated and retried**, and every run lands in **execution history** with logs.

```
            ┌─────────────────────────┐
            │   Control plane (cloud  │   scheduling, secrets, history
            │   or DMZ)               │   — never touches your DB
            └────────────┬────────────┘
                         │ outbound poll (agent dials out)
                         ▼
   corporate network ┌─────────────────────┐
   (behind firewall) │   Runtime agent     │  tag: oracle-ebs
                     │   runs the C# code  │
                     └───┬─────────────┬───┘
                         │             │
              Oracle SQL │             │ HTTPS REST
                         ▼             ▼
                 ┌───────────────┐ ┌──────────────────┐
                 │  Oracle EBS   │ │  Payments API    │
                 │  (on-prem DB) │ │  (bank / Tipalti)│
                 └───────────────┘ └──────────────────┘
```

---

## Step 1 — Create the environment and its secrets

Secrets in Serto are **scoped to an environment** (e.g. `production`), encrypted at rest, and **never returned** by the API — the agent receives them only at execution time. The integration reads them by name through `context.Secrets`; your code never embeds a credential.

We need two:

| Secret key       | Value                                              |
|------------------|----------------------------------------------------|
| `ORACLE_ERP_CONN`| Oracle connection string for the AP schema         |
| `PAY_API_KEY`    | Bearer token for the payments provider             |

Secret keys follow the environment-variable convention: **start with a letter, uppercase letters / digits / underscores only** (`^[A-Z][A-Z0-9_]*$`). The environment must exist before you set secrets for it.

### Option A — the UI

Open **Secrets**, choose the `production` environment, and add each key/value. Existing values are shown only as "set"; updating is a fresh write.

### Option B — the control-plane API

Using the personal access token from the **Developer** tab (a token whose user has the *Manage secrets* permission):

```bash
export SERTO_API_TOKEN=pat_...

curl -sS -X PUT https://serto.example.com/api/secrets/production/ORACLE_ERP_CONN \
  -H "Authorization: Bearer $SERTO_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value":"Data Source=ebs-db.corp:1521/PROD;User Id=SERTO_AP;Password=•••"}'

curl -sS -X PUT https://serto.example.com/api/secrets/production/PAY_API_KEY \
  -H "Authorization: Bearer $SERTO_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value":"sk_live_•••"}'
```

To rotate a credential later, `PUT` the same key again — the next run picks up the new value, no redeploy needed.

> **Least privilege:** give `SERTO_AP` only what it needs — `SELECT` on the approved-invoice view and `UPDATE` on the payment-status column. The agent runs inside your network; the database user is your real blast-radius control.

---

## Step 2 — Create the project

Your **code is the manifest** — the attributes in the integration declare it, its schedule, and which agent may run it. There are two ways to get the project; pick one, the result is the same.

### Option A — scaffold it with `serto init` (recommended)

```bash
serto init AcmePaymentRun
cd AcmePaymentRun
```

This generates a ready-to-build project that **already references both `Serto.Sdk` and `Serto.Connectors`**, so the Oracle and HTTP connectors are available immediately — plus a test project and a local-secrets template:

```
AcmePaymentRun/
├─ AcmePaymentRun.csproj        # references Serto.Sdk + Serto.Connectors
├─ MyIntegration.cs             # a scheduled stub you'll replace
├─ .secrets.example.json        # template for local-run secrets
├─ .gitignore                   # ignores secrets.json, bin/, obj/
└─ AcmePaymentRun.Tests/        # xUnit project wired to Serto.Testing
```

Open `MyIntegration.cs` and replace the stub with the payment run below (and rename the class to `ApPaymentRun`).

> Other starters: `serto init <name> --template webhook` for a webhook-triggered .NET integration, or `--runtime python|node|go|shell` to scaffold in another language. We're using the defaults (`dotnet`, `scheduled`).

### Option B — create it by hand

If you'd rather not scaffold, a minimal project is just a csproj referencing the two packages:

`AcmePaymentRun.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serto.Sdk" Version="1.5.4" />
    <PackageReference Include="Serto.Connectors" Version="1.5.4" />
  </ItemGroup>
</Project>
```

### The integration

Either way, this is the code (paste it into `MyIntegration.cs` if you scaffolded — the file name doesn't matter in C#):

```csharp
using Serto.Sdk;
using Serto.Connectors.Sql;
using Serto.Connectors.Http;
using Microsoft.Extensions.Logging;

namespace AcmePaymentRun;

// Runs at 02:00 every weekday. The capability tag pins it to the on-prem agent that can reach
// Oracle EBS — the control plane will not hand this work to any other agent.
[ScheduledIntegration(
    name: "AP Payment Run",
    slug: "ap-payment-run",
    cronExpression: "0 2 * * 1-5",
    Description = "Pay approved supplier invoices via the bank API and reconcile back to EBS.")]
[RequiresAgentCapabilities("oracle-ebs")]
public sealed class ApPaymentRun : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        // Connectors take the SECRET KEY, not the value — resolved from the environment at runtime.
        var erp = context.OracleConnector("ORACLE_ERP_CONN");
        var pay = context.HttpConnector("https://api.payments.example.com")
            .WithBearerToken("PAY_API_KEY")
            .WithRetryPolicy(maxRetries: 3);   // 429/5xx retried with backoff, honoring Retry-After

        // 1. Read invoices finance has approved but we haven't paid yet.
        var invoices = (await erp.QueryAsync<Invoice>(
            """
            SELECT invoice_id   AS InvoiceId,
                   supplier_ref AS SupplierRef,
                   amount       AS Amount,
                   currency     AS Currency
            FROM   ap_payments_queue
            WHERE  status = 'APPROVED'
            """, ct: ct)).ToList();

        context.Logger.LogInformation("Found {Count} approved invoices to pay.", invoices.Count);

        var failures = 0;
        foreach (var invoice in invoices)
        {
            try
            {
                // 2. Pay it. The invoice id is the idempotency key, so a retry — or a re-run after a
                //    crash mid-batch — never double-pays: the bank dedupes on the same key.
                var receipt = await pay
                    .WithIdempotencyKey($"ap-{invoice.InvoiceId}")
                    .PostJsonAsync<PaymentRequest, PaymentReceipt>(
                        "/payments",
                        new PaymentRequest(invoice.SupplierRef, invoice.Amount, invoice.Currency),
                        ct);

                // 3. Reconcile: write the bank reference back and mark it paid — same run, one place.
                //    Oracle binds these :Named parameters by name (the connector handles BindByName).
                await erp.ExecuteAsync(
                    """
                    UPDATE ap_payments_queue
                    SET    status = 'PAID', payment_ref = :PaymentRef, paid_at = SYSDATE
                    WHERE  invoice_id = :InvoiceId
                    """,
                    new { receipt?.PaymentRef, invoice.InvoiceId }, ct);

                context.Logger.LogInformation(
                    "Paid invoice {InvoiceId} -> {PaymentRef}", invoice.InvoiceId, receipt?.PaymentRef);
            }
            catch (Exception ex)
            {
                // One bad invoice shouldn't strand the rest. Log it, count it, keep going. The good
                // ones are paid-and-committed; the run still ends failed so it shows red and alerts.
                failures++;
                context.Logger.LogError(ex, "Failed to pay invoice {InvoiceId}", invoice.InvoiceId);
            }
        }

        if (failures > 0)
            throw new InvalidOperationException($"{failures} of {invoices.Count} invoices failed to pay.");
    }

    private record Invoice(string InvoiceId, string SupplierRef, decimal Amount, string Currency);
    private record PaymentRequest(string SupplierRef, decimal Amount, string Currency);
    private record PaymentReceipt(string PaymentRef);
}
```

What's doing the heavy lifting here, none of which you had to write:

- **Secrets by reference** — `OracleConnector("ORACLE_ERP_CONN")` / `WithBearerToken("PAY_API_KEY")` resolve from the environment at runtime; no credential is in the code or the package.
- **Idempotent writes** — `WithIdempotencyKey` makes a retried or replayed `POST` safe. (The HTTP connector only retries non‑idempotent verbs *when* a key is set, precisely so a payment can't be silently duplicated.)
- **Retries with backoff** — `WithRetryPolicy` handles `429`/`5xx`, honoring `Retry-After`.
- **Correct Oracle binding** — the SQL connector flips `BindByName` for Oracle, so `:Named` parameters bind by name like every other provider instead of by position.
- **Partial‑failure semantics** — paid invoices commit individually; the run is still marked failed if any invoice failed, so it surfaces in history and triggers alerting.

If you scaffolded (Option A), point the generated test at the renamed class: in `AcmePaymentRun.Tests/MyIntegrationTests.cs`, change `RunAsync<MyIntegration>` to `RunAsync<ApPaymentRun>`. Note that this integration talks to Oracle, so the generated "runs without error" test needs a reachable test database — for DB‑backed work, validate end‑to‑end with `serto test` against a test DB (next step) and keep the xUnit project for pure mapping/transform logic.

Build it:

```bash
dotnet build
```

---

## Step 3 — Preview, test, deploy

> The CLI is `serto` once installed as a .NET global tool. From a repo checkout you can substitute `dotnet run --project src/Cli --` for `serto`. Run these from inside the integration project directory.

### Preview what the control plane will provision

```bash
serto scan
```

`scan` reflects over your build output and prints the discovered integration, its `ap-payment-run` scheduled trigger, the run policy, and — by static analysis of your code — the **required secrets**:

```
Required secrets: ORACLE_ERP_CONN, PAY_API_KEY
```

(The scanner recognizes `OracleConnector("…")`, `WithBearerToken("…")`, `context.Secrets["…"]`, and the other connector/secret call shapes, so this list stays honest as the code changes.)

### Dry-run it locally

Before it ever touches production, run the integration on your machine to prove the wiring. `test` reads secrets from a local JSON file rather than the control plane. Copy the scaffolded template and fill in test values (`secrets.json` is already git‑ignored):

```bash
cp .secrets.example.json secrets.json
# edit secrets.json:
# { "ORACLE_ERP_CONN": "Data Source=…test-db…", "PAY_API_KEY": "sk_test_…" }

serto test AcmePaymentRun.ApPaymentRun --secrets secrets.json
```

`test` builds the project, runs a preflight (the class is discoverable and instantiable, and every required secret is present in the file — it warns about any referenced in code but missing), then executes `RunAsync` locally and streams the logs — the same code path the agent will use. Point it at a test database and the payments provider's sandbox key first.

### Deploy

```bash
serto deploy --url https://serto.example.com
```

`deploy` packages the project, uploads it (SHA‑256 verified), and **auto‑provisions** the integration and its schedule. The output ends with a **secret check** — required vs. configured — so you find a missing `ORACLE_ERP_CONN` *now*, not at 02:00. Re‑deploying preserves operator overrides (if someone pauses the schedule or edits the cron in the UI, a later deploy keeps the override and reports it as drift).

---

## Step 4 — Run it on the on-prem agent

The control plane schedules; an **agent executes**, and this one must run where it can reach Oracle. On a host inside the network:

1. In the UI, create an **agent token** for the `production` environment (`agt_…`, shown once).
2. Start the agent, advertising the `oracle-ebs` capability so it's eligible for this work:

```bash
export Agent__ControlPlaneUrl=https://serto.example.com
export Agent__AgentToken=agt_...
export Agent__Environment=production
export Agent__Tags__0=oracle-ebs          # matches [RequiresAgentCapabilities("oracle-ebs")]
export Agent__PackagesPath=./packages
dotnet run --project path/to/src/RuntimeAgent
```

The agent dials out to the control plane (no inbound ports), downloads the verified package, and polls for due work. Because the integration requires the `oracle-ebs` tag, **only** an agent offering that tag will ever claim it — so your payment run can't accidentally execute somewhere without database reach.

---

## Step 5 — Watch it work

You don't have to wait for 02:00. Open the integration in the UI and click **Run now**; within a poll interval the on‑prem agent claims and executes it. Open **execution history** to see the run, its status, and the `Paid invoice … -> …` log lines. Trigger it again and the idempotency keys mean already‑paid invoices are safe even if the prior run half‑finished.

---

## What just happened

- A real operational job — read an ERP DB, call an external API idempotently, reconcile back — went from a brittle, multi‑system, partly‑manual Control‑M/SFTP chain to **one reviewable C# file** under version control.
- **Nothing was exposed.** The database stayed behind the firewall; the agent only made an outbound connection; secrets stayed in the control plane and reached the code only at run time.
- You got **idempotency, retries, partial‑failure handling, audit history, and alerting** as properties of the platform, not as boilerplate you maintained.

## Next steps

- [Writing integrations](writing-integrations.md) — webhook, manual, queue, and message triggers; more connector patterns.
- [SDK, connectors, and the connector philosophy](sdk-connectors-and-adapters.md) — why connectors are transport‑level (HTTP, multi‑provider SQL), not per‑vendor.
- [Installation guide](installation.md) — production deployment and configuration.
