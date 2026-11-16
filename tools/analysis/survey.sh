#!/usr/bin/env bash
# Writes analysis-report.txt: what the installed Sea Power build can do.
# Reads metadata and IL only; nothing is loaded or run.
#
#   ./survey.sh                      survey the default Steam path
#   ./survey.sh /path/to/Seapower-Scripts.dll
#   ./survey.sh --selftest           check the reader against a known assembly
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$here"

if [ ! -x ".venv/bin/python" ]; then
    ./setup.sh
fi

case "${1:-}" in
    --selftest|selftest)
        exec .venv/bin/python analyze.py selftest
        ;;
    --readiness|readiness)
        exec .venv/bin/python analyze.py readiness
        ;;
    "")
        exec .venv/bin/python analyze.py brief
        ;;
    *)
        exec .venv/bin/python analyze.py --dll "$1" brief
        ;;
esac
