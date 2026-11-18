#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../.." && pwd)"
fixture_dir="$repo_root/tests/e2e/medium/verification"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/src"
cp "$fixture_dir/Directory.Build.props" "$work/"
cp -r "$fixture_dir/Fixture.Target" "$fixture_dir/Fixture.Target.Tests" "$work/src/"
rm -rf "$work"/src/*/bin "$work"/src/*/obj

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

run_cli() {
  dotnet "$cli" run \
    --project "$work/src/Fixture.Target/Fixture.Target.csproj" \
    --test-project "$work/src/Fixture.Target.Tests/Fixture.Target.Tests.csproj" \
    "${baseline[@]}" --output "$work/out" "${@:2}" > "$work/$1.log" 2>&1 \
    || { echo "FAIL $1"; tail -20 "$work/$1.log"; exit 1; }
}

matched() { grep -c '前回の保存と生成入力が完全一致' "$work/$1.log" || true; }
none_inherited() { grep -c '前回の保存から継承できる判定はない' "$work/$1.log" || true; }
inherited() { { grep -oE '前回の保存から [0-9]+ 件の判定を継承' "$work/$1.log" || true; } | grep -oE '[0-9]+' | awk '{ s += $1 } END { print s + 0 }'; }

settled() {
  jq -r '[.files[].mutants[] | "\(.location.start.line):\(.location.start.column) \(.replacement) \(.status)"] | sort | .[]' \
    "$work/out/reports/mutation-report.json"
}

baseline=()
run_cli first
settled > "$work/first.settled"
check "最初の走行は再利用しない" 0 "$(matched first)"
check "最初の走行の変異の数" 17 "$(jq '.counters.mutants' "$work/out/reports/timings.json")"

baseline=(--with-baseline)
run_cli identical
check "継承を求めずに保存した同じ入力も全体を再利用する" 1 "$(matched identical)"
check "再利用した判定は最初の走行と同じ" "" "$(diff "$work/first.settled" <(settled) || true)"

# shellcheck disable=SC2016
sed -i 's|</PropertyGroup>|  <DefineConstants>$(DefineConstants);REUSE_PROBE</DefineConstants>\n  </PropertyGroup>|' "$work/Directory.Build.props"
run_cli configured
check "設定を変えた走行は全体を再利用しない" 0 "$(matched configured)"
check "設定を変えた走行は判定を継承しない" 1 "$(none_inherited configured)"
check "設定を変えた走行の継承の件数" 0 "$(inherited configured)"
check "設定を変えた走行の判定は最初の走行と同じ" "" "$(diff "$work/first.settled" <(settled) || true)"

run_cli configured-again
check "変えた後の同じ入力の保存は再び再利用される" 1 "$(matched configured-again)"

printf '\n// touched\n' >> "$work/src/Fixture.Target/Configured.cs"
run_cli other-file
check "他のファイルを変えた走行は全体を再利用しない" 0 "$(matched other-file)"
check "他のファイルを変えた走行は判定を継承しない" 1 "$(none_inherited other-file)"
check "他のファイルを変えた走行の継承の件数" 0 "$(inherited other-file)"

printf '\n// touched\n' >> "$work/src/Fixture.Target.Tests/CalculatorTests.cs"
run_cli tests
check "テストを変えた走行は全体を再利用しない" 0 "$(matched tests)"
check "テストを変えた走行は判定を継承しない" 1 "$(none_inherited tests)"

run_cli validating --validate-survivors
check "実行設定を変えた走行は全体を再利用しない" 0 "$(matched validating)"
check "実行設定を変えた走行は判定を継承しない" 1 "$(none_inherited validating)"

# 直前の実行設定を保ち、設定差だけで不一致になる誤検知を避けて環境差を検査する。
MUTATION_E2E_REUSE_ENV="${MUTATION_E2E_REUSE_ENV-}:changed" run_cli environment --validate-survivors
check "環境変数を変えた走行は全体を再利用しない" 0 "$(matched environment)"
check "環境変数を変えた走行は判定を継承しない" 1 "$(none_inherited environment)"
check "環境変数を変えた走行の継承の件数" 0 "$(inherited environment)"

exit "$fail"
