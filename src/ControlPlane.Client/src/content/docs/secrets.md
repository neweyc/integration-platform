# Secrets

Integrations never contain credentials. You reference a secret by name in code, and Serto resolves the value at run time. That keeps secrets out of your source, out of your packages, and — if you want — off the control plane entirely.

## Referencing a secret

Wherever a connector or your code needs a credential, pass the secret's **name**:

```csharp
var api = ctx.HttpConnector("https://api.erp.com").WithBearerToken("ERP_API_KEY");
var db  = ctx.SqlConnector("ORDERS_DB");
```

You can also read any secret directly:

```csharp
var token = ctx.Secrets["ERP_API_KEY"];
```

## Two ways to store the value

**Inline (control plane).** You enter the value in the dashboard; the control plane stores it encrypted and hands it to the agent at run time. Simple, and right for most cases.

**Reference (on-prem vault).** The value lives in a vault inside your own network. The control plane stores only a *reference* — never the secret itself. At run time the agent resolves that reference against the vault locally, so the credential never touches the control plane. This is the strongest isolation, and the right choice when the control plane runs somewhere you don't want holding production credentials.

Either way your integration code is identical: it asks for `"ERP_API_KEY"` and gets a value.

## Per-environment isolation

Secrets are scoped to an environment. `production` and `staging` have separate secret sets, so a staging run can't read production credentials — even for the same integration.

## Next steps

- [Architecture](/docs/architecture) — where secrets are resolved, and why that matters.
- [Connectors](/docs/connectors) — what consumes these secrets.
