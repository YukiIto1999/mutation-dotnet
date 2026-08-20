#!/usr/bin/env bash
# 同一 target を 3 つのテスト基盤で検査し、判定が一致することを確かめる
set -euo pipefail

fixture_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$fixture_dir/../../../.." && pwd)"
output_root="${1:-$fixture_dir/.mutation-output}"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"

declare -A counts
fail=0
for framework in Nunit Mstest Xunit; do
  out="$output_root/$framework"
  dotnet "$cli" run \
    --project "$fixture_dir/Frameworks.Target/Frameworks.Target.csproj" \
    --test-project "$fixture_dir/Frameworks.$framework.Tests/Frameworks.$framework.Tests.csproj" \
    --output "$out" > "$output_root/$framework.log" 2>&1 || { echo "FAIL $framework run"; tail -5 "$output_root/$framework.log"; fail=1; continue; }
  counts[$framework]=$(jq -c '{m:.counters.mutants,k:(.counters.byStatus.Killed//0),s:(.counters.byStatus.Survived//0),n:(.counters.byStatus.NoCoverage//0),c:(.counters.byStatus.CompileError//0)}' "$out/reports/timings.json")
  echo "$framework: ${counts[$framework]}"
done

[ "$fail" = 0 ] || exit 1
[ "${counts[Nunit]}" = "${counts[Xunit]}" ] || { echo "FAIL NUnit と xunit の判定が一致しない"; fail=1; }
[ "${counts[Mstest]}" = "${counts[Xunit]}" ] || { echo "FAIL MSTest と xunit の判定が一致しない"; fail=1; }
killed=$(jq '.counters.byStatus.Killed // 0' "$output_root/Xunit/reports/timings.json")
compile_err=$(jq '.counters.byStatus.CompileError // 0' "$output_root/Xunit/reports/timings.json")
nocov=$(jq '.counters.byStatus.NoCoverage // 0' "$output_root/Xunit/reports/timings.json")
[ "$killed" -gt 0 ] || { echo "FAIL Killed が 0"; fail=1; }
[ "$nocov" -gt 0 ] || { echo "FAIL NoCoverage が 0"; fail=1; }
[ "$compile_err" = 0 ] || { echo "FAIL CompileError が $compile_err"; fail=1; }
[ "$fail" = 0 ] && echo "ok   3 基盤の判定が一致"
exit "$fail"
