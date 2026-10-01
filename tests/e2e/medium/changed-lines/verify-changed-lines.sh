#!/usr/bin/env bash
# 一時 repository に置いた検証フィクスチャへ run --since と changed-lines を通し、変わった行の判定と終了コードを照合する
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
printf 'bin/\nobj/\nout/\n' > "$work/.gitignore"

# git の hook の中から起動されても一時 repository だけを操作するよう、呼び出し元の repository を指す環境変数を外す
mapfile -t repository_variables < <(git rev-parse --local-env-vars)
unset "${repository_variables[@]}"

# 基点の commit は hook を起こさない commit-tree で作り、利用者の git 設定はそのまま使う
export GIT_AUTHOR_NAME=fixture GIT_AUTHOR_EMAIL=fixture@example.invalid
export GIT_COMMITTER_NAME=fixture GIT_COMMITTER_EMAIL=fixture@example.invalid
git -C "$work" init -q
git -C "$work" add -A
base="$(git -C "$work" commit-tree -m base "$(git -C "$work" write-tree)")"

# 生存する 11 行目の書き換えと、project の subdirectory に置く未追跡で未被覆のファイル
calculator="$work/src/Fixture.Target/Calculator.cs"
sed -i '11s|$| // changed|' "$calculator"
cat > "$work/src/Fixture.Target/Extra.cs" <<'EOF'
namespace Fixture.Target;

public static class Extra
{
    public static int Triple(int a) => a * 3;
}
EOF

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

judge() {
  local since="$1" report="${2:-$work/out/reports/mutation-report.json}"
  (cd "$work" && dotnet "$cli" changed-lines --report "$report" --since "$since" > "$work/stdout" 2> "$work/stderr") \
    && echo 0 || echo $?
}

dotnet "$cli" run \
  --project "$work/src/Fixture.Target/Fixture.Target.csproj" \
  --test-project "$work/src/Fixture.Target.Tests/Fixture.Target.Tests.csproj" \
  --since "$base" --output "$work/out" > "$work/run.log" 2>&1 \
  || { echo "FAIL run --since"; tail -20 "$work/run.log"; exit 1; }

check "未検出あり の終了コード" 2 "$(judge "$base")"
check "変わった 11 行目の生存" 1 "$(grep -c '^src/Fixture.Target/Calculator.cs:11:[0-9]* Survived ' "$work/stdout" || true)"
check "未追跡ファイルの未被覆" 1 "$(grep -c '^src/Fixture.Target/Extra.cs:5:[0-9]* NoCoverage ' "$work/stdout" || true)"
check "変わっていない 9 行目の未被覆" 0 "$(grep -c 'Calculator.cs:9:' "$work/stdout" || true)"

# git の hook の中では呼び出し元の repository が GIT_DIR で export される。その値に引かれず、報告の repository を読む
check "呼び出し元の GIT_DIR の下での終了コード" 2 "$(GIT_DIR="$(git -C "$repo_root" rev-parse --absolute-git-dir)" judge "$base")"

# 基点を今の内容へ進め、検出される 5 行目と 20 行目だけを書き換える
git -C "$work" add -A
next="$(git -C "$work" commit-tree -p "$base" -m next "$(git -C "$work" write-tree)")"
sed -i -e '5s|$| // changed|' -e '20s|$| // changed|' "$calculator"
check "検出だけ の終了コード" 0 "$(judge "$next")"
check "検出だけ の要約" 1 "$(grep -c '未検出 0 件' "$work/stdout" || true)"

check "解決できない基点 の終了コード" 1 "$(judge no-such-ref)"
check "解決できない基点 の説明" 1 "$(grep -c -- '--since の差分を解決できない' "$work/stderr" || true)"
check "報告なし の終了コード" 1 "$(judge "$next" "$work/missing.json")"

exit "$fail"
