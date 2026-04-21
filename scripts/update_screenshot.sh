#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root/SleuthRay"

# Use `timeout` if available so this never hangs forever in CI.
if command -v timeout >/dev/null 2>&1; then
  timeout 30s dotnet run --project SleuthRay.csproj -- --screenshot --screenshot-path=../screenshots/sleuthray.png
else
  dotnet run --project SleuthRay.csproj -- --screenshot --screenshot-path=../screenshots/sleuthray.png
fi

echo "Updated: $repo_root/screenshots/sleuthray.png"
