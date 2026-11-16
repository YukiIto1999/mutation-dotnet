{ pkgs, ... }:

{
  packages = [
    pkgs.dotnet-sdk_10
    pkgs.just
    pkgs.git
    pkgs.jq
  ];

  env = {
    DOTNET_CLI_TELEMETRY_OPTOUT = "1";
    DOTNET_NOLOGO = "1";
  };

  # T1(予算 2 分): analyzer 込みの build と Small test
  scripts.verify.exec = ''
    set -euo pipefail

    dotnet build Mutation.slnx
    dotnet tests/Mutation.Tests/bin/Debug/net10.0/Mutation.Tests.dll --no-ansi --disable-logo --no-progress
  '';

  # T2(予算 15 分): T1 と Medium の e2e
  scripts.verify-push.exec = ''
    set -euo pipefail

    verify
    bash tests/e2e/medium/verification/verify.sh
    bash tests/e2e/medium/frameworks/verify-frameworks.sh
    bash tests/e2e/medium/changed-lines/verify-changed-lines.sh
    bash tests/e2e/medium/detection-matrix/verify-detection-matrix.sh
  '';

  # T3 の全量回帰(予算なし、gate にしない): T1 と T2 の製品検査を、影響範囲で絞らず全て実行する。
  # mutation 分析とは別の走行で、終了 status と失敗した段を .mutation-output/regression-full へ残してから終える。失敗した段は work/backlog.md へ記録する
  scripts.regression-full.exec = ''
    set -euo pipefail

    out=.mutation-output/regression-full
    mkdir -p "$out" work
    heading="## $(date +%F) regression-full $(git rev-parse --short HEAD)"
    previous=$(cat "$out/failed-stages" 2>/dev/null || true)
    rm -f "$out/exit-status" "$out/failed-stages"
    status=0

    # 失敗しても後続の段を実行し、全量の失敗を揃える。前回と同じ段の失敗は記録し直さない
    stage() {
      local name="$1"
      shift
      if "$@"; then
        return 0
      fi
      status=1
      printf '%s\n' "$name" >> "$out/failed-stages"
      if ! grep -qxF -- "$name" <<< "$previous"; then
        printf '\n%s\n\n- [ ] %s が失敗した\n' "$heading" "$name" >> work/backlog.md
      fi
      echo "regression-full: $name が失敗した" >&2
    }

    stage "T1 の verify" verify
    stage "e2e の verification" bash tests/e2e/medium/verification/verify.sh
    stage "e2e の frameworks" bash tests/e2e/medium/frameworks/verify-frameworks.sh
    stage "e2e の changed-lines" bash tests/e2e/medium/changed-lines/verify-changed-lines.sh
    stage "e2e の detection-matrix" bash tests/e2e/medium/detection-matrix/verify-detection-matrix.sh

    touch "$out/failed-stages"
    printf '%s\n' "$status" > "$out/exit-status"
    echo "regression-full: 終了 status $status($out)"
    exit "$status"
  '';

  # T3 の mutation 分析(予算なし、gate にしない): 全量の mutation。全量回帰の結果を変えない別の走行で、生の終了 status を .mutation-output/full/exit-status へ残す。
  # 前回の分析から増えた失敗と未検出の変異を work/backlog.md へ記録する。出力先は前回の undetected.json を引き継ぐため従来のまま
  scripts.mutation-full.exec = ''
    set -euo pipefail

    out=.mutation-output/full
    cli=src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll
    mkdir -p "$out" work
    heading="## $(date +%F) mutation-full $(git rev-parse --short HEAD)"
    rm -f "$out/exit-status"

    # 前回の分析と同じ段の失敗は記録し直さない。第 2 引数は終了 status
    failed() {
      if [ "$(cat "$out/failed-stage" 2>/dev/null)" != "$1" ]; then
        printf '\n%s\n\n- [ ] %s が失敗した\n' "$heading" "$1" >> work/backlog.md
      fi
      printf '%s' "$1" > "$out/failed-stage"
      echo "mutation-full: $1 が失敗した" >&2
      exit "$2"
    }

    # 単独で実行しても engine の CLI と test project の成果物がそろうよう、build を先に通す
    dotnet build Mutation.slnx || failed "分析の前の build" 1
    status=0
    dotnet "$cli" run \
      --project src/Mutation/Mutation.csproj \
      --test-project tests/Mutation.Tests/Mutation.Tests.csproj \
      --concurrency 2 --output "$out" || status=$?
    printf '%s\n' "$status" > "$out/exit-status"
    echo "mutation-full: 全量の mutation の終了 status $status"
    [ "$status" -eq 0 ] || failed "全量の mutation" "$status"
    mutants=$(jq '.counters.mutants' "$out/reports/timings.json") || failed "全量の mutation の報告の読み取り" 1
    [ "$mutants" -gt 0 ] || failed "全量の mutation(変異の生成が 0 件。対象の指定を確かめる)" 1
    rm -f "$out/failed-stage"

    # 未検出の変異は、ファイル・演算子・置換・変異前の行の文面で前回と突き合わせ、行番号のずれを新規と数えない
    jq '[.files | to_entries[] | .key as $file | (.value.source | split("\n")) as $lines
        | .value.mutants[] | select(.status == "Survived" or .status == "NoCoverage")
        | { key: ([$file, .mutatorName, .replacement, (($lines[.location.start.line - 1] // "") | gsub("^\\s+|\\s+$"; ""))] | join("\t")),
            line: "\($file):\(.location.start.line) \(.status) \(.mutatorName): \(.replacement)" }]' \
      "$out/reports/mutation-report.json" > "$out/undetected.next.json"
    [ -f "$out/undetected.json" ] || echo '[]' > "$out/undetected.json"
    jq -r --slurpfile previous "$out/undetected.json" \
      '(reduce $previous[0][].key as $k ({}; .[$k] = true)) as $known | .[] | select($known[.key] | not) | .line' \
      "$out/undetected.next.json" > "$out/delta.txt"
    mv "$out/undetected.next.json" "$out/undetected.json"
    if [ -s "$out/delta.txt" ]; then
      { printf '\n%s\n\n' "$heading"; sed 's/^/- [ ] /' "$out/delta.txt"; } >> work/backlog.md
    fi
    echo "mutation-full: 変異 $mutants 件、前回の分析から増えた未検出 $(wc -l < "$out/delta.txt") 件(work/backlog.md)"
  '';

  # T3(予算なし、gate にしない): 全量回帰と mutation 分析を別の走行で順に実行し、終了 status は全量回帰のものを返す。
  # 分析の失敗や未完了は、先に確定した全量回帰の結果を変えない。-e は付けず、全量回帰が失敗しても分析まで進める
  scripts.verify-full.exec = ''
    set -uo pipefail

    regression=0
    regression-full || regression=$?
    analysis=0
    mutation-full || analysis=$?
    echo "verify-full: 全量回帰の終了 status $regression / mutation 分析の終了 status $analysis (終了 status は全量回帰のもの)"
    exit "$regression"
  '';
}
