[English](README.md) | [日本語](README.ja.md)

# mutation-dotnet

mutation-dotnet は、C# / .NET project 向けの高速な mutation testing ツール。実行時間は build の回数ではなく、各 mutant を被覆するテストの量に比例する。

## mutation-dotnet が解決すること

mutation testing が遅い主因は、mutant ごとの build とテストホスト起動の固定費にある。mutation-dotnet はこの固定費を設計で取り除く。

- mutant schemata が全 mutant を 1 回の Roslyn コンパイルへ埋め込み、実行時に切り替える。build は mutant ごとでなく 1 回で済む
- 常駐 in-process テストホストがテスト基盤を読み込んだまま、per-test カバレッジと fail-fast で被覆テストだけを実行する
- 型検査が compile できない変異を emit 前に除外し、rollback の再コンパイルを避ける
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

## オプション

| option | 意味 |
|---|---|
| `--concurrency N` | worker 数。既定は論理コア数の半分 |
| `--configuration NAME` | build 構成。既定 Debug |
| `--mutate GLOBS` | 変異対象の glob。project directory 相対、`!` で除外、`,` 区切り |
| `--since REF` | git の基点からの変更ファイルだけを対象にする |
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

## 開発

`devenv shell verify` が build、全テスト、E2E 検証、自己適用の mutation ゲートを一括実行する。ゲートは自身のエンジンを変異させ、score が基準未満なら失敗する。ブランチ・コミット・リリースの規約は [CONTRIBUTING.md](CONTRIBUTING.md)、版の記録は [CHANGELOG.md](CHANGELOG.md)。

## 制約事項

- 実測済みの platform は Linux。コードに platform 固有の前提はないが、Windows / macOS は実測未了
- static 初期化でしか実行されない変異は実行時追跡で帰属し、`--exclude-static` 時は Ignored として報告

## ライセンス

MIT
