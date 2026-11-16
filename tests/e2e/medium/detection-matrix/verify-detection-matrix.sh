#!/usr/bin/env bash
# --detection-matrix の run での既定の run と同じ判定と killedBy への全失敗の記録の 3 種のホストでの照合
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../.." && pwd)"
output_root="${1:-$script_dir/.mutation-output}"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"
verification="$repo_root/tests/e2e/medium/verification"
frameworks="$repo_root/tests/e2e/medium/frameworks"
mkdir -p "$output_root"

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

# 行と置換で選んだ変異の killedBy の基盤ごとの修飾を除いた表示名の並べ替えた連結
killers() {
  jq -r --argjson line "$2" --arg replacement "$3" \
    '[.files[].mutants[] | select(.location.start.line == $line and .replacement == $replacement)]
     | if length == 1 then .[0].killedBy | map(split(".") | last) | sort | join(",") else "変異が \(length) 件" end' "$1"
}

# 実行した変異のうち計画したテストの結果がそろわないものの件数
incomplete() {
  jq '[.files[].mutants[] | select(.status == "Killed" or .status == "Survived")
       | select((.coveredBy | length) == 0 or .testsCompleted != (.coveredBy | length) or has("statusReason"))] | length' "$1"
}

run_matrix() {
  local name="$1" project="$2" test_project="$3"
  dotnet "$cli" run --project "$project" --test-project "$test_project" \
    --detection-matrix --concurrency 2 --output "$output_root/$name" > "$output_root/$name.log" 2>&1 \
    || { echo "FAIL $name run"; tail -5 "$output_root/$name.log"; fail=1; return 1; }
}

# xunit v2 での既定の run と同じ判定と 3 件のテストが検出する変異と 1 件だけが検出する変異
if run_matrix Xunit2 "$verification/Fixture.Target/Fixture.Target.csproj" "$verification/Fixture.Target.Tests/Fixture.Target.Tests.csproj"; then
  report="$output_root/Xunit2/reports/mutation-report.json"
  timings="$output_root/Xunit2/reports/timings.json"
  check "Xunit2 mutants total" 17 "$(jq '.counters.mutants' "$timings")"
  check "Xunit2 Killed" 13 "$(jq '.counters.byStatus.Killed // 0' "$timings")"
  check "Xunit2 Survived" 1 "$(jq '.counters.byStatus.Survived // 0' "$timings")"
  check "Xunit2 NoCoverage" 1 "$(jq '.counters.byStatus.NoCoverage // 0' "$timings")"
  check "Xunit2 Timeout" 2 "$(jq '.counters.byStatus.Timeout // 0' "$timings")"
  check "Xunit2 killedBy (value <= 0)" "IsPositive_false_for_negative,IsPositive_false_for_zero,IsPositive_true_for_positive" "$(killers "$report" 7 "value <= 0")"
  check "Xunit2 killedBy (value >= 0)" "IsPositive_false_for_zero" "$(killers "$report" 7 "value >= 0")"
  check "Xunit2 incomplete rows" 0 "$(incomplete "$report")"
fi

# VSTest(NUnit)と Microsoft.Testing.Platform(TUnit): 2 件のテストが検出する加算の変異と 1 件だけが検出する比較の変異
for framework in Nunit Tunit; do
  if run_matrix "$framework" "$frameworks/Frameworks.Target/Frameworks.Target.csproj" "$frameworks/Frameworks.$framework.Tests/Frameworks.$framework.Tests.csproj"; then
    report="$output_root/$framework/reports/mutation-report.json"
    check "$framework killedBy (a - b)" "Add_commutes,Add_sums" "$(killers "$report" 7 "a - b")"
    check "$framework killedBy (value >= 10)" "Above_boundary" "$(killers "$report" 10 "value >= 10")"
    check "$framework incomplete rows" 0 "$(incomplete "$report")"
  fi
done

exit "$fail"
