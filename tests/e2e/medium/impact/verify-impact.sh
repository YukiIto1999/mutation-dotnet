#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../.." && pwd)"
fixture_dir="$repo_root/tests/e2e/medium/verification"
cli="$repo_root/src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/src" "$work/out"
cp "$fixture_dir/Directory.Build.props" "$work/"
cp -r "$fixture_dir/Fixture.Target" "$fixture_dir/Fixture.Target.Tests" "$work/src/"
rm -rf "$work"/src/*/bin "$work"/src/*/obj
printf 'bin/\nobj/\nout/\n' > "$work/.gitignore"
printf 'namespace Fixture.Target;\n\ninternal static class Spare\n{\n}\n' > "$work/src/Fixture.Target/Spare.cs"
mkdir -p "$work/src/Fixture.Shared" "$work/src/Fixture.Transitive"
printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Fixture.Shared/Fixture.Shared.csproj" /></ItemGroup></Project>\n' > "$work/src/Fixture.Target/Fixture.Target.csproj"
printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Fixture.Transitive/Fixture.Transitive.csproj" /></ItemGroup></Project>\n' > "$work/src/Fixture.Shared/Fixture.Shared.csproj"
printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$work/src/Fixture.Transitive/Fixture.Transitive.csproj"
printf 'namespace Fixture.Dependency; public static class Marker { public const int Value = 1; }\n' > "$work/src/Fixture.Transitive/Marker.cs"
printf 'namespace Fixture.Shared; public static class Bridge { public const int Value = Fixture.Dependency.Marker.Value; }\n' > "$work/src/Fixture.Shared/Bridge.cs"

# 呼出し元の repository を操作しないため hook 由来の GIT 環境変数の継承は不可
mapfile -t repository_variables < <(git rev-parse --local-env-vars)
unset "${repository_variables[@]}"

export GIT_AUTHOR_NAME=fixture GIT_AUTHOR_EMAIL=fixture@example.invalid
export GIT_COMMITTER_NAME=fixture GIT_COMMITTER_EMAIL=fixture@example.invalid
git -C "$work" init -q
git -C "$work" add -A
base="$(git -C "$work" commit-tree -m base "$(git -C "$work" write-tree)")"

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

run_since() {
  local name="$1"
  shift
  dotnet "$cli" run \
    --project "$work/src/Fixture.Target/Fixture.Target.csproj" \
    --test-project "$work/src/Fixture.Target.Tests/Fixture.Target.Tests.csproj" \
    --since "$base" --output "$work/out/$name" "$@" > "$work/out/$name.log" 2>&1 \
    || { echo "FAIL run $name"; tail -20 "$work/out/$name.log"; exit 1; }
}

report() { echo "$work/out/$1/reports/mutation-report.json"; }
mutants() { jq '[.files[].mutants[]] | length' "$(report "$1")"; }
scope() { jq -r '.run.selection[0].scope' "$(report "$1")"; }
reasons() { jq -r '[.run.selection[0].reasons[].relation] | sort | join(",")' "$(report "$1")"; }
unanalysed() { jq -r '[.run.selection[0].unanalysed[].relation] | sort | join(",")' "$(report "$1")"; }

printf 'documentation only\n' > "$work/guide.md"
run_since unknown
check "関係不明な変更の範囲" full-production "$(scope unknown)"
check "関係不明な変更の候補" 17 "$(mutants unknown)"
check "関係不明な変更の関係" unknown-input "$(reasons unknown)"
rm "$work/guide.md"

printf '\n// transitive dependency changed\n' >> "$work/src/Fixture.Transitive/Marker.cs"
run_since dependency
check "推移的な参照の変更の範囲" full-production "$(scope dependency)"
check "推移的な参照の変更の関係" dependency-changed "$(reasons dependency)"
check "推移的な参照の変更の候補" 17 "$(mutants dependency)"
git -C "$work" show "$base:src/Fixture.Transitive/Marker.cs" > "$work/src/Fixture.Transitive/Marker.cs"

printf '\n// a test changed\n' >> "$work/src/Fixture.Target.Tests/CalculatorTests.cs"
run_since test-only
check "テストだけの変更の範囲" full-production "$(scope test-only)"
check "テストだけの変更の関係" test-changed "$(reasons test-only)"
check "テストだけの変更の候補は対象の全範囲" 17 "$(mutants test-only)"
check "テストだけの変更の要約" 1 "$(grep -c '^選別 Fixture.Target: full-production' "$work/out/test-only.log" || true)"

run_since test-only-lines --changed-lines
check "変更行の部分集合の範囲" changed-lines "$(scope test-only-lines)"
check "変更行の部分集合の候補" 0 "$(mutants test-only-lines)"
check "変更行の部分集合の未分析" test-changed "$(unanalysed test-only-lines)"
gate_status=0
(cd "$work" && dotnet "$cli" changed-lines --report "$(report test-only-lines)" --since "$base" > "$work/out/gate.out" 2> "$work/out/gate.err") || gate_status=$?
check "未分析を残した変更行の gate の終了コード" 0 "$gate_status"
check "gate が未分析の影響範囲を示す" 1 "$(grep -c '未分析の影響範囲' "$work/out/gate.out" || true)"

sed -i 's|<PropertyGroup>|<!-- configuration changed -->\n  <PropertyGroup>|' "$work/Directory.Build.props"
run_since configured
check "設定の変更の範囲" full-production "$(scope configured)"
check "設定の変更の関係" config-changed,test-changed "$(reasons configured)"

rm "$work/src/Fixture.Target/Spare.cs"
run_since removed
check "消えたファイルの範囲" full-production "$(scope removed)"
check "消えたファイルの関係" config-changed,file-removed,test-changed "$(reasons removed)"
check "消えたファイルの候補は対象の全範囲" 17 "$(mutants removed)"

exit "$fail"
