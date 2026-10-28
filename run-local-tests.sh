#!/usr/bin/env bash
# Run the local assembly metadata audit for the current RimWorld 1.7 build.
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ "${1:-}" == --help || "${1:-}" == -h ]]; then
  echo "Usage: $0 [built-mod-root]"
  echo "Requires Docker and Python 3. No game installation is needed for the metadata audit."
  exit 0
fi
if [[ $# -gt 1 ]]; then
  echo "Usage: $0 [built-mod-root]" >&2
  exit 2
fi
bash "$ROOT/Source/RuntimeTests/audit-rimworld-1.7-reference.sh" "${1:-$ROOT}"
