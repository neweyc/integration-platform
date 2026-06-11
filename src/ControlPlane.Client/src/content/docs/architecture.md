# Architecture

Serto has two parts: a **control plane** and one or more **runtime agents**. Keeping them separate is what lets your integrations run inside your own network while you manage everything from one place.

## Control plane

The control plane is the API and dashboard. It stores your integration packages, schedules them, keeps execution history, and manages secrets and access. It's the thing you log into, and you self-host it (Docker) wherever your team can reach it.

The control plane decides *what* should run and *when* — but it does not run your integrations itself.

## Runtime agents

A runtime agent is a small worker you run wherever your integrations need to execute — next to a database, inside a VPC, or on a small box at the edge. Agents poll the control plane for work that's due, run it, and report results back.

Because the agent runs in your environment, your integrations reach your systems directly. The control plane never needs a network path to your databases or internal APIs.

## How a deploy flows

1. You write integrations in C# and run `serto deploy`.
2. The CLI packages them and uploads to the control plane, which reads the trigger attributes and provisions schedules.
3. When an integration is due, the control plane marks it as work to be claimed.
4. An eligible agent in the right environment claims it, runs it, and streams logs and status back.

## Secrets stay put

Secrets are referenced by name in code. You can store their values in the control plane, or — for the strongest isolation — keep them in an on-prem vault that only the agent can reach. In that mode the control plane holds a *reference*, not the value, and the credential never leaves your network.

## Environments

Everything is scoped to an environment, such as `production` or `staging`. An agent serves one environment, and secrets and integrations are isolated per environment — so staging can't read production credentials.

## Next steps

- [Writing integrations](/docs/writing-integrations)
- [Self-host the control plane](/install)
