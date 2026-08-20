#!/usr/bin/env bash
# 検証フィクスチャへ CLI を実行し、既知の期待判定と照合する
set -euo pipefail

fixture_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$fixture_dir/../../../.." && pwd)"
output_dir="${1:-$fixture_dir/.mutation-output}"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"

dotnet "$cli" run \
  --project "$fixture_dir/Fixture.Target/Fixture.Target.csproj" \
  --test-project "$fixture_dir/Fixture.Target.Tests/Fixture.Target.Tests.csproj" \
  --output "$output_dir"

report="$output_dir/reports/mutation-report.json"
timings="$output_dir/reports/timings.json"

fail=0
check() {
  local label="$1" expected="$2" actual="$3"
  if [ "$expected" = "$actual" ]; then
    echo "ok   $label = $actual"
  else
    echo "FAIL $label expected=$expected actual=$actual"
    fail=1
  fi
}

check "mutants total"   17 "$(jq '.counters.mutants' "$timings")"
check "Killed"          13 "$(jq '.counters.byStatus.Killed // 0' "$timings")"
check "Survived"         1 "$(jq '.counters.byStatus.Survived // 0' "$timings")"
check "NoCoverage"       1 "$(jq '.counters.byStatus.NoCoverage // 0' "$timings")"
check "Timeout"          2 "$(jq '.counters.byStatus.Timeout // 0' "$timings")"
check "CompileError"     0 "$(jq '.counters.byStatus.CompileError // 0' "$timings")"

survived_line=$(jq -r '[.files[].mutants[] | select(.status == "Survived")][0].location.start.line' "$report")
check "Survived line (CoveredButUnasserted)" 11 "$survived_line"

nocover_line=$(jq -r '[.files[].mutants[] | select(.status == "NoCoverage")][0].location.start.line' "$report")
check "NoCoverage line (Untested)" 9 "$nocover_line"

timeout_line=$(jq -r '[.files[].mutants[] | select(.status == "Timeout")][0].location.start.line' "$report")
check "Timeout line (i += 1)" 20 "$timeout_line"

static_status=$(jq -r '[.files[] | .mutants[] | select(.static == true)][0].status' "$report")
check "static mutant status (Offset)" Killed "$static_status"

seed_status=$(jq -r '[.files[] | .mutants[] | select(.replacement == "basis / 2")][0].status' "$report")
check "static-init-only callee status (ComputeSeed)" Killed "$seed_status"

exit "$fail"
