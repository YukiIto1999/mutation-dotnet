#!/usr/bin/env bash
# 再設計調査の比較計測一式: workload の準備、cold 反復、高並列、MTP 実 project、増分、依存変更、メモリ制約
# usage: scenarios.sh [prepare|cold|incremental|all]
# 前提: Mutation.slnx を build 済み、dotnet-stryker を tool manifest で restore 済み、cgroup v1 が書込み可能
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
base="$(cd "$here/.." && pwd)"
repo_root="$(cd "$base/../../.." && pwd)"
W="$base/work/workloads"
R="$here/run-one.sh"
P1=Bench.Target/Bench.Target.csproj; T1=Bench.Target.Tests/Bench.Target.Tests.csproj

prepare() {
  rm -rf "$W"; mkdir -p "$W"
  cp -r "$repo_root/tests/e2e/medium/verification" "$W/fixture"
  for w in oracle pollution stale; do cp -r "$base/workloads/$w" "$W/$w"; done
  python3 "$repo_root/tests/performance/medium/generate_target.py" "$W/synth40" --modules 40
  python3 "$repo_root/tests/performance/medium/generate_target.py" "$W/synth200" --modules 200
  python3 "$repo_root/tests/performance/medium/generate_target.py" "$W/synthslow" --modules 40
  python3 "$here/make-slow-tests.py" "$W/synthslow"
  cp -r "$W/synth40" "$W/synth40git"
  for g in synth40git stale; do
    (cd "$W/$g" && git init -q -b main && git add -A && git -c user.name=bench -c user.email=bench@example.invalid commit -q -m v1)
  done
}

cold() {
  for i in 1 2 3 4 5; do $R ours fixture-ours-c2-$i "$W/fixture" Fixture.Target/Fixture.Target.csproj Fixture.Target.Tests/Fixture.Target.Tests.csproj 2; done
  for i in 1 2 3; do $R stryker fixture-stryker-c2-$i "$W/fixture" Fixture.Target/Fixture.Target.csproj Fixture.Target.Tests/Fixture.Target.Tests.csproj 2; done
  for i in 1 2 3 4 5; do $R ours oracle-ours-c2-$i "$W/oracle" Oracle.Target/Oracle.Target.csproj Oracle.Target.Tests/Oracle.Target.Tests.csproj 2; done
  for i in 1 2 3; do $R stryker oracle-stryker-c2-$i "$W/oracle" Oracle.Target/Oracle.Target.csproj Oracle.Target.Tests/Oracle.Target.Tests.csproj 2; done
  for i in 1 2 3 4 5; do $R ours synth40-ours-c2-$i "$W/synth40" $P1 $T1 2; done
  for i in 1 2 3; do $R stryker synth40-stryker-c2-$i "$W/synth40" $P1 $T1 2; done
  for i in 1 2 3; do $R ours synth200-ours-c2-$i "$W/synth200" $P1 $T1 2; done
  $R stryker synth200-stryker-c2-1 "$W/synth200" $P1 $T1 2
  for i in 1 2 3; do $R ours synthslow-ours-c2-$i "$W/synthslow" $P1 $T1 2; done
  $R stryker synthslow-stryker-c2-1 "$W/synthslow" $P1 $T1 2
  for i in 1 2 3; do $R ours synth40-ours-c4-$i "$W/synth40" $P1 $T1 4; done
  $R stryker synth40-stryker-c4-1 "$W/synth40" $P1 $T1 4
  $R ours m-synth200-ours-c2-512 "$W/synth200" $P1 $T1 2 --mem 512
  $R stryker m-synth200-stryker-c2-512 "$W/synth200" $P1 $T1 2 --mem 512
  # MTP/TUnit の実 project: 本 repository 自身
  $R ours self-ours-c2-1 "$repo_root" src/Mutation/Mutation.csproj tests/Mutation.Tests/Mutation.Tests.csproj 2
}

incremental() {
  G="$W/synth40git"
  reset_git() { (cd "$1" && git checkout -q -- . && git clean -q -fdx -e bin -e obj); }
  for tool in ours stryker; do
    reset_git "$G"; out="$base/work/out/inc-$tool"; rm -rf "$out" "$out-since"
    if [ $tool = ours ]; then bl="--with-baseline"; else bl="--with-baseline:main"; fi
    OUTDIR=$out $R $tool i-$tool-1-cold "$G" $P1 $T1 2 -- $bl
    OUTDIR=$out $R $tool i-$tool-2-warm "$G" $P1 $T1 2 --keep -- $bl
    (cd "$G" && printf '\n// change\n' >> Bench.Target/Mod3.cs)
    OUTDIR=$out $R $tool i-$tool-3-src "$G" $P1 $T1 2 --keep -- $bl
    (cd "$G" && sed -i 's#^}$#    [Fact] public void Extra_case() => Assert.Equal(1, 1);\n}#' Bench.Target.Tests/Mod5Tests.cs)
    OUTDIR=$out $R $tool i-$tool-4-test "$G" $P1 $T1 2 --keep -- $bl
    if [ $tool = ours ]; then since="--since HEAD"; else since="--since:main"; fi
    OUTDIR=$out-since $R $tool i-$tool-5-since "$G" $P1 $T1 2 --keep -- $since
  done
  reset_git "$G"
  S="$W/stale"
  for tool in ours stryker; do
    reset_git "$S"; out="$base/work/out/stale-$tool"; rm -rf "$out"
    if [ $tool = ours ]; then bl="--with-baseline"; else bl="--with-baseline:main"; fi
    OUTDIR=$out $R $tool s-$tool-1 "$S" Stale.App/Stale.App.csproj Stale.App.Tests/Stale.App.Tests.csproj 2 -- $bl
    cp "$S/Normalizer.v2.cs.txt" "$S/Stale.Core/Normalizer.cs"
    OUTDIR=$out $R $tool s-$tool-2 "$S" Stale.App/Stale.App.csproj Stale.App.Tests/Stale.App.Tests.csproj 2 --keep -- $bl
  done
  reset_git "$S"
}

case "${1:-all}" in
  prepare) prepare ;;
  cold) cold ;;
  incremental) incremental ;;
  all) prepare; cold; incremental ;;
esac
python3 "$here/summarize.py" "$base/work/out/runs/*.json"
