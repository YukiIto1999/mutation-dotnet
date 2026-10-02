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

  # T2(予算 15 分): T1、Medium の e2e、push する範囲で変わった行の mutation
  scripts.verify-push.exec = ''
    set -euo pipefail

    verify
    bash tests/e2e/medium/verification/verify.sh
    bash tests/e2e/medium/frameworks/verify-frameworks.sh
    bash tests/e2e/medium/changed-lines/verify-changed-lines.sh

    # 比較の基点は upstream との分岐点、upstream の無い枝では develop との分岐点。取れなければ失敗
    base=$(git merge-base HEAD '@{upstream}' 2>/dev/null || git merge-base HEAD origin/develop)
    cli=src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll
    dotnet "$cli" run \
      --project src/Mutation/Mutation.csproj \
      --test-project tests/Mutation.Tests/Mutation.Tests.csproj \
      --concurrency 2 --output .mutation-output/push --since "$base" --changed-lines
    dotnet "$cli" changed-lines --report .mutation-output/push/reports/mutation-report.json --since "$base"
  '';

  # T3(予算なし、gate にしない): 全量の mutation と e2e。前回の T3 から増えた失敗と未検出の変異を docs/backlog.md へ記録する
  scripts.verify-full.exec = ''
    set -euo pipefail

    out=.mutation-output/full
    cli=src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll
    mkdir -p "$out" docs
    heading="## $(date +%F) verify-full $(git rev-parse --short HEAD)"

    # 前回の T3 と同じ段の失敗は記録し直さない
    failed() {
      if [ "$(cat "$out/failed-stage" 2>/dev/null)" != "$1" ]; then
        printf '\n%s\n\n- [ ] %s が失敗した\n' "$heading" "$1" >> docs/backlog.md
      fi
      printf '%s' "$1" > "$out/failed-stage"
      echo "verify-full: $1 が失敗した" >&2
      exit 1
    }

    verify || failed "T1 の verify"
    bash tests/e2e/medium/verification/verify.sh || failed "e2e の verification"
    bash tests/e2e/medium/frameworks/verify-frameworks.sh || failed "e2e の frameworks"
    bash tests/e2e/medium/changed-lines/verify-changed-lines.sh || failed "e2e の changed-lines"
    dotnet "$cli" run \
      --project src/Mutation/Mutation.csproj \
      --test-project tests/Mutation.Tests/Mutation.Tests.csproj \
      --concurrency 2 --output "$out" || failed "全量の mutation"
    mutants=$(jq '.counters.mutants' "$out/reports/timings.json") || failed "全量の mutation の報告の読み取り"
    [ "$mutants" -gt 0 ] || failed "全量の mutation(変異の生成が 0 件。対象の指定を確かめる)"
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
      { printf '\n%s\n\n' "$heading"; sed 's/^/- [ ] /' "$out/delta.txt"; } >> docs/backlog.md
    fi
    echo "verify-full: 変異 $mutants 件、前回の T3 から増えた未検出 $(wc -l < "$out/delta.txt") 件(docs/backlog.md)"
  '';
}
