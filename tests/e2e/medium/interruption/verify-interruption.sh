#!/usr/bin/env bash
# 走行の途中に SIGTERM と SIGKILL を送り、判定を確定した変異が残り、走行が途中として記録され、同じ入口の続きの実行が残りだけを実行して全件の判定を確定することを照合する
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../.." && pwd)"
medium="$repo_root/tests/e2e/medium"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"

work="$(mktemp -d)"
background=""
cleanup() {
  [ -z "$background" ] || kill -KILL "$background" 2>/dev/null || true
  pkill -KILL -f "Mutation.Worker.dll.*$work" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

# 判定に 0.4 秒かかる 6 件のテストを持つ対象と、速く終わる検証フィクスチャの対象を、一つの走行で順に検査する
cp "$medium/interruption/Directory.Build.props" "$work/"
cp -r "$medium/interruption/Pauses.Target" "$medium/interruption/Pauses.Target.Tests" \
  "$medium/verification/Fixture.Target" "$medium/verification/Fixture.Target.Tests" "$work/"
rm -rf "$work"/*/bin "$work"/*/obj
projects="$work/Pauses.Target/Pauses.Target.csproj,$work/Fixture.Target/Fixture.Target.csproj"
tests="$work/Pauses.Target.Tests/Pauses.Target.Tests.csproj,$work/Fixture.Target.Tests/Fixture.Target.Tests.csproj"

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
  dotnet "$cli" run --project "$projects" --test-project "$tests" --concurrency 1 "$@"
}

# 走行を background で始める。signal が dotnet の process へ直接届くよう、subshell を exec で dotnet に置き換える
start_cli() {
  local log="$1"
  shift
  ( exec dotnet "$cli" run --project "$projects" --test-project "$tests" --concurrency 1 "$@" > "$log" 2>&1 ) &
  background=$!
}

# 時間のかかる対象の変異ごとの判定。位置と置換と status を並べ替えた一行ずつ。Pending は含めない
settled() {
  jq -r '[.files | to_entries[] | select(.key | startswith("Pauses.Target/")) | .value.mutants[]
          | select(.status != "Pending")
          | "\(.location.start.line):\(.location.start.column) \(.replacement) \(.status)"] | sort | .[]' "$1"
}

# 走行の log の「共有ホスト N 件 / 隔離 process M 件」の和と、「前回の保存から K 件の判定を継承」の和
executed_total() { { grep -oE '共有ホスト [0-9]+ 件 / 隔離 process [0-9]+ 件' "$1" || true; } | grep -oE '[0-9]+' | awk '{ s += $1 } END { print s + 0 }'; }
inherited_total() { { grep -oE '前回の保存から [0-9]+ 件の判定を継承' "$1" || true; } | grep -oE '[0-9]+' | awk '{ s += $1 } END { print s + 0 }'; }

# 時間のかかる対象の追記の記録が、一行目の保存と三件の判定の行を持つまで待つ
wait_for_three_verdicts() {
  local journal="$1" pid="$2" waited=0
  while [ "$waited" -lt 480 ]; do
    if [ -f "$journal" ] && [ "$(wc -l < "$journal")" -ge 4 ]; then
      return 0
    fi
    kill -0 "$pid" 2>/dev/null || { echo "FAIL 三件の判定の前に走行が終わった"; return 1; }
    sleep 0.25
    waited=$((waited + 1))
  done
  echo "FAIL 三件の判定の行を待ちきれなかった"
  return 1
}

# 走行が残した worker の process が消えるまで待った後に残る件数
leftover_workers() {
  local waited=0
  while pgrep -f "Mutation.Worker.dll.*$work" > /dev/null && [ "$waited" -lt 60 ]; do
    sleep 0.25
    waited=$((waited + 1))
  done
  pgrep -fc "Mutation.Worker.dll.*$work" || true
}

# 参照: 途中で止めない走行
run_cli --output "$work/full" > "$work/full.log" 2>&1 || { echo "FAIL 参照の走行"; tail -20 "$work/full.log"; exit 1; }
full_report="$work/full/reports/mutation-report.json"
settled "$full_report" > "$work/full.settled"
check "参照の走行の状態" completed "$(jq -r '.run.state' "$full_report")"
check "参照の走行の Pending" 0 "$(jq '[.files[].mutants[] | select(.status == "Pending")] | length' "$full_report")"
total=$(wc -l < "$work/full.settled")
echo "     時間のかかる対象の変異 $total 件"
[ "$total" -ge 8 ] || { echo "FAIL 時間のかかる対象の変異が少なすぎる"; fail=1; }

# SIGTERM: 三件の判定を確定した後に送る。二つ目の対象には取りかからない
start_cli "$work/term.log" --output "$work/term"
wait_for_three_verdicts "$work/term/work/Pauses.Target/snapshot.verdicts.jsonl" "$background" || exit 1
kill -TERM "$background"
status=0
wait "$background" || status=$?
background=""
check "SIGTERM の終了コード" 143 "$status"

term_report="$work/term/reports/mutation-report.json"
term_timings="$work/term/reports/timings.json"
check "報告が残る" true "$([ -f "$term_report" ] && echo true || echo false)"
check "報告の走行の状態" interrupted "$(jq -r '.run.state' "$term_report")"
check "報告の complete" false "$(jq '.run.complete' "$term_report")"
check "timings の走行の状態" interrupted "$(jq -r '.run.state' "$term_timings")"
check "取りかからなかった対象" Fixture.Target "$(jq -r '.run.interruptedTargets | join(",")' "$term_report")"
settled "$term_report" > "$work/term.settled"
kept=$(wc -l < "$work/term.settled")
pending=$(jq '[.files[].mutants[] | select(.status == "Pending")] | length' "$term_report")
[ "$kept" -ge 3 ] && echo "ok   判定を確定した変異が報告に残る = $kept" || { echo "FAIL 判定を確定した変異が $kept 件しか残らない"; fail=1; }
[ "$pending" -ge 1 ] && echo "ok   判定を確定できなかった変異は Pending = $pending" || { echo "FAIL Pending が無い"; fail=1; }
check "Pending の件数が run と一致" "$pending" "$(jq '.run.unjudged' "$term_report")"
check "止めた時点で確定した判定は、止めない走行の判定と同じ" "" "$(LC_ALL=C comm -23 "$work/term.settled" "$work/full.settled")"
check "snapshot が残る" true "$([ -f "$work/term/work/Pauses.Target/snapshot.json" ] && echo true || echo false)"
check "追記の記録は snapshot に畳まれて消える" false "$([ -f "$work/term/work/Pauses.Target/snapshot.verdicts.jsonl" ] && echo true || echo false)"
check "SIGTERM の後に残る worker" 0 "$(leftover_workers)"

# 続き: 同じ command に --with-baseline を足すと、確定済みの判定を継承して残りだけを実行する
run_cli --output "$work/term" --with-baseline > "$work/term-resume.log" 2>&1 || { echo "FAIL SIGTERM の続きの走行"; tail -20 "$work/term-resume.log"; exit 1; }
resume_report="$work/term/reports/mutation-report.json"
check "続きの走行の状態" completed "$(jq -r '.run.state' "$resume_report")"
check "続きの走行の判定は、止めない走行の判定と同じ" "" "$(diff <(settled "$resume_report") "$work/full.settled" || true)"
check "続きの走行が継承した判定の件数" "$kept" "$(inherited_total "$work/term-resume.log")"
check "続きの走行が実行した変異は、残りだけ" "$(( $(executed_total "$work/full.log") - kept ))" "$(executed_total "$work/term-resume.log")"

# SIGKILL: 報告は書けないが、追記の記録が判定を残し、続きの走行が使う
start_cli "$work/kill.log" --output "$work/kill"
wait_for_three_verdicts "$work/kill/work/Pauses.Target/snapshot.verdicts.jsonl" "$background" || exit 1
kill -KILL "$background"
wait "$background" 2>/dev/null || true
background=""
check "SIGKILL の後に報告は無い" false "$([ -f "$work/kill/reports/mutation-report.json" ] && echo true || echo false)"
kept_by_journal=$(( $(wc -l < "$work/kill/work/Pauses.Target/snapshot.verdicts.jsonl") - 1 ))
[ "$kept_by_journal" -ge 3 ] && echo "ok   追記の記録が判定を残す = $kept_by_journal" || { echo "FAIL 追記の記録の判定が $kept_by_journal 件しか無い"; fail=1; }
check "SIGKILL の後に残る worker" 0 "$(leftover_workers)"
run_cli --output "$work/kill" --with-baseline > "$work/kill-resume.log" 2>&1 || { echo "FAIL SIGKILL の続きの走行"; tail -20 "$work/kill-resume.log"; exit 1; }
kill_report="$work/kill/reports/mutation-report.json"
check "SIGKILL の続きの走行の状態" completed "$(jq -r '.run.state' "$kill_report")"
check "SIGKILL の続きの走行の判定は、止めない走行の判定と同じ" "" "$(diff <(settled "$kill_report") "$work/full.settled" || true)"
check "SIGKILL の続きの走行が継承した判定の件数" "$kept_by_journal" "$(inherited_total "$work/kill-resume.log")"

exit "$fail"
