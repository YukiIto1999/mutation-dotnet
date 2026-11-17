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
- 過去の判定は、入力の閉包の全体が一致するときだけ再利用する。
  閉包は、compiler の引数、全ソース、参照する assembly、analyzer と generator、additional file、analyzer config、resource、テストの directory、engine と compiler の版、実行環境、設定である。
  指紋と、テスト assembly と変異 assembly の hash が一致すると、build と対象の準備後に、生成、baseline、変異の実行を省ける。それ以外の差があれば、mutant を再判定する。
  閉包が網羅しない範囲は [docs/limitations.md](docs/limitations.md) にある。

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
| `--since REF` | git の基点から変わったものを候補の選択に使う(作業木と `REF` の差分と、未追跡ファイル、削除、移動)。対象へ届く変更があれば、その対象の production の全範囲を候補にし、何にも届かない変更なら何も選ばない。[変更による選別](#変更による選別)を参照 |
| `--changed-lines` | `--since REF` とともに指定し、明示した部分集合として、変わった production の変更行に報告範囲が重なる mutant だけを生成し、変更が届くその他の関係は未分析として記録する。影響範囲の分析ではない。差分を取得できなければ失敗し、変更ごとの実行で生成が 0 件なら成功 |
| `--break-at SCORE` | mutation score がこの値未満なら終了コード 2 |
| `--ignore-operators NAMES` | 除外する変異演算子の名前。例 `LiteralMutator`。`,` 区切り |
| `--ignore-methods NAMES` | この呼び出しの中を変異させない method 名。例 `ConfigureAwait`。`,` 区切り |
| `--validate-survivors` | 生存 mutant を新規プロセスで再検証 |
| `--exclude-static` | static の文脈にある変異と、テストの外か static 初期化の最中に実行される変異を、通常のテストが被覆していても対象外にし、Ignored として報告 |
| `--with-baseline` | 同じ出力先の同じ種類の前回実行の保存から判定を継承する(全量の実行は全量の実行から、`--since` の実行は `--since` の実行から)。入力の閉包と設定が前回と同一で、mutant のファイル、変異列、被覆するテストも一致するときだけ継承し、ソース、参照、設定、テスト、実行設定のどれが変わっても mutant を再判定する。今回の `CompileError`、`NoCoverage`、`Ignored` の分類は、継承した判定より常に優先する。終わる前に止まった走行は確定した判定を残すので、同じ command で、判定のない mutant の続きを実行できる |
| `--detection-matrix` | 最初の失敗で打ち切らず、mutant ごとに被覆する全テストを実行し、失敗した全テストを報告に記録する。判定は変わらない。前回の判定は継承しないので、`--with-baseline` は効かない |

再利用は、捕捉した compilation、テスト出力、実行環境だけで判定が決まるテストに限って使う。
実行時に読む fixture は test 出力へ含め、それ以外の file、network、時刻に依存するテストでは `--with-baseline` を付けず毎回判定する。
環境変数と作業 directory は hash として同一性に含め、平文では保存しない。

## レポート

console 要約に加え、`<output>/reports/` へ 2 ファイルを書き出す。

- `mutation-report.json` は mutation-testing report schema の報告で、mutation-testing-elements で表示できる。
- `timings.json` はフェーズ別と mutant 別の実測時間で、ボトルネック分析に使う。

`--detection-matrix` を指定すると、`mutation-report.json` の実行した各 mutant は、失敗した全テストを `killedBy` に、実行したテストを `coveredBy` に、結果を観測したテストの数を `testsCompleted` に持つ。
時間切れや異常終了で結果の一部を失った mutant は行列が完全でなく、`testsCompleted` が `coveredBy` の数より小さくなり、`statusReason` が実行されなかったテストを名指しする。
テストは表示名で示す。
この指定では、Microsoft.Testing.Platform はテストを一件ずつ別の呼び出しで実行する。

二つの報告は、走行が全 mutant の判定を確定したかを示す `run` を持つ。
`state` は、全対象が判定に至り全 mutant の判定が確定していれば `completed`、そうでなければ `interrupted`、`failed`、`unjudged` のいずれかである(この順に優先する)。
`interrupted` は取り消しを受けた走行、`failed` は取り消し以外の理由(build、対象の特定、生成、基準実行の失敗)で判定に至らなかった対象がある走行、`unjudged` は判定のない mutant が残った走行である。
`complete` は `completed` のときだけ真で、`unjudged` は判定のない mutant の件数、`unjudgedBy` はその理由別の内訳(`notRun`、`interrupted`、`contendedTimeout`、`runnerFailure`)、`interruptedTargets` は取り消しで止まった対象の名前、`failedTargets` は判定に至る前に失敗した対象の名前である。
判定のない mutant は status が `Pending` で、`statusReason` を持つ。
検出にも未検出にも数えないので、mutation score の母数に入らない。

test runner の失敗も、判定のない理由の一つである。
worker が test host を開けない(共有でも隔離でも)、run に失敗か `killed`、`survived`、`hitlimit` 以外の結末で応える、期限切れでも取り消しでもないのに応答しないときは、その mutant を `Pending` にして `unjudgedBy.runnerFailure` へ数え、どのテストも検出したとは記録しない。
`Killed` か `Timeout` になるのは、テストが失敗した場合、probe 回数の上限か(上の対照の実行を経た)期限の超過、mutant を実行している間に worker process が落ちた場合だけである。
`run` は runner の失敗の件数を出力して終了コード 3 で終わる。

`--since` の走行では、`run.selection` が対象ごとの項目を持つ。
`scope`(`full-production`、`changed-lines`、`no-impact`)、範囲を決めた `reasons`(関係と file)、明示した `--changed-lines` の部分集合が外した `unanalysed` の関係、対象へ届かないと判断した `notInputs` の file である。
[変更による選別](#変更による選別)を参照。

## 走行の中断

`run` は `SIGINT`、`SIGTERM`、`SIGHUP` を受ける。
最初の信号で走行を取り消し、実行中の worker を止め、新しい mutant に取りかからず、確定した判定を `run.state` を `interrupted` にして `mutation-report.json` と `timings.json` に書く。
信号を受けたときに実行していた mutant と、取りかからなかった mutant は `Pending` で、`statusReason` がどちらかを示す。
判定に至らなかった対象は `run.interruptedTargets` に載る。
二度目の信号は、process を直ちに終える。
build と mutant の生成は、取り消しの要求を受けても終えるまで走る。

判定は確定するたびに `<output>/work/<target>/snapshot.verdicts.jsonl` にも追記するので、受け取れない `SIGKILL` で殺された走行も、確定した判定を残す。
続きは同じ command に `--with-baseline` を付けて実行する。
確定した判定を継承し、`Pending` の mutant だけを検査する。
`--detection-matrix` の走行は継承しないので、この方法で続きを実行できない。

| `run` の終了コード | 意味 |
|---|---|
| 0 | 全 mutant の判定が確定し、対象の失敗がなく、score が `--break-at` を満たす |
| 1 | 対象が失敗した、または引数か build が誤っている |
| 2 | mutation score が `--break-at` を下回った |
| 129、130、143 | `SIGHUP`、`SIGINT`、`SIGTERM`(128 に信号番号を足した値)で止まり、途中の報告を書いた |
| 3 | 走行は終わったが、判定のない mutant が残った(`run.state` が `unjudged`) |

テストが予算の時間を超えた mutant は、すぐには検出に数えない。
予算は基準実行の所要時間から決めた固定の値(被覆するテストの 2 倍に 2 秒と、猶予 5 秒)なので、基準より忙しい機でも超える。
超えた後、mutant のない同じテストを新規 process で走らせ、その対照の実行も予算に収まらなければ、`Timeout` でなく `Pending` にして `unjudgedBy.contendedTimeout` へ数える。
収まれば mutant を新規 process でもう一度実行し、終われば再実行の判定を採り、二度目も超えたときだけ `Timeout` とする。
probe 回数の上限は時間によらないので、`Timeout` のままである。
Stryker.NET、StrykerJS、PIT は固定の期限のままで、忙しい機には人が大きくする定数で対処する。

## 変更による選別

`--since REF` では、`REF` から変わった全ファイルを、対象と対にしたテスト project の build graph に照らして分類する。
分類は、production のソース、テスト、参照する project、build の入力、削除、関係不明の file である。
compiler 入力に含まれない file も、外部 import や実行時の fixture として使われうるため、影響なしとは断定しない。関係不明の変更と削除は対象全体へ広げ、`bin` と `obj` の既知の build 成果物だけを除外する。

| 変わったもの | 選択 |
|---|---|
| 変更がない、または除外した build 成果物だけ | `no-impact`。候補は空。除外した file は `run.selection[].notInputs` に記録 |
| production のソース、テスト、参照する project、build 入力、削除、関係不明の file | `full-production`。`--mutate` が許す対象の production の全ファイル。未変更の呼出し元や関係不明の入力について、これより細かい範囲の根拠は示せない。`run.selection[].reasons` に関係と file を記録 |
| 同じ変更に `--changed-lines` | `changed-lines`。変わった production の変更行の mutant だけの明示した部分集合。テスト、build の入力、参照する project、削除、未変更の呼び出し元は `run.selection[].unanalysed` に残り、console 要約が出力し、`changed-lines` が判定の後に出力する |

選別は対象ごとに報告する。関係不明の file は、文書に見えるものも含め、指定した全対象を広げる場合がある。
project 間の参照は、StructuredLogger が MSBuild の評価から作る `ProjectReference` graph を推移的に辿る。assembly 名や出力 directory からは推測しない。
選別は、全体の再利用の指紋に含まれる。
分類が見ない範囲は [docs/limitations.md](docs/limitations.md) にある。

## 変更行の判定

`run --since REF --changed-lines` は、同じ `REF` から変わった行に報告範囲が重なる mutant だけを生成する。
`changed-lines` はその報告を判定し、変わった行に未検出の mutant があれば非ゼロで終了する。
変わった行は、作業木と `REF` の差分の新しい側の行、未追跡ファイルの全行、`REF` から移動か改名したファイルの全行である。
移動と改名は、削除と追加として扱う。
status が `Survived` か `NoCoverage` の mutant を未検出と数え、変わった行の外の mutant は結果に入れない。
変わった行の mutant が `Pending` なら未検出とは数えないが、合格とも言えないので、未検出がなければ `changed-lines` は 0 でなく 1 で終わる。
変更ごとの実行で生成が 0 件なら成功するが、その判定は変わった行だけの結果である。
報告が未分析の関係(変えたテスト、build の入力、削除、未変更の呼び出し元)を記録していれば、`changed-lines` は判定の後にそれを出力する。
全量の実行で生成が 0 件なら全量の検証入口が失敗する。

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
| 1 | 報告を読めない、`REF` との差分を解決できない、または変わった行の mutant に `Pending` があり未検出がない |
| 2 | 変わった行の mutant に未検出が一件以上。各件を `path:line:column status mutator: replacement` で出力 |

## 開発

検証は、時間の予算で分けた devenv の入口で行う。

- `devenv shell verify`(2 分以内、commit の前)は、analyzer の警告をエラー扱いにした build と、ユニットテストを実行する。
  build は関数の認知的複雑度(SonarAnalyzer の S3776、上限 15)も判定する。
- `devenv shell verify-push`(15 分以内、push の前)は、`verify` と、フィクスチャへの E2E 検証(判定カテゴリ、3 つのテスト基盤、変更行の判定、検出行列、`SIGTERM` と `SIGKILL` で止めた走行、保存の再利用とその無効化、変更による選別)を実行する。
  自身のエンジンへの mutation testing は実行しない。
- `devenv shell verify-full`(gate にしない、release の前)は、`regression-full` の後に `mutation-select` を、それぞれ [tiergate.json](tiergate.json) に宣言した上限で止まる別の走行で実行し、終了 status は `regression-full` のものにする。
- `devenv shell regression-full`(15 分以内)は、`verify` と E2E 検証を変更で絞らず全て実行し、終了 status と失敗した段を `.mutation-output/regression-full/` へ残し、前回に無かった失敗した段を、追跡外の作業台帳 `work/backlog.md` へ追記する。
  上限で止まった段は失敗でなく未完了として残し、終了 status 124 で終える。
- `devenv shell mutation-select`(30 分以内)は、前回完了した分析の commit(記録が無ければ upstream との merge-base、どちらも無ければ既定 branch まで遡らず導入時点の HEAD)を比較元にして、`run --since <比較元> --changed-lines --with-baseline` でエンジンの変わった行を分析し、`changed-lines --since <比較元>` で判定する。
  導入時点の HEAD を比較元にした走行は、それより前の source を選択型の分析の対象外として `.mutation-output/select/selection.json` に記録する。全量が要る問いは `mutation-full` を使う。
  `changed-lines` の生の終了 status(変わった行に未検出があれば 2)は、分析の不成立と上限での停止から分けて残す。
  比較元、参考の記録としての変わった file、変異した file、未分析の範囲(変わった file の変わっていない行、変異した file の依存先と逆依存と、ほかの変更入力の影響。[#1](https://github.com/YukiIto1999/mutation-dotnet/issues/1) が未対応)と生の終了 status を `.mutation-output/select/` へ残し、差分の取得の失敗、変更なし、変異 0 件、上限での停止を区別して報告する。
  分析ごとに、生の報告と、未検出 mutant ごとに `changed-lines` が未検出として出したかと後から書く原因と重大度の欄を持つ `undetected.json` を、`.mutation-output/select/runs/` へ上書きせずに残す。
  全量の分析ではなく、全量回帰の結果は変えない。
- `devenv shell mutation-full`(予算なし)は、全量の問いがある場合にエンジン全量の mutation testing を分析として実行し、生の終了 status を `.mutation-output/full/exit-status` へ残し、前回の分析に無かった失敗と未検出 mutant を `work/backlog.md` へ追記する。
  `verify-full` からは実行しない。

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
