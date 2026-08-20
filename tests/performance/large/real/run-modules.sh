#!/usr/bin/env bash
# 実プロジェクトの module 群へ本ツールを順に流し、module ごとの実測をまとめる
# usage: run-modules.sh RESULTS_DIR "<project> <test-project>"...
# 追加の CLI 引数(--mutate など)は MUTATION_EXTRA_ARGS で渡す
set -uo pipefail

results="$1"; shift
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"
mkdir -p "$results"
summary="$results/summary.tsv"
printf 'module\tstatus\twall_ms\tmutants\tkilled\tsurvived\tnocov\ttimeout\tcompile_err\tscore\tbuild_ms\tmutate_ms\tcompile_ms\tbaseline_ms\ttesting_ms\ttests\n' > "$summary"

for pair in "$@"; do
  read -r project testproject <<< "$pair"
  name="$(basename "$project" .csproj)"
  out="$results/$name"
  start=$(date +%s%3N)
  # shellcheck disable=SC2086
  if dotnet "$cli" run --project "$project" --test-project "$testproject" --output "$out" ${MUTATION_EXTRA_ARGS:-} > "$results/$name.log" 2>&1; then
    status=ok
  else
    status=failed
  fi
  wall=$(( $(date +%s%3N) - start ))
  t="$out/reports/timings.json"
  if [ -f "$t" ]; then
    jq -r --arg m "$name" --arg s "$status" --arg w "$wall" '
      [$m, $s, $w, .counters.mutants,
       (.counters.byStatus.Killed // 0), (.counters.byStatus.Survived // 0),
       (.counters.byStatus.NoCoverage // 0), (.counters.byStatus.Timeout // 0),
       (.counters.byStatus.CompileError // 0),
       ((.counters.score // 0) * 100 | round),
       (.phases.buildMs|round), (.phases.mutateMs|round), (.phases.compileMs|round),
       (.phases.baselineMs|round), (.phases.testingMs|round), .counters.tests] | @tsv' "$t" >> "$summary"
  else
    printf '%s\t%s\t%s\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\t-\n' "$name" "$status" "$wall" >> "$summary"
  fi
  tail -1 "$summary"
done
column -t -s $'\t' "$summary"
