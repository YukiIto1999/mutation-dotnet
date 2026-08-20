#!/usr/bin/env bash
# 合成ターゲットへ本ツールと Stryker.NET を実行し、実測を比較する
set -euo pipefail

modules=40
concurrency="$(( $(nproc) / 2 ))"
run_stryker=1
while [ $# -gt 0 ]; do
  case "$1" in
    --modules) modules="$2"; shift 2 ;;
    --concurrency) concurrency="$2"; shift 2 ;;
    --skip-stryker) run_stryker=0; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

bench_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$bench_dir/../../.." && pwd)"
work="$bench_dir/work"
target="$work/target"
results="$work/results"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"

rm -rf "$target" "$results"
mkdir -p "$results"
python3 "$bench_dir/generate_target.py" "$target" --modules "$modules"

now_ms() { date +%s%3N; }

clean_target() {
  find "$target" -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
  rm -rf "$target/Bench.Target.Tests/StrykerOutput"
}

echo "== mutation-dotnet (concurrency $concurrency) =="
clean_target
start=$(now_ms)
dotnet "$cli" run \
  --project "$target/Bench.Target/Bench.Target.csproj" \
  --test-project "$target/Bench.Target.Tests/Bench.Target.Tests.csproj" \
  --output "$results/ours-c$concurrency" \
  --concurrency "$concurrency"
ours_ms=$(( $(now_ms) - start ))
echo "ours (c=$concurrency) wall: ${ours_ms}ms"

echo "== mutation-dotnet (concurrency 1) =="
clean_target
start=$(now_ms)
dotnet "$cli" run \
  --project "$target/Bench.Target/Bench.Target.csproj" \
  --test-project "$target/Bench.Target.Tests/Bench.Target.Tests.csproj" \
  --output "$results/ours-c1" \
  --concurrency 1
ours_c1_ms=$(( $(now_ms) - start ))
echo "ours (c=1) wall: ${ours_c1_ms}ms"

stryker_ms=0
if [ "$run_stryker" = 1 ]; then
  echo "== Stryker.NET (concurrency $concurrency) =="
  (cd "$repo_root" && dotnet tool restore >/dev/null)
  clean_target
  start=$(now_ms)
  (
    cd "$target/Bench.Target.Tests"
    dotnet stryker --project Bench.Target.csproj --concurrency "$concurrency" \
      --reporter json --skip-version-check --output "$results/stryker-c$concurrency"
  )
  stryker_ms=$(( $(now_ms) - start ))
  echo "stryker (c=$concurrency) wall: ${stryker_ms}ms"
fi

summary="$results/summary.md"
{
  echo "# ベンチマーク結果 (modules=$modules, concurrency=$concurrency)"
  echo
  echo "| 実行 | wall time |"
  echo "|---|---|"
  echo "| mutation-dotnet c=$concurrency | ${ours_ms}ms |"
  echo "| mutation-dotnet c=1 | ${ours_c1_ms}ms |"
  if [ "$run_stryker" = 1 ]; then
    echo "| Stryker.NET c=$concurrency | ${stryker_ms}ms |"
  fi
  echo
  echo "## mutation-dotnet 内訳 (c=$concurrency)"
  echo '```json'
  jq '{phases, counters}' "$results/ours-c$concurrency/reports/timings.json"
  echo '```'
  if [ "$run_stryker" = 1 ]; then
    echo
    echo "## Stryker.NET 判定内訳"
    echo '```json'
    stryker_report=$(find "$results/stryker-c$concurrency" -name mutation-report.json | head -1)
    jq '[.files[].mutants[].status] | group_by(.) | map({(.[0]): length}) | add' "$stryker_report"
    echo '```'
  fi
} > "$summary"
echo
cat "$summary"
