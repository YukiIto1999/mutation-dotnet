#!/usr/bin/env bash
# 一件の計測: 対象を clean にし build server を止めてから、cgroup 計測下で一回実行する
# usage: run-one.sh TOOL(ours|stryker) NAME WORKDIR PROJECT TESTPROJECT CONCURRENCY [--keep] [--mem MB] [--cpus LIST] -- [extra args]
# env: OUT_ROOT(計測結果の置き場)、BENCH_OUTDIR(ツール出力を固定する増分シナリオ用。OUTDIR は MSBuild の OutDir と衝突するため使わない)、MUTATION_CLI、STRYKER_CLI
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/../../../.." && pwd)"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
tool="$1"; name="$2"; workdir="$3"; project="$4"; testproject="$5"; conc="$6"; shift 6
keep=0; mem=""; cpus=""
while [ $# -gt 0 ] && [ "$1" != "--" ]; do
  case "$1" in
    --keep) keep=1; shift ;;
    --mem) mem="--mem-limit-mb $2"; shift 2 ;;
    --cpus) cpus="--cpus $2"; shift 2 ;;
    *) echo "unknown $1" >&2; exit 2 ;;
  esac
done
[ "${1:-}" = "--" ] && shift
root="${OUT_ROOT:-$here/../work/out}"
mkdir -p "$root/runs"
out="${BENCH_OUTDIR:-$root/runs/$name}"
meta="$root/runs/$name"
if [ "$keep" = 0 ]; then
  [ -z "${BENCH_OUTDIR:-}" ] && rm -rf "$out"
  find "$workdir" -type d \( -name bin -o -name obj -o -name StrykerOutput \) -prune -exec rm -rf {} + 2>/dev/null
fi
dotnet build-server shutdown >/dev/null 2>&1
cli="${MUTATION_CLI:-$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll}"
if [ "$tool" = ours ]; then
  python3 "$here/measure.py" --out "$meta.json" $mem $cpus --cwd "$workdir" -- \
    dotnet "$cli" run --project "$project" --test-project "$testproject" --output "$out" --concurrency "$conc" "$@"
else
  stryker=(dotnet tool run --allow-roll-forward dotnet-stryker)
  [ -n "${STRYKER_CLI:-}" ] && stryker=(dotnet "$STRYKER_CLI")
  python3 "$here/measure.py" --out "$meta.json" $mem $cpus --cwd "$(dirname "$workdir/$testproject")" -- \
    "${stryker[@]}" --project "$(basename "$project")" --concurrency "$conc" --reporter json --reporter progress \
    --skip-version-check --output "$out" "$@"
fi
