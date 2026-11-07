# アーキテクチャ

この文書は、mutation-dotnet の engine を変更する保守者が、作業の前に現在の仕組みを確かめるための説明である。
正本は source と test であり、この文書は到達の入口と、source を一つずつ読んでも掴みにくい流れを示す。
source を変える変更と同じ変更で、対応する節を直し、対応する source が無くなった節は取り除く。
利用者向けの option と報告は README、検証入口は CONTRIBUTING、制約は [limitations.md](limitations.md)、測定の手順は [performance.md](performance.md)、標準からの逸脱は [exceptions.md](exceptions.md) にある。
以下の path は、特記がなければ `src/Mutation/` からの相対である。

## 実行の流れ

一回の `run` は、build、対象ごとの継承の照合、変異の生成、baseline、判定、報告の順に進む。
入口は `src/Mutation.Cli/commands/RunCommand.cs` の `Run` で、`--project` と `--test-project` を位置で対にして対象の列を作り、`composition/BuildCore.cs` が組み立てた operation を呼ぶ。
流れ全体の所有者は `verifying/application/workflows/run-mutation-testing/RunMutationTesting.cs` であり、対象一件の遂行は同じ directory の `TargetRun.cs` にある。

build は全対象をまとめて一度行い、csc の呼び出しが binlog に残らなかった対象だけを再 build する。
`mutating/infrastructure/msbuild/BuildExecution.cs` が `dotnet build -bl` を実行し、対象が複数のときは全テスト project を並べた `build-targets.slnx` を作業 directory へ書いて入口にする。
再 build の要否は `PrepareTarget.cs` が判定し、再 build には `--no-incremental` を付ける。
対象の csc 引数とテスト assembly の実行時 path は、`mutating/infrastructure/msbuild/TargetLocation.cs` が binlog から取り出す。
同じ project の csc 呼び出しが binlog に複数あるときは、最後の一回だけを採る。
所在は `mutating/infrastructure/msbuild/BinlogCompilations.cs` の `Read` である。

対象は順に処理し、対象の間は並列にしない。
対象一件の準備や検査が失敗しても、その対象を `TargetOutcome.Failed` として記録して残りの対象を続け、終了コードは 1 になる。
build そのものの失敗だけは全対象に及ぶ失敗として、実行全体を止める。
報告は全対象を通して一つ書き、終了コードの決定は `RunCommand.cs` の `ExitCodeAsync` にある。

## 変異の生成

対象のコンパイルは csc の引数から Roslyn で再構築し、対象が使う source generator も再実行して生成コードを含める。
所在は `mutating/infrastructure/roslyn/TargetCompilationLoader.cs` と `GeneratorExecution.cs` で、振る舞いは `tests/Mutation.Tests/mutating/GeneratorExecutionFacts.cs` が固定する。

候補は構文木を走査して集め、各演算子の構文条件と semantic model による検査で選別する。
`CandidateCollector.cs` が走査し、`MutationContexts.cs` の禁則と各演算子の条件が採否を決め、`MutationNumbering.cs` が文書順に連番を振る。
一部の演算子は `SpeculativeBinding.cs` で置換後の型付けを確認するが、全演算子が全ての置換先を事前に型検査する保証ではない。
どこを変異させるかは、ファイルの glob、演算子名、呼び出し名、変更行の選別を `mutating/domain/aggregates/policy/MutationPolicy.cs` が一手に決める。
振る舞いは `tests/Mutation.Tests/mutating/SemanticFilterFacts.cs` と `MutationPolicyFacts.cs` が固定する。

既定の演算子は `CandidateCollector.DefaultOperators` が列挙する 13 種で、各演算子は `IMutationOperator` を実装する。
`--ignore-operators` は `IMutationOperator.Name`、既定では型名と突き合わせる。
演算子の実装は `mutating/infrastructure/roslyn/operators/` にある。

| 演算子 | 変異の内容 |
|---|---|
| `BinaryOperatorMutator` | 二項演算子を同系統の別演算子へ置き換える |
| `RelationalPatternMutator` | 関係 pattern の演算子を同系統の別演算子へ置き換える |
| `AssignmentMutator` | 複合代入演算子を同系統の別演算子へ置き換える |
| `UnaryMutator` | 単項演算子を除去し、増減演算子を反転する |
| `LiteralMutator` | 真偽値と文字列のリテラルを裏返す |
| `ConditionMutator` | 分岐と繰り返しの条件式を定数または否定へ置き換える |
| `LinqMethodMutator`、`StringMethodMutator`、`MathMethodMutator` | 署名が対称な method の対を入れ替える。仕様は `MethodPairSwaps.cs` の一機構が持つ |
| `InitializerMutator` | 配列と collection の初期化子を空にする |
| `NullCoalesceMutator` | null 合体演算子の片側を落とす |
| `StatementRemover` | 呼び出し文、代入文、増減文を取り除く |
| `MethodBodyNullifier` | member 本体の先頭で早期に脱出させる |

演算子ごとの振る舞いは `tests/Mutation.Tests/mutating/` の `BinaryOperatorMutatorFacts.cs`、`RelationalPatternMutatorFacts.cs`、`ExpandedOperatorFacts.cs`、`MethodBodyNullifierFacts.cs` などが固定する。

`SchemataRewriter.cs` が対象の各 syntax tree へ変異を織り込み、一括 emit した assembly で実行時に一つずつ活性化する。
式は `MutationInjected.MutantSwitch.IsActive(id)` を条件にした三項演算子の連鎖へ、文は同じ条件の `if` と `else` へ、member 本体は先頭に挿入した `if (IsActive(id))` の早期脱出へ織り込む。
式内で真偽を反転して宣言の束縛範囲を保つ必要がある条件は、`(元の式) ^ IsActive(id)` の形にする。
三項演算子を置けない文脈は `StatementContexts.cs` が正規化する。
`tests/Mutation.Tests/mutating/SchemataRoundtripFacts.cs` と `StatementContextFacts.cs` が織り込みの結果を固定する。

注入する `MutantSwitch` の source は `MutantSwitchSource.cs` の `Template` が正本である。
このクラスは、活性化する変異の連番、probe 呼び出し回数の上限、被覆収集の状態、probe ごとの hit の記録を持つ。
static constructor と static 初期化子の実行区間は `StaticContextRewriter.cs` が `EnterStatic`、`ExitStatic`、`TrackStatic` で包み、この区間で観測した hit を static hit として別に記録する。
`tests/Mutation.Tests/mutating/StaticContextTrackingFacts.cs` が固定する。
注入クラスを変えるときは、worker の `src/Mutation.Worker/switching/SwitchAccessor.cs` の反射による束縛を揃える。

emit に失敗したときは、`SchemataCompiler.cs` が rollback する。
`RollbackAnalysis.cs` が compile error の位置から注入の目印 `mutation.id` と static 計装の目印を最内側へ遡って特定し、該当する変異を無効化して再 emit する。
emit の試行は初回を含めて 5 回までで、位置を特定できないか上限に達したときは、書換後の source を `mutated/<対象名>/failed-sources/` に残して失敗する。
rollback で無効化した変異は `CompileError` として報告され、判定の母数には入らない。
型付けの事前検査は compile error を減らすための仕組みであり、`CompileError` の発生を無くす保証ではない。

## 検査の実行

変異を織り込んだ assembly は `mutated/<対象名>/` に書き出す。
テストは常駐する worker process が実行し、隔離が要る変異を除いて、mutant ごとに process を起動しない。
worker の起動と指示の送信は `verifying/infrastructure/workers/WorkerClient.cs` にある。
CLI と worker は匿名パイプで一行の JSON を交わし、指示は `protocol/WorkerRequest.cs` が定める `init`、`discover`、`baseline`、`run`、`exit` の閉じた直和である。
worker が一指示を終えたことは、実 stdout に刻む `WorkerContract.OutputSentinel` で CLI へ伝える。
`protocol/` は CLI 側の `Mutation` と worker の両方が使う契約で、worker は `src/Mutation.Worker/Mutation.Worker.csproj` の `Compile Include` で source を共有する。

worker はテスト assembly の基盤を `src/Mutation.Worker/hosts/HostSelection.cs` で判別し、`ITestHost` の実装を選ぶ。
判別は `xunit.core.dll` があれば xunit v2、VSTest adapter があれば VSTest adapter、Microsoft.Testing.Platform の実行体なら Platform の順である。
変異 assembly への差し替えは、worker が `AssemblyLoadContext.Default.Resolving` で対象 assembly の名前に変異版を返して行う。
所在は `hosts/HostEnvironment.cs` である。

| 基盤 | ホスト | 実行の形 |
|---|---|---|
| xunit v2 | `xunit/XunitTestHost.cs` | `xunit.runner.utility` の in-process 実行で、最初の失敗で打ち切る |
| Microsoft.Testing.Platform | `platform/PlatformTestHost.cs` | テスト実行体の entry point を同一 process で繰り返し呼び、`--filter-uid` で選ぶ。baseline は一テストずつ、判定の run は全 uid を一度に渡す |
| NUnit と MSTest | `vstest/VstestTestHost.cs` | テスト出力に同梱された VSTest adapter を in-process で実行する |

最初の失敗での打ち切りは基盤ごとに違う。
TUnit は `--fail-fast`、xunit v3 は `--stop-on-fail` を `platform/PlatformFlavor.cs` が付ける。
TUnit と xunit v3 以外の Microsoft.Testing.Platform 基盤には、打ち切りの引数を付けない。
VSTest adapter は `ITestExecutor.Cancel` を使わず、最初の失敗を記録するだけで、残りの被覆テストを走らせ切る。
理由は `vstest/VstestTestHost.cs` の実装コメントにある。
framework 内の並列は、xunit v2 で `MaxParallelThreads` を 1 にし、VSTest の run settings で無効にして、per-test の帰属と変異の活性化を一貫させる。
基盤の三系統の判定一致は `tests/e2e/medium/frameworks/verify-frameworks.sh` が固定する。

baseline は変異を全て不活性にした assembly でテストを一度実行する。
テストごとに、結果、所要時間、被覆した変異、probe の呼び出し回数を集め、`verifying/infrastructure/workers/BaselineMapping.cs` が `BaselineProfile` へ写す。
baseline の被覆収集は、テスト数と `--concurrency` に応じて最大 `テスト数 / 8` の worker に分担できる。
`BaselineExecution.cs` が分担し、テストが一件も見つからなければ `PipelineFailure.NoTestsFound` で対象を中断する。
baseline で失敗したテストは以後の選択から除外する。

`verifying/domain/services/MutantScheduler.cs` が、被覆から変異ごとの実行計画を算出する。
被覆するテストが無い変異は実行せず `NoCoverage` にする。
被覆するテストは baseline の所要時間の昇順に並べ、実行時間の予算は被覆テストの所要時間の合計の 2 倍に 2000ms を足した値にする。
昇順は worker へ渡すテスト列の順であり、基盤がその列の順に実行するかは確かめていない。
static 初期化の文脈にある変異と、テスト境界の外か static 初期化の区間で hit した変異は、隔離を必要とする候補として扱う。
隔離は、環境変数 `MUTATION_ACTIVE` で事前に活性化した新規 process で実行する。
新規 process が走らせるテストは、static 初期化の区間で hit したテスト、被覆テスト、同じファイルの変異を被覆するテスト、全ての passing テストの順で、それぞれ所要時間の昇順に並べる。
この隔離候補に通常の被覆テストがある場合は、共有の worker でその被覆テストを先に試し、生存したときに新規 process でも確かめる。
実行形は閉じた直和 `MutantExecution` の `Resident`、`Isolated`、`ResidentThenIsolated` で表す。
隔離実行の同時本数は `IsolationRuns.cs` が 2 本に絞る。
`--exclude-static` を指定すると、構文上 static の文脈にある変異と、テスト境界の外か static 初期化の区間で hit した変異を、通常の被覆テストがあっても `Ignored` にして score の母数から外す。
`--validate-survivors` を指定すると、今回の実行で `Survived` になった隔離でない変異を、`VerifyMutants.cs` が新規 process で再検証して結果を重ねる。
前回から継承した `Survived` は、再検証の対象に入らない。
振る舞いは `tests/Mutation.Tests/verifying/MutantSchedulerFacts.cs` が固定する。

共有 worker が活性の変異の枝へ一度も到達しなかったことを `RunCompleted.Reached == false` で報告した場合、`TestingOrchestrator.cs` は生存・検出のどちらも新規 process で再判定する。
この条件で検出を返した共有 worker は、破棄して作り直す。

無限走行は probe の呼び出し回数の上限で即断する。
上限は `verifying/domain/services/HitLimits.cs` が、実行するテストの baseline の probe 回数の最大値に 200 を掛けて 100,000 を足した値にする。
上限を超えると、注入クラスが目印付きの例外を投げて worker が `hitlimit` を返し、判定は `TimedOut` になる。
probe を通らないループと、例外を握り潰す包括的な catch は、実行時間の予算に応答の猶予 5 秒を足した期限と worker の終了で捕らえる。
`HitLimits` の値は `tests/Mutation.Tests/verifying/VerdictVocabularyFacts.cs` が固定する。

判定は `verifying/infrastructure/workers/VerdictInterpretation.cs` が決める。
baseline で成功したテストが失敗すれば `Killed`、全て成功すれば `Survived` になる。
応答が無く worker が終了していれば `Killed` で、検出の経緯は `(process crash)` とする。
応答が無く worker が生きている場合と、worker が `Failed` を返した場合は `TimedOut` になる。
`Killed` と `TimedOut` は score の検出に数えられるので、worker の異常終了と失敗も検出に入る。
Microsoft.Testing.Platform の run は killer test の名前を返さないため、`TerminalOutput.cs` が端末出力の `failed <名前> (<所要時間>)` の行から取り出し、取り出せなければ `(unknown test)` とする。
`tests/Mutation.Tests/verifying/VerdictInterpretationFacts.cs` が固定する。

## 継承

`--with-baseline` を指定すると、対象ごとに `work/<対象名>/snapshot.json` へ判定を保存し、次の実行で再利用する。
保存と再利用の所在は `verifying/infrastructure/snapshot/` にある。
再利用には、全体の再構築と変異ごとの継承の二つの経路があり、照合する入力が違う。

全体の再構築は、build と対象の特定の後、変異の生成より前に判定する。
生成入力の指紋、テスト assembly の byte hash、保存した変異 assembly の byte hash が全て前回と一致すると、`SnapshotReconstruction.cs` が変異の生成、compile、baseline、テスト実行を全て省いて前回の判定を復元する。
指紋は `mutating/infrastructure/fingerprint/CompilationFingerprint.cs` が、csc 引数の文字列、csc が読む全ソースの path と内容 hash、差分運用なら対象のソースごとの変わった行の範囲、選別と検査の設定から計算する。
指紋を計算するのは `--with-baseline` のときだけなので、全体の再構築には、指紋を保存した前回の実行が要る。
参照する assembly、analyzer、generator、`AdditionalFiles`、analyzer config の内容と、tool と compiler の版は、指紋に入らない。
それらの path が csc 引数に現れる場合は、path の文字列だけが指紋に入る。

全体が一致しないときは、baseline の後に `SnapshotReuse.cs` が変異ごとに判定を継承する。
継承する変異は、そのファイルの内容 hash、ファイル内の変異列の演算子と位置と置換、テスト assembly の byte hash、変異を被覆する通常のテストの集合が、前回と全て一致するものに限る。
継承するのは `Killed`、`Survived`、`TimedOut` である。
この経路は生成入力の指紋と設定を照合せず、他のファイルの内容も照合しない。
そのため、設定の変更や別のファイルの変更が継承する変異の意味を変えても、上の条件が一致すれば前回の判定を継承する。
判定の合成は、今回の `CompileError`、`NoCoverage`、`Ignored` を先に置き、継承した判定、実行した判定の順に上書きする。
所在は `verifying/application/workflows/verify-mutants/VerifyMutants.cs` の `MergedVerdicts` である。
この上書きの順序が今回の分類に与える影響は、実行で確かめていない。
`tests/Mutation.Tests/verifying/SnapshotReuseFacts.cs` と `SnapshotReconstructionFacts.cs` は、上の局所的な条件を固定するもので、依存の閉包の健全性までは保証しない。

## 出力の配置

出力 directory の配置は `verifying/application/workflows/run-mutation-testing/RunLayout.cs` が一点で所有する。
次の表の path は `--output` で指定した出力 directory からの相対であり、リポジトリ直下の作業記録用 `work/` とは別である。

| 置き場 | 内容 |
|---|---|
| `work/` | 対象間で共有する build の中間物と binlog |
| `work/<対象名>/` | 対象ごとの `snapshot.json` とテスト実行体の成果物 |
| `mutated/<対象名>/` | 変異 assembly と、失敗時の `failed-sources/` |
| `since/work/<対象名>/`、`since/mutated/<対象名>/` | `--since` を指定した実行の保存と変異 assembly |
| `reports/` | 対象をまたいで一つの `mutation-report.json` と `timings.json` |

`--since` の実行は、全量の実行の保存と変異 assembly を上書きしないよう、`since/` の下に置く。
`--mutate`、`--ignore-operators`、`--ignore-methods` だけが違う実行は同じ置き場を共有する。
全体の再構築は設定を含む指紋が違えば起きないが、変異ごとの継承は設定を照合しないので、これらを切り替えた実行の間で前回の判定を継承し得る。

## 報告

`reports/mutation-report.json` は mutation-testing-elements の report schema の書式で、`verifying/infrastructure/reports/StrykerReport.cs` が書き出す。
変異の id は `<対象名>:<連番>` で修飾して対象をまたいで一意にする。
`reports/timings.json` は `TimingsReport.cs` が書き出し、`phases`、`counters`、`targets`、`mutants` を持つ。
`phases` は build、mutate、compile、baseline、testing、total の段階別時間、`counters` は変異数、テスト数、score、worker の再起動回数、判定別件数、`targets` は対象別の内訳と中断、`mutants` は変異別の所要時間である。

`changed-lines` command は、`run --since` が書いた報告を、`composition/adapters/ChangedLinesFromGit.cs` が返す変更行と突き合わせ、変わった行に重なる未検出の mutant を判定する。
`Survived` と `NoCoverage` を未検出と数える。
所在は `verifying/application/usecases/JudgeChangedLines.cs` で、`tests/Mutation.Tests/root/ChangedLinesGateFacts.cs` と `verifying/JudgeChangedLinesFacts.cs` が固定する。
差分は `git diff --no-renames` の一回の解決で得て、移動と改名を削除と追加とみなす。
`--since` の file 選別と `changed-lines` の判定は、同じ解決を使う。
所在は `mutating/infrastructure/git/` である。

## 構造と依存方向

`src/` は三つの project からなる。

| project | 役割 |
|---|---|
| `src/Mutation/` | engine。文脈、共有の値、契約、組立点を持つ |
| `src/Mutation.Cli/` | CLI。`composition/BuildCore.cs` が返した operation を呼び、表示だけを持つ |
| `src/Mutation.Worker/` | 常駐するテストホスト。基盤ごとのホストと、変異 assembly の切替を持つ |

engine は、変異を作る `mutating/` と、検査して run を所有する `verifying/` の二つの文脈に分かれる。
文脈の中は、`domain/`、`application/`、`infrastructure/` の層に分かれ、依存は infrastructure から application、application から domain へ向かう。
`shared/` は二つの文脈が同じ意味で使う値と失敗を持ち、`protocol/` は CLI と worker の契約を持つ。
`composition/` は adapter を port へ配線し、外の設定を workflow の語彙へ写して呼ぶだけで、業務判断を持たない。
`verifying/` の `IMutantSource` は変異の取得を port として宣言し、`composition/adapters/MutantSourceFromMutating.cs` が `mutating/` の use-case へ写す。

依存方向と層の構成は `tests/Mutation.Tests/root/ArchitectureFacts.cs` が検査する。
検査の内容は次のとおりである。

- 二つの文脈が互いと composition を参照しない。
- domain が application と infrastructure を参照しない。
- application が infrastructure を参照しない。
- domain と application の直下が、規範の単位フォルダだけで構成される。
- 各 project の root 直下が entry point だけである。
- shared kernel がどの文脈にも依存しない。
- protocol がどの境界にも依存しない。
- CLI が infrastructure へ届かない。

engine の `Result` と閉じた直和は、隣接 checkout の typemodeling-dotnet を `src/Mutation/Mutation.csproj` の `ProjectReference` で参照する。

テストの配置は次のとおりである。

| 場所 | 内容 |
|---|---|
| `tests/Mutation.Tests/` | ユニットテストと構造検査 |
| `tests/e2e/medium/` | 判定カテゴリ、三つのテスト基盤、変更行の判定を実 process で検証する fixture と script |
| `tests/performance/` | 合成対象と実 project への測定 script |

## 保守の注意

- 注入クラスの正本は `MutantSwitchSource.Template` である。
  変えたら、worker の `SwitchAccessor` の束縛を揃える。
- xunit v2 の message sink は `SynchronousMessageReporting` を有効にして、被覆の drain の順序を保証している。
  所在は `src/Mutation.Worker/xunit/XunitTestHost.cs` である。
- Linux の binlog では csc 実行 file の絶対 path が `/` で始まるため、csc 引数の option 判定は `BinlogCompilations.IsOptionLike` に集約している。
- `TestingOrchestrator.ExecuteAsync` は呼び出しごとに独立した集計を返し、survivor の再検証の合成は `OrchestrationResult.Overlaid` が行う。
