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
    bash tests/e2e/medium/interruption/verify-interruption.sh
  '';

  # T3 の全量回帰(gate にしない): T1 と T2 の製品検査を、影響範囲で絞らず全て実行する。
  # mutation 分析とは上限・取消・終了 status を共有しない別の走行で、上限は tiergate.json の regression-full.seconds。
  # 終了 status と失敗した段を .mutation-output/regression-full へ残してから終える。失敗した段は work/backlog.md へ記録する
  scripts.regression-full.exec = ''
    set -euo pipefail

    out=.mutation-output/regression-full
    mkdir -p "$out" work
    limit=$(jq -er '."regression-full".seconds' tiergate.json)
    deadline=$((SECONDS + limit))
    heading="## $(date +%F) regression-full $(git rev-parse --short HEAD)"
    previous=$(cat "$out/failed-stages" 2>/dev/null || true)
    rm -f "$out/exit-status" "$out/failed-stages"
    status=0
    stopped=0

    # 失敗しても後続の段を実行し、全量の失敗を揃える。前回と同じ段の失敗は記録し直さない。
    # 上限で止まった段と、上限を使い切って実行しなかった段は、失敗と分けて未完了として残す
    stage() {
      local name="$1" left=$((deadline - SECONDS)) code=0
      shift
      if [ "$left" -le 0 ]; then
        stopped=1
        printf '%s(上限 %s 秒を使い切り未実行)\n' "$name" "$limit" >> "$out/failed-stages"
        echo "regression-full: $name は上限 $limit 秒を使い切ったため未実行" >&2
        return 0
      fi
      timeout --kill-after=30 "$left" "$@" || code=$?
      case $code in
        0) return 0 ;;
        124 | 137)
          stopped=1
          printf '%s(上限 %s 秒で停止、status %s)\n' "$name" "$limit" "$code" >> "$out/failed-stages"
          echo "regression-full: $name を上限 $limit 秒で止めた(status $code)" >&2
          return 0 ;;
      esac
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
    stage "e2e の interruption" bash tests/e2e/medium/interruption/verify-interruption.sh

    # 失敗した段があれば 1、無く上限で止まった段があれば未完了の 124
    [ "$status" -ne 0 ] || [ "$stopped" -eq 0 ] || status=124
    touch "$out/failed-stages"
    printf '%s\n' "$status" > "$out/exit-status"
    echo "regression-full: 終了 status $status($out)"
    exit "$status"
  '';

  # 明示して呼ぶ全量の mutation 分析(予算なし、gate にしない): 全量の分析が必要な問いがある場合の入口で verify-full からは呼ばない
  # 生の終了 status を .mutation-output/full/exit-status へ残し 前回の分析から増えた失敗と未検出の変異を work/backlog.md へ記録
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

  # T3 の mutation 分析(gate にしない): 比較元からの変更部分を engine の --since --changed-lines で選ぶ選択型の分析
  # 比較元は前回完了した分析の commit で 記録が無ければ upstream か既定 branch との merge-base
  # 変更部分は engine が選ぶ変わった行に重なる mutant で 影響部分(依存先と逆依存)は engine に選択の機能が無い(#1)ため未分析の範囲として記録
  # 変わった行の判定は changed-lines の生の終了 status で残し 未検出(2)と分析の不成立(1、上限切れ)を分ける
  # 上限は tiergate.json の mutation-select.seconds で 上限で止まった分析は timeout の生の status のまま未完了として残す
  scripts.mutation-select.exec = ''
    set -uo pipefail

    out=.mutation-output/select
    cli=src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll
    mkdir -p "$out" work
    limit=$(jq -er '."mutation-select".seconds' tiergate.json) \
      || { echo "mutation-select: tiergate.json に mutation-select.seconds が無い" >&2; exit 1; }
    deadline=$((SECONDS + limit))
    head=$(git rev-parse HEAD) || { echo "mutation-select: HEAD を読めない" >&2; exit 1; }
    heading="## $(date +%F) mutation-select $(git rev-parse --short HEAD)"
    base="" base_source="" engine="" mutants="" gate=""
    # 分析ごとの記録は上書きしない(比較元と HEAD、生の報告、未検出 mutant ごとの変更行との重なりと後から書く原因と重大度の欄)
    run_dir="$out/runs/$(date -u +%Y%m%dT%H%M%SZ)-$(git rev-parse --short HEAD)"
    mkdir -p "$run_dir"
    rm -f "$out/exit-status" "$out/selection.json" "$out/undetected.txt" "$out/engine.log"
    rm -rf "$out/reports"
    : > "$out/candidates.txt"

    # 選択の記録と生の終了 status の保存 失敗は前回と同じ結果なら backlog へ記録し直さない
    finish() {
      printf '%s\n' "$1" > "$out/exit-status"
      jq -n --arg head "$head" --arg base "$base" --arg baseSource "$base_source" \
        --arg outcome "$2" --arg summary "$3" --argjson limit "$limit" \
        --arg engine "$engine" --arg mutants "$mutants" --arg gate "$gate" \
        --rawfile candidates "$out/candidates.txt" \
        --slurpfile report <(cat "$out/reports/mutation-report.json" 2>/dev/null || echo null) \
        '{ scope: "selected", head: $head, base: $base, baseSource: $baseSource,
           outcome: $outcome, summary: $summary, limitSeconds: $limit,
           candidates: ($candidates | split("\n") | map(select(. != ""))),
           candidatesRole: "file 単位の候補の参考の記録で、生成対象の選択には使わない",
           targets: ($report[0].files // {} | keys),
           selection: "engine の --since --changed-lines が、比較元と作業木の差分と未追跡 file のうち src/Mutation の変わった行に重なる mutant だけを生成する",
           changedLines: "生成した mutant の判定は changed-lines --report --since base の出力(runs/*/changed-lines.txt)と未検出の記録(runs/*/undetected.json)に残す",
           changedLinesExitStatus: ($gate | tonumber? // null),
           unanalyzed: [
             "変更した file のうち変わった行に重ならない箇所",
             "生成対象の依存先と逆依存の consumer",
             "削除した file の変更前の参照から影響する箇所",
             "src/Mutation の外の production(Mutation.Cli、Mutation.Worker)",
             "production でない変更入力(テスト、契約、設定、依存の版、生成器)が検出力や振る舞いを変える production の箇所"
           ],
           unanalyzedReason: "依存関係と変更入力から影響する production の箇所を選ぶ機能が engine に無い(https://github.com/YukiIto1999/mutation-dotnet/issues/1)",
           engineExitStatus: ($engine | tonumber? // null),
           reportRun: ($report[0].run // null),
           mutants: ($mutants | tonumber? // null) }' > "$out/selection.json"
      jq -c . "$out/selection.json" >> "$out/history.jsonl"
      cp "$out/selection.json" "$run_dir/selection.json"
      cp "$out/candidates.txt" "$run_dir/candidates.txt"
      [ ! -f "$out/engine.log" ] || cp "$out/engine.log" "$run_dir/engine.log"
      [ ! -d "$out/reports" ] || cp -r "$out/reports" "$run_dir/reports"
      if [ "$1" -ne 0 ]; then
        if [ "$(cat "$out/failed-stage" 2>/dev/null)" != "$2" ]; then
          printf '\n%s\n\n- [ ] mutation-select: %s\n' "$heading" "$3" >> work/backlog.md
        fi
        printf '%s' "$2" > "$out/failed-stage"
      else
        rm -f "$out/failed-stage"
      fi
      echo "mutation-select: $3(選択型の分析で全量ではない。終了 status $1、記録 $out/selection.json)"
      exit "$1"
    }

    # 完了した分析の commit を次回の比較元にする
    completed() {
      printf '%s\n' "$head" > "$out/analyzed-commit"
    }

    # 走行全体の上限の残りで実行し 残りが無ければ実行せず timeout と同じ 124 を返す
    bounded() {
      local left=$((deadline - SECONDS))
      [ "$left" -gt 0 ] || return 124
      timeout --kill-after=30 "$left" "$@"
    }

    recorded=$(cat "$out/analyzed-commit" 2>/dev/null || true)
    missing="分析の記録が無く"
    [ -z "$recorded" ] || missing="記録した分析の commit $recorded から HEAD との merge-base を得られず"
    if [ -n "$recorded" ] && base=$(git merge-base "$recorded" HEAD 2>/dev/null); then
      base_source="前回完了した分析の commit $recorded(HEAD との merge-base)"
    elif upstream=$(git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null) \
      && base=$(git merge-base "$upstream" HEAD); then
      base_source="$missing、upstream $upstream との merge-base"
    elif default=$(git symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null) \
      && base=$(git merge-base "$default" HEAD); then
      base_source="$missing、upstream も無く、既定 branch $default との merge-base"
    else
      base=""
      base_source="$missing、upstream と既定 branch からも比較元を得られない"
      finish 1 diff-failed "不成立: 比較元を決められず差分を取得できない"
    fi

    # 候補は engine の --since と同じ範囲(比較元と作業木の差分と未追跡 file)の記録で 生成対象の選択には使わない
    { git diff --name-status --no-renames "$base" -- && git ls-files --others --exclude-standard | sed 's/^/?\t/'; } \
      > "$out/candidates.txt" || finish 1 diff-failed "不成立: 比較元 $base からの差分を取得できない"
    if [ ! -s "$out/candidates.txt" ]; then
      completed
      finish 0 no-change "変更なし: 比較元 $base から変更入力が無い"
    fi

    bounded dotnet build Mutation.slnx
    build=$?
    case $build in
      0) ;;
      124 | 137) finish "$build" incomplete "未完了: 上限 $limit 秒の中で分析の前の build が終わらなかった" ;;
      *) finish "$build" build-failed "不成立: 分析の前の build が失敗した" ;;
    esac
    bounded dotnet "$cli" run \
      --project src/Mutation/Mutation.csproj \
      --test-project tests/Mutation.Tests/Mutation.Tests.csproj \
      --since "$base" --changed-lines --with-baseline \
      --concurrency 2 --output "$out" 2>&1 | tee "$out/engine.log"
    engine=''${PIPESTATUS[0]}
    case $engine in
      0) ;;
      124 | 137) finish "$engine" incomplete "未完了: 上限 $limit 秒で分析を止めた" ;;
      *)
        if grep -qF -- "--since の差分を解決できない" "$out/engine.log"; then
          finish "$engine" diff-failed "不成立: engine が比較元 $base からの差分を取得できない"
        fi
        finish "$engine" engine-failed "不成立: 選択した範囲の mutation の終了 status $engine" ;;
    esac
    mutants=$(jq '.counters.mutants' "$out/reports/timings.json") \
      || finish 1 report-unreadable "不成立: 選択した範囲の mutation の報告を読めない"
    if [ "$mutants" -eq 0 ]; then
      completed
      finish 0 no-mutant "候補 $(wc -l < "$out/candidates.txt") 件はあるが、src/Mutation の変わった行に変異できる箇所が 0 件"
    fi

    jq -r '.files | to_entries[] | .key as $file | .value.mutants[]
        | select(.status == "Survived" or .status == "NoCoverage")
        | "\($file):\(.location.start.line) \(.status) \(.mutatorName): \(.replacement)"' \
      "$out/reports/mutation-report.json" > "$out/undetected.txt" \
      || finish 1 report-unreadable "不成立: 選択した範囲の mutation の報告を読めない"
    if [ -s "$out/undetected.txt" ]; then
      { printf '\n%s\n\n' "$heading"; sed 's/^/- [ ] /' "$out/undetected.txt"; } >> work/backlog.md
    fi
    # 変わった行の未検出は道具の changed-lines の判定を使い、独自の差分の判定は持たない
    bounded dotnet "$cli" changed-lines --report "$out/reports/mutation-report.json" --since "$base" \
      > "$run_dir/changed-lines.txt" 2> "$run_dir/changed-lines.err"
    gate=$?
    printf '%s\n' "$gate" > "$run_dir/changed-lines-status"
    case $gate in
      0 | 2) ;;
      124 | 137) finish "$gate" incomplete "未完了: 上限 $limit 秒の中で changed-lines の判定が終わらなかった" ;;
      *) finish "$gate" changed-lines-failed "不成立: changed-lines が報告か比較元 $base からの差分を得られない(終了 status $gate)" ;;
    esac
    jq --arg root "$PWD/" --arg gate "$gate" --arg base "$base" --arg head "$head" \
      --rawfile blocked "$run_dir/changed-lines.txt" \
      '.projectRoot as $project
       | ($blocked | split("\n") | map(select(. != ""))) as $lines
       | { base: $base, head: $head,
           changedLinesExitStatus: ($gate | tonumber),
           overlap: "onChangedLine は changed-lines --report reports/mutation-report.json --since base が未検出として出した mutant かで、真なら T2 の変更行の gate があれば止めていた。範囲は比較元から作業木までの変更行で、その間の push の変更行の和である",
           causeChoices: ["元の実装の欠陥", "検証の不足", "等価変異", "契約内の観測差", "別の保証への影響", "runner または実行環境の失敗"],
           causeStandard: "architecture-standard structure/tests/methods.md「テストの有効性」の原因表。cause、causeEvidence、severity は後から書く",
           mutants: [ .files | to_entries[] | .key as $file | .value.mutants[]
             | select(.status == "Survived" or .status == "NoCoverage")
             | ((($project + "/" + $file) | ltrimstr($root))) as $path
             | "\($path):\(.location.start.line):\(.location.start.column) \(.status) \(.mutatorName): \(.replacement)" as $line
             | { id, file: $path, line: .location.start.line, column: .location.start.column,
                 status, mutator: .mutatorName, replacement,
                 onChangedLine: any($lines[]; . == $line),
                 cause: null, causeEvidence: null, severity: null } ] }' \
      "$out/reports/mutation-report.json" > "$run_dir/undetected.json" \
      || finish 1 report-unreadable "不成立: 選択した範囲の mutation の報告を読めない"
    blocking=$(jq '[.mutants[] | select(.onChangedLine == true)] | length' "$run_dir/undetected.json")
    completed
    finish 0 analyzed "変わった行で変異 $mutants 件、未検出 $(wc -l < "$out/undetected.txt") 件(work/backlog.md)、そのうち changed-lines が変わった行の未検出とした $blocking 件(生の終了 status $gate、$run_dir/undetected.json)"
  '';

  # T3(gate にしない): 全量回帰と選択型の mutation 分析を、それぞれの上限(tiergate.json)を持つ別の走行で順に実行し、終了 status は全量回帰のものを返す。
  # 分析の失敗や未完了は、先に確定した全量回帰の結果を変えない。-e は付けず、全量回帰が失敗しても分析まで進める
  scripts.verify-full.exec = ''
    set -uo pipefail

    regression=0
    regression-full || regression=$?
    analysis=0
    mutation-select || analysis=$?
    echo "verify-full: 全量回帰の終了 status $regression / mutation 分析の終了 status $analysis (終了 status は全量回帰のもの)"
    exit "$regression"
  '';
}
