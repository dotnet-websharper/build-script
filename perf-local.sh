#!/usr/bin/env bash
# Local replica of the perf-benchmark.yml `measure` job: scaffold the benchmark solution and
# run the cold/warm phases against either locally built WebSharper packages or a published
# WebSharper.Templates version. Writes everything to a log file. Never pushes or commits.
#
# Usage:
#   ./perf-local.sh [options]
#
# Options:
#   --localnuget <path>     Folder of locally built WS .nupkg files (default: ../localnuget).
#   --ws-version <v>        Pin scaffolded projects' WebSharper* refs to this version. If omitted,
#                           it is detected from the WebSharper.<v>.nupkg in --localnuget.
#   --templates-version <v> PUBLISHED mode: install this WebSharper.Templates version from the
#                           dotnet-websharper GitHub feed and benchmark it (ignores --localnuget/
#                           --ws-version). Requires the GitHub NuGet feed to be reachable.
#   --reps <n>              Repetitions per phase (default: 1).
#   --out <dir>             Output dir for logs + results.jsonl (default: ./perf-out-local).
#   --work <dir>           Scaffold dir (default: <out>/work). Recreated each run.
#   --keep                 Keep the work dir after running (default: keep; pass --no-keep to remove).
#   --no-keep              Remove the work dir after running.
#   -h, --help             Show this help.
#
# Examples:
#   ./perf-local.sh                                  # local packages from ../localnuget, 1 rep
#   ./perf-local.sh --ws-version 10.1.0.663 --reps 3
#   ./perf-local.sh --templates-version 10.1.5.674   # benchmark a published release
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

localnuget="$SCRIPT_DIR/../localnuget"
ws_version=""
templates_version=""
reps="1"
out="$PWD/perf-out-local"
work=""
keep="1"

while [ $# -gt 0 ]; do
  case "$1" in
    --localnuget) localnuget="$2"; shift 2 ;;
    --ws-version) ws_version="$2"; shift 2 ;;
    --templates-version) templates_version="$2"; shift 2 ;;
    --reps) reps="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --work) work="$2"; shift 2 ;;
    --keep) keep="1"; shift ;;
    --no-keep) keep="0"; shift ;;
    -h|--help) sed -n '2,30p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

out="$(mkdir -p "$out" && cd "$out" && pwd)"
[ -n "$work" ] || work="$out/work"
log="$out/perf-local.log"

# Tee everything from here on to the log file.
exec > >(tee "$log") 2>&1

# Convert a path to a native form for file contents (nuget.config). MSYS/Git Bash auto-converts
# paths passed as ARGS to native exes, but not paths written into files, so a bare /h/... source
# is misread by Windows NuGet as C:\h\... — use cygpath where available.
to_native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

published=0
[ -n "$templates_version" ] && published=1

echo "=== perf-local: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
echo "mode:        $([ "$published" = 1 ] && echo published || echo local)"
echo "out:         $out"
echo "work:        $work"
echo "reps:        $reps"

if [ "$published" = 0 ]; then
  localnuget="$(cd "$localnuget" && pwd)"
  echo "localnuget:  $localnuget"
  if [ -z "$ws_version" ]; then
    pkg="$(ls "$localnuget"/WebSharper.[0-9]*.nupkg 2>/dev/null | head -n 1 || true)"
    [ -n "$pkg" ] || { echo "ERROR: no WebSharper.<version>.nupkg in $localnuget; pass --ws-version or --templates-version." >&2; exit 1; }
    ws_version="$(basename "$pkg" | sed -E 's/^WebSharper\.([0-9][0-9A-Za-z.+-]*)\.nupkg$/\1/')"
  fi
  echo "ws-version:  $ws_version"
else
  echo "templates:   $templates_version (published GitHub feed)"
fi

# Fresh work dir + a local nuget.config that NuGet picks up by walking up from work/Perf/*.
rm -rf "$work"
mkdir -p "$work"
if [ "$published" = 1 ]; then
  cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear />
    <add key="github" value="https://nuget.pkg.github.com/dotnet-websharper/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
  echo "--- installing published templates ---"
  dotnet new install "WebSharper.Templates::$templates_version" --force
else
  cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear />
    <add key="localperf" value="$(to_native "$localnuget")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
  echo "--- using already-installed templates (versions will be rewritten to $ws_version) ---"
fi

echo "--- scaffolding ---"
( cd "$work" && bash "$SCRIPT_DIR/perf-scaffold.sh" Perf )

# Portable in-place edit (works with GNU and BSD sed).
sed_inplace() { local tmp; tmp="$(mktemp)"; sed -E "$1" "$2" > "$tmp" && mv "$tmp" "$2"; }

if [ "$published" = 0 ]; then
  echo "--- pinning WebSharper* package versions to $ws_version ---"
  while IFS= read -r proj; do
    sed_inplace 's#(<PackageReference Include="WebSharper[^"]*" Version=")[^"]*(")#\1'"$ws_version"'\2#g' "$proj"
  done < <(find "$work/Perf" -name '*.fsproj')
fi

echo "--- running benchmark (full output in $log) ---"
dotnet fsi "$SCRIPT_DIR/perf-run.fsx" -- \
  --root "$work/Perf" \
  --out "$out" \
  --reps "$reps" \
  --os "local-$(uname -s 2>/dev/null || echo unknown)" \
  --which branch \
  --sha "$([ "$published" = 1 ] && echo "$templates_version" || echo "$ws_version")" \
  --branch "$([ "$published" = 1 ] && echo published || echo local)" \
  --dotnet-version "" \
  --run-id "local-$(date -u +%Y%m%d%H%M%S)"

echo "--- done ---"
echo "results: $out/results.jsonl"
echo "logs:    $out/logs/"
echo "summary table:"
dotnet fsi "$SCRIPT_DIR/aggregate.fsx" -- --input "$out/results.jsonl" || true

if [ "$keep" = 0 ]; then rm -rf "$work"; echo "(removed work dir)"; else echo "work kept: $work"; fi
