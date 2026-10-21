[English](README.md) | [日本語](README.ja.md)

# mutation-dotnet

mutation-dotnet は、C# / .NET project 向けの mutation testing ツールである。
mutant schemata、被覆に基づくテスト選択、常駐テストホストを組み合わせ、コンパイルとホスト起動の繰り返しを減らす。

## mutation-dotnet が解決すること

mutation testing では、変異のコンパイル、テストホストの起動、無関係なテストの実行に時間がかかる場合がある。
mutation-dotnet は、次の機構で繰り返す処理を減らす。

- 生成した mutant は Roslyn compilation を共有し、実行時に切り替えるため、mutant ごとの project 全体の build を避けられる。
  build の準備と rollback では、追加のコンパイルが必要になる場合がある。
- 常駐 in-process テストホストがテスト基盤を読み込んだまま、per-test カバレッジで被覆テストだけを実行する。
  xunit v2、TUnit、xunit v3 は最初の失敗で打ち切る。
- 演算子ごとの型検査が、無効な候補の一部を emit 前に除外する。
  emit で特定できた無効な変異は rollback し、CompileError として報告する。
  コンパイルを回復できない場合は、その対象を失敗とする。
- RelationalPatternMutator が関係 pattern の演算子を交換し、compile 可能な switch の網羅性を維持する。
- 保存した照合条件が一致すると、過去の判定を再利用できる。
  生成入力の指紋、テスト assembly と変異 assembly の hash が一致すると、build と対象の準備後に、生成、baseline、変異の実行を省ける。
  入力の照合が網羅しない範囲は [docs/limitations.md](docs/limitations.md) にある。

2026 年 8 月に、現行より前の版で実測した結果として、21 module・2,921 mutants の実プロジェクトの全実行は 4 分 21 秒で完了し、compile error の mutant は 0 件だった。
約 18,000 mutants の別対象では、無変更の再実行が snapshot 継承により 32 秒で完了した。
これらの値は現行の版で再測定していない。
測定の手順は [docs/performance.md](docs/performance.md) にある。

## 動作要件

- .NET SDK 10 が必要で、本リポジトリの `devenv shell` が用意する。
- 隣接 checkout の [typemodeling-dotnet](../typemodeling-dotnet) が必要で、相対 `ProjectReference` で参照する。
- 対象 project は SDK-style の .NET project で、net8.0 と net10.0 で実測確認済みである。
  worker が .NET 10 プロセスのため、.NET Framework は対象外である。
- テスト基盤は、xunit v2(in-process front controller)、TUnit と xunit v3(Microsoft.Testing.Platform 1.x と 2.x)、NUnit と MSTest(VSTest adapter の in-process 実行)に対応する。

## 利用を始める

```bash
dotnet build Mutation.slnx
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project path/to/Target.csproj \
  --test-project path/to/Target.Tests.csproj \
  --output .mutation-output
```

module ごとに project を分けている場合は、対を `,` 区切りで並べて一度に渡す。
対象は build を共有し、報告も一つにまとまる。

```bash
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project core/a/A.csproj,core/b/B.csproj \
  --test-project core/a/tests/A.Tests.csproj,core/b/tests/B.Tests.csproj \
  --output .mutation-output
```

対象の一件が失敗しても残りの対象は検査を続け、中断した対象は要約と `timings.json` に残る。
終了コードは 1 になる。

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
| `--exclude-static` | static の文脈にある変異と、テストの外か static 初期化の最中に実行される変異を、通常のテストが被覆していても対象外にし、Ignored として報告 |
| `--with-baseline` | 同じ出力先の同じ種類の前回実行の保存から、不変の mutant の判定を継承。全量の実行は全量の実行から、`--since` の実行は `--since` の実行から継承 |

## レポート

console 要約に加え、`<output>/reports/` へ 2 ファイルを書き出す。

- `mutation-report.json` は mutation-testing report schema の報告で、mutation-testing-elements で表示できる。
- `timings.json` はフェーズ別と mutant 別の実測時間で、ボトルネック分析に使う。

## 変更行の判定

`run --since REF` は、ファイル単位で変異対象を絞る。
`--changed-lines` を加えると、同じ `REF` から変わった行に報告範囲が重なる mutant だけを生成する。
`changed-lines` はその報告を判定し、変わった行に未検出の mutant があれば非ゼロで終了する。
変わった行は、作業木と `REF` の差分の新しい側の行、未追跡ファイルの全行、`REF` から移動か改名したファイルの全行である。
移動と改名は、削除と追加として扱う。
status が `Survived` か `NoCoverage` の mutant を未検出と数え、変わった行の外の mutant は結果に入れない。
変更ごとの実行で生成が 0 件なら成功し、全量の実行で生成が 0 件なら全量の検証入口が失敗する。

push 前の検査の例を示す。
基点は push する範囲の始点で、ここでは upstream との分岐点である。

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

- `devenv shell verify`(2 分以内、commit の前)は、analyzer の警告をエラー扱いにした build と、ユニットテストを実行する。
  build は関数の認知的複雑度(SonarAnalyzer の S3776、上限 15)も判定する。
- `devenv shell verify-push`(15 分以内、push の前)は、`verify`、フィクスチャへの E2E 検証(判定カテゴリ、3 つのテスト基盤、変更行の判定)、自身のエンジンへの push の基点からの変更行の判定を実行する。
  変更した行に生存または未被覆の mutant が一件でもあれば失敗する。
- `devenv shell verify-full`(予算なし、gate にしない、release の前)は、E2E 検証とエンジン全量の mutation testing を実行し、前回の全量に無かった失敗と未検出 mutant を、追跡外の作業台帳 `work/backlog.md` へ追記する。

ブランチ・コミット・リリースの規約は [CONTRIBUTING.md](CONTRIBUTING.md) に、版の記録は [CHANGELOG.md](CHANGELOG.md) にある。

## 文書

[docs/](docs/README.md) は、保守者向けの追跡対象の文書を置く。
内容は、アーキテクチャ、制約、性能の測定手順、標準からの逸脱であり、日本語で書き、現在の source と test を投影する。

`work/` は、計画、調査、実験、下書き、`verify-full` の作業台帳のような、追跡されない作業記録を置く。
`work/` はそれを作った checkout にだけ存在し、追跡対象の文書は `work/` の内容に依存しない。

## 制約事項

- 実測済みの platform は Linux であり、Windows と macOS は実測していない。
- static の文脈にある変異と、テストの外か static 初期化の最中に実行される変異は、隔離が必要になる場合がある。
  通常の被覆テストがあれば共有の worker で先に実行し、生存した場合だけ新規 process で確かめる。
  `--exclude-static` を指定すると、これらの変異は Ignored として報告する。
- source の所在つきの制約は [docs/limitations.md](docs/limitations.md) にある。

## アーキテクチャの標準

アーキテクチャの標準は `/home/nixos/environment/architecture-standard` にある。
準拠の基準は、常に現在の標準本文である。
適用は、標準の README の適用の4則と利用の手順に従う。
決定の記録は、現在の実現方法をコード、振る舞いをテスト、変更の理由を commit の件名、採らなかった理由を判断を所有するコードの近くの実装コメントに置く。
標準からの逸脱は [docs/exceptions.md](docs/exceptions.md) に記録する。

## ライセンス

MIT
