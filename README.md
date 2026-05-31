# Integration Platform

A code-first integration platform. Integrations are written as C# classes rather than configured via low-code drag-and-drop.

## Stack

- **Backend**: ASP.NET Core 10, EF Core, PostgreSQL
- **Frontend**: React 19, Vite, TypeScript, TanStack Query, shadcn/ui, Tailwind

## Local development

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker

### 1. Start the database

```bash
docker-compose -f docker-compose.dev.yml up -d
```

### 2. Start the API

```bash
$HOME/.dotnet/dotnet run --project src/ControlPlane
```

Runs on `http://localhost:5000`. Migrations are applied automatically on startup.

### 3. Start the frontend dev server

```bash
cd src/ControlPlane.Client && npm run dev
```

Runs on `http://localhost:5173`. API calls are proxied to the backend automatically.

> **Note**: Use port 5173 during development, not 5000. Hot module replacement means changes are reflected instantly without rebuilding.

### First run

Navigate to `http://localhost:5173` — you'll be directed to `/setup` to create the first tenant and admin user.

## Connecting to the database (pgAdmin)

| Field    | Value                  |
|----------|------------------------|
| Host     | `localhost`            |
| Port     | `5433`                 |
| Database | `integrationplatform`  |
| Username | `devuser`              |
| Password | `devpassword`          |

Port is `5433` (not the default 5432) to avoid conflicts with any local Postgres instance.

## Building the frontend for production

```bash
cd src/ControlPlane.Client && npm run build
```

Output goes to `src/ControlPlane/wwwroot` and is served by the .NET server at `http://localhost:5000`.

## Running tests

```bash
$HOME/.dotnet/dotnet test IntegrationPlatform.slnx
```
