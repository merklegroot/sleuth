#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root/SleuthRay"

# In some non-interactive environments, window creation can hang.
# Run with a timeout so the script never blocks indefinitely.
python3 - <<'PY'
import subprocess

cmd = [
    "dotnet", "run",
    "--project", "SleuthRay.csproj",
    "--",
    "--screenshot",
    "--screenshot-path=../screenshots/sleuthray.png",
]

subprocess.run(cmd, check=True, timeout=30)
PY

echo "Updated: $repo_root/screenshots/sleuthray.png"
