[English](README.md) | [日本語](README.ja.md)

# mutation-dotnet

mutation-dotnet は、C# / .NET project 向けの高速な mutation testing ツール。実行時間は build の回数ではなく、各 mutant を被覆するテストの量に比例する。

## mutation-dotnet が解決すること

mutation testing が遅い主因は、mutant ごとの build とテストホスト起動の固定費にある。mutation-dotnet はこの固定費を設計で取り除く。

- mutant schemata が全 mutant を 1 回の Roslyn コンパイルへ埋め込み、実行時に切り替える。build は mutant ごとでなく 1 回で済む
- 常駐 in-process テストホストがテスト基盤を読み込んだまま、per-test カバレッジと fail-fast で被覆テストだけを実行する
- 型検査が compile できない変異を emit 前に除外し、rollback の再コンパイルを避ける
- **RelationalPatternMutator** — 関係 pattern の演算子を交換し、compile 可能な switch の網羅性を維持する。
- snapshot が前回実行から不変の mutant の判定を継承し、生成入力が完全一致なら全段階を省略する

21 module・2,921 mutants の実プロジェクトの全実行は 4 分 21 秒で、compile error の mutant は 0 件。約 18,000 mutants の別対象では、無変更の再実行が snapshot 継承により 32 秒で完了する。

## 動作要件

- .NET SDK 10(本リポジトリの `devenv shell` が用意)
- 隣接 checkout の [typemodeling-dotnet](../typemodeling-dotnet)(相対 `ProjectReference` で参照)
- 対象 project: SDK-style の .NET project(net8.0 と net10.0 で実測確認済み。worker が .NET 10 プロセスのため .NET Framework は対象外)
- テスト基盤: xunit v2(in-process front controller)、TUnit / xunit v3(Microsoft.Testing.Platform 1.x / 2.x)、NUnit / MSTest(VSTest adapter の in-process 実行)

## 利用を始める

```bash
dotnet build Mutation.slnx
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project path/to/Target.csproj \
  --test-project path/to/Target.Tests.csproj \
  --output .mutation-output
```

module ごとに project を分けている場合は、対を `,` 区切りで並べて一度に渡す。build は一度だけになり、報告も一つにまとまる。

```bash
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project core/a/A.csproj,core/b/B.csproj \
  --test-project core/a/tests/A.Tests.csproj,core/b/tests/B.Tests.csproj \
  --output .mutation-output
```

対象の一件が失敗しても残りの対象は検査を続け、中断した対象は要約と `timings.json` に残る。終了コードは 1 になる。

## オプション

| option | 意味 |
|---|---|
| `--project PATHS` | 変異対象 project。`,` 区切りで複数 |
| `--test-project PATHS` | テスト project。`--project` と同数を同じ順で並べる |
| `--concurrency N` | worker 数。既定は論理コア数の半分 |
| `--configuration NAME` | build 構成。既定 Debug |
| `--mutate GLOBS` | 変異対象の glob。project directory 相対、`!` で除外、`,` 区切り |
| `--since REF` | git の基点から内容が変わったファイルだけを対象にする(作業木と `REF` の差分と、未追跡ファイル)。`REF` から移動か改名したファイルは追加の扱い |
| `--changed-lines` | `--since REF` とともに指定し、報告する範囲が変更行に重なる mutant だけを生成する。差分を取得できなければ失敗し、変更ごとの実行で生成が 0 件なら成功 |
| `--break-at SCORE` | mutation score がこの値未満なら終了コード 2 |
| `--ignore-operators NAMES` | 除外する変異演算子の名前。例 `LiteralMutator`。`,` 区切り |
| `--ignore-methods NAMES` | この呼び出しの中を変異させない method 名。例 `ConfigureAwait`。`,` 区切り |
| `--validate-survivors` | 生存 mutant を新規プロセスで再検証 |
| `--exclude-static` | static 初期化でしか実行されない変異を対象外にし、Ignored として報告 |
| `--with-baseline` | 前回実行の保存から不変の mutant の判定を継承 |

## レポート

console 要約に加え、`<output>/reports/` へ 2 ファイルを書き出す。

- `mutation-report.json` は mutation-testing report schema。mutation-testing-elements で表示できる
- `timings.json` はフェーズ別・mutant 別の実測時間。ボトルネック分析に使う

## 変更行の判定

`run --since REF` はファイル単位で変異対象を絞る。`--changed-lines` を加えると、同じ `REF` から変わった行に報告範囲が重なる mutant だけを生成する。`changed-lines` はその報告を判定し、変わった行に未検出の mutant があれば非ゼロで終了する。変わった行は、作業木と `REF` の差分の新しい側の行と、未追跡ファイルの全行と、`REF` から移動か改名したファイルの全行。移動と改名は削除と追加の扱い。status が `Survived` か `NoCoverage` の mutant を未検出と数え、変わった行の外の mutant は結果に入れない。変更ごとの実行で生成が 0 件なら成功し、全量の実行で生成が 0 件なら全量の検証入口が失敗する。

push 前の検査の例。基点は push する範囲の始点で、ここでは upstream との分岐点。

```bash
base=$(git merge-base HEAD '@{upstream}' 2>/dev/null || git merge-base HEAD origin/develop)
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project path/to/Target.csproj \
  --test-project path/to/Target.Tests.csproj \
  --since "$base" --changed-lines --output .mutation-output/push
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll changed-lines \
  --report .mutation-output/push/reports/mutation-report.json --since "$base"
```

| 終了コード | 意味 |
|---|---|
| 0 | 変わった行の mutant に未検出なし。変わった行に mutant が一件もない場合を含む |
| 1 | 報告を読めない、または `REF` との差分を解決できない |
| 2 | 変わった行の mutant に未検出が一件以上。各件を `path:line:column status mutator: replacement` で出力 |

## 開発

検証は、時間の予算で分けた devenv の三つの入口で行う。

- `devenv shell verify`(2 分以内、commit の前)が、analyzer の警告をエラー扱いにした build と、ユニットテストを実行する。build は関数の認知的複雑度(SonarAnalyzer の S3776、上限 15)も判定する
- `devenv shell verify-push`(15 分以内、push の前)が、`verify`、フィクスチャへの E2E 検証(判定カテゴリ、3 つのテスト基盤、変更行の判定)、自身のエンジンへの push の基点からの変更行の判定を実行する。変更した行に生存または未被覆の mutant が一件でもあれば失敗する
- `devenv shell verify-full`(予算なし、gate にしない、release の前)が、E2E 検証とエンジン全量の mutation testing を実行し、前回の全量に無かった失敗と未検出 mutant を backlog へ追記する

ブランチ・コミット・リリースの規約は [CONTRIBUTING.md](CONTRIBUTING.md)、版の記録は [CHANGELOG.md](CHANGELOG.md)。

## 制約事項

- 実測済みの platform は Linux。コードに platform 固有の前提はないが、Windows / macOS は実測未了
- static 初期化でしか実行されない変異は実行時追跡で帰属し、`--exclude-static` 時は Ignored として報告

## アーキテクチャの標準

アーキテクチャの標準は `/home/nixos/environment/architecture-standard` にある。
準拠の基準は、常に現在の標準本文である。
適用は、標準の README の適用の4則と利用の手順に従う。
決定の記録は git 管理外の `docs/decisions/` にある。

## ライセンス

MIT
