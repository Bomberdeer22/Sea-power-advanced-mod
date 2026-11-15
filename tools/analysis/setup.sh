#!/usr/bin/env bash
# Create a private virtual environment for the analysis tools and install their dependencies.
# Nothing is installed system-wide; delete tools/analysis/.venv to undo it.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$here"

python="${PYTHON:-python3}"
if ! command -v "$python" >/dev/null 2>&1; then
    echo "Python 3 not found. Install it, or set PYTHON to the executable." >&2
    exit 1
fi

if [ ! -x ".venv/bin/python" ]; then
    echo "Creating tools/analysis/.venv with $python ..."
    "$python" -m venv .venv
fi

.venv/bin/python -m pip install --quiet --upgrade pip
.venv/bin/python -m pip install --quiet -r requirements.txt

echo "Ready. Run:"
echo "  tools/analysis/.venv/bin/python tools/analysis/analyze.py selftest"
echo "  tools/analysis/.venv/bin/python tools/analysis/analyze.py brief"
