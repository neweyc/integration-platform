#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT_DIR"

git diff --check

"$HOME/.dotnet/dotnet" restore Serto.slnx
"$HOME/.dotnet/dotnet" build Serto.slnx --no-restore -warnaserror -warnnotaserror:NU1900 /nr:false
"$HOME/.dotnet/dotnet" test Serto.slnx --no-build /nr:false

if [ -d src/ControlPlane.Client/node_modules ]; then
  npm run lint --prefix src/ControlPlane.Client
  npm run build --prefix src/ControlPlane.Client
else
  echo "Skipping frontend validation because src/ControlPlane.Client/node_modules is missing."
  echo "Run npm ci --prefix src/ControlPlane.Client, then rerun scripts/validate.sh."
fi
