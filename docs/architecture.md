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

`--since` の実行では、変異させるファイルの集合を、変わったファイルが対象の入力へ届く関係から選ぶ。
`GenerateMutants.Assess` が `mutating/domain/services/ChangeImpact.cs` を呼び、その結果の集合を `MutationPolicy` へ渡す。
関係の判定に使う入力は、`mutating/infrastructure/msbuild/TargetGraphs.cs` が MSBuild の評価と compiler 呼出しから作る。
project 参照の解釈は StructuredLogger の `ProjectReferenceGraph` に委ね、対象とテストから推移的に辿った project の compiler 入力を集める。assembly 名や出力 directory では参照関係を推測しない。
増分 build で参照先の compiler 呼出しが省かれた場合、`PrepareTarget.cs` が `--no-incremental` の再 build で補う。
変更は、production、テスト、参照先、build 入力、削除、関係不明に分類する。compiler 入力に現れないことだけでは影響なしにせず、関係不明なら全範囲へ広げる。既知の入力でない build 成果物だけを除外する。
届く関係が一つもなければ `no-impact` で候補は空である。
届く関係があれば、変えたソースの未変更の呼び出し元と、変えたテストが検出する production を file より細かく選べないので、対象の全範囲(`full-production`)を候補にし、呼び出し元を `callers-of-changed-production` として理由に加える。
`--changed-lines` は広げず、変えたソースだけを候補にする明示した部分集合(`changed-lines`)にし、ほかの関係を `unanalysed` として残す。
消えたファイルと、行の差分を持たない binary のファイルは、`mutating/infrastructure/git/ChangedFiles.cs` が `git diff --name-only` から拾い、`ChangedLines.Deleted` に載せる。
選択は `CandidateSelection` として `PreparedTarget`、`TargetResult` を通り、`RunStateReport.cs` が報告の `run.selection` に書く。
選択を見分ける鍵(`ImpactAssessment.Key`)は、全体の再構築の指紋に含める。
振る舞いは `tests/Mutation.Tests/mutating/ChangeImpactFacts.cs`、`tests/Mutation.Tests/root/ChangedLinesRunFacts.cs` と `ChangedLinesDeletionFacts.cs`、`tests/e2e/medium/impact/verify-impact.sh` が固定する。

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
`WorkerClient` は応答を読んだ後も目印を待ち、その指示の処理中に worker が書いた出力を応答に添える。
この回収は `tests/Mutation.Tests/verifying/TestingOrchestratorOutputFacts.cs` が固定する。
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

`--detection-matrix` を指定すると、CLI は `run` 指示の `AllFailures` を立て、worker は最初の失敗で打ち切らずに求められた全テストを実行する。
xunit v2 と VSTest adapter は一回の実行の中でテストごとの失敗と完了を記録し、Microsoft.Testing.Platform はテストごとに entry point を呼んで失敗をテストへ帰属させる。
記録の型は `src/Mutation.Worker/observation/RunObservation.cs` で、worker は失敗した全テストと結果を観測した全テストの識別子を `RunCompleted` で返す。
`verifying/infrastructure/workers/DetectionRows.cs` がそれを変異ごとの `DetectionRow` へ写し、応答が無い run は求めた全テストを未実行とする。
判定は最初の失敗で決まるので、既定の実行と同じ規則である。
通常の被覆と static または ambient の被覆を両方持つ変異は、既定の実行では共有側で検出すれば隔離側を走らせないが、行列の実行では隔離側のテストも走らせ、`DetectionRows.Merge` で両方の行を一行へ合わせる。
判定は共有側の検出のままである。
検出行列は前回の判定から得られないため、この指定は `--with-baseline` の継承と再利用を止める。
所在は `composition/operations/MutationTesting.cs` である。
振る舞いは `tests/Mutation.Tests/verifying/TestingOrchestratorFacts.cs`、`TestingOrchestratorRowFacts.cs`、`DetectionRowsFacts.cs`、`MutationTestingBaselineFacts.cs`、`tests/e2e/medium/detection-matrix/verify-detection-matrix.sh` が固定する。

baseline は変異を全て不活性にした assembly でテストを一度実行する。
テストごとに、結果、所要時間、被覆した変異、probe の呼び出し回数を集め、`verifying/infrastructure/workers/BaselineMapping.cs` が `BaselineProfile` へ写す。
baseline の被覆収集は、テスト数と `--concurrency` に応じて最大 `テスト数 / 8` の worker に分担できる。
`BaselineExecution.cs` が分担し、テストが一件も見つからなければ `PipelineFailure.NoTestsFound` で対象を中断する。
baseline で失敗したテストは以後の選択から除外する。
複数 worker の観測の集計と不正な発見応答の拒否は、`tests/Mutation.Tests/verifying/BaselineExecutionFacts.cs` が実 process の worker を使って固定する。
同じ検査が、テストを発見した worker が最初の分担を受け持つことと、分担を終えた worker へ終了の指示を送ることも固定する。

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
振る舞いは `tests/Mutation.Tests/verifying/MutantSchedulerFacts.cs` と `VerifyMutantsFacts.cs` が固定する。

共有 worker が活性の変異の枝へ一度も到達しなかったことを `RunCompleted.Reached == false` で報告した場合、`TestingOrchestrator.cs` は生存・検出のどちらも新規 process で再判定する。
この条件で検出を返した共有 worker は破棄し、次の共有実行の直前に新しい worker を起動する。
振る舞いは `tests/Mutation.Tests/verifying/TestingOrchestratorFacts.cs` が固定する。
この検査は、本物の `WorkerClient` が `tests/Mutation.ScriptedWorker/` の実 process と往復し、判定・`WorkerRestarts`・worker process の残り方で確かめる。

無限走行は probe の呼び出し回数の上限で即断する。
上限は `verifying/domain/services/HitLimits.cs` が、実行するテストの baseline の probe 回数の最大値に 200 を掛けて 100,000 を足した値にする。
上限を超えると、注入クラスが目印付きの例外を投げて worker が `hitlimit` を返し、判定は `TimedOut` になる。
probe を通らないループと、例外を握り潰す包括的な catch は、実行時間の予算に応答の猶予 5 秒(`RunDeadlines.cs`)を足した期限と worker の終了で捕らえる。
`HitLimits` の値は `tests/Mutation.Tests/verifying/VerdictVocabularyFacts.cs` が固定する。

判定は `verifying/infrastructure/workers/VerdictInterpretation.cs` が決める。
baseline で成功したテストが失敗すれば `Killed`、全て成功すれば `Survived` になる。`Survived` にするのは worker が `survived` を返したときだけで、`killed`、`survived`、`hitlimit` 以外の結末の値は `UnjudgedCause.RunnerFailure` の未判定にする。
応答が無く worker が終了していれば `Killed` で、検出の経緯は `(process crash)` とする。
worker が `Failed` を返した場合、応答が無く期限切れでも取り消しでもなく worker も生きている場合、共有 worker と隔離 process が init に失敗した場合も、`UnjudgedCause.RunnerFailure` の未判定にする。
この未判定は検出にも未検出にも数えず、`Pending` として報告し、`unjudgedBy.runnerFailure` に数える。
取り消しを受けた後の結果は、どの場合も採らない。
期限切れで止めた worker は、終了済みでも `Killed` でなく `TimedOut` として扱う(`WorkerExchange.DeadlineExpired`)。
probe 上限の超過による `TimedOut` は時間によらないので、そのまま検出に数える。
`Killed` と `TimedOut` は score の検出に数えられるので、worker process が mutant の実行中に落ちた場合も検出に入る。
Microsoft.Testing.Platform の run は killer test の名前を返さないため、`TerminalOutput.cs` が端末出力の `failed <名前> (<所要時間>)` の行から取り出し、取り出せなければ `(unknown test)` とする。
`tests/Mutation.Tests/verifying/VerdictInterpretationFacts.cs`、`TestingOrchestratorStartFailureFacts.cs` が固定する。

期限切れの `TimedOut` は、競合で遅れただけの変異を検出に数えないよう、`TimeoutAdjudication.cs` が確かめ直す。
期限は基準実行の所要時間から決めた固定の値なので、機が基準より遅ければ無限走行でない変異も超えるためである。
超えた変異ごとに、変異のない同じテスト列を新規 process で走らせる対照の実行(`IsolationRuns.ControlAsync`)の所要時間が、その変異の予算に収まるかを `TimeoutJudgement.cs` が判定する。
収まらないか終わらなければ、対照のテストさえ予算の外なので、`UnjudgedCause.ContendedTimeout` の未判定にし、検出にも未検出にも数えない。
収まれば新規 process で変異をもう一度実行し、終われば再実行の判定を採り、また期限を超えたときだけ検出に数える `TimedOut` にする。
Stryker.NET、StrykerJS、PIT は倍率と定数の固定の期限で超過をそのまま timeout とし、忙しい機への手当ては定数を人が大きくすることだけなので、この確かめ直しは持たない。
費用は、期限を超えた変異ごとの新規 process 二本と再実行一回である。
振る舞いは `tests/Mutation.Tests/verifying/TestingOrchestratorTimeoutFacts.cs` と `TimeoutJudgementFacts.cs` が固定する。
競合で判定できなかった変異の所要時間には、対照の実行に費やした時間が入る。

## 取り消し

`run` は `src/Mutation.Cli/commands/TerminationSignals.cs` で SIGHUP、SIGINT、SIGTERM を受け、最初の一回を取り消しの要求(`CancellationToken`)に変える。
二度目の要求は既定の動作へ譲り、process を強制的に終える。
要求は `RunMutationTesting.ExecuteAsync` から `TargetRun`、`VerifyMutants`、`IWorkerRuns` の `ExecutionControl` を通って、`TestingOrchestrator` と `WorkerClient` に届く。
`WorkerClient.SendAsync` は、応答を待つ間に要求を受けると worker process を殺して返す。
`TestingOrchestrator` は、要求の後に新しい変異へ取りかからず、実行中だった変異の結果を採らない。
その結果を採ると、止めるために殺した worker の終了が `(process crash)` の検出になるためである。
実行中だった変異は `UnjudgedCause.Interrupted`、取りかからなかった変異は `UnjudgedCause.NotRun` の未判定になり、`RunTally.Abandon` が記録する。
`VerifyMutants` は確定した判定と未判定を保存に畳み、`RunMutationTesting` は止まった走行でも報告を書き出す。
取り消しの要求を受けた後に始まる対象は、`PipelineFailure.Interrupted` で判定に至らなかった対象として記録する。
baseline は worker を止めて失敗として返し、`TargetRun` が `Interrupted` に直す。
build と変異の生成は要求を受けても終えるまで走り、その後の段階の前で止まる。
終了コードは `RunCommand.cs` が決め、取り消しで止まった走行は信号に対応する 129(SIGHUP)、130(SIGINT)、143(SIGTERM)である。
SIGKILL は受け取れないので、止まった走行の判定は追記の記録(継承の節)だけが残す。
振る舞いは `tests/Mutation.Tests/verifying/TestingOrchestratorCancellationFacts.cs`、`TestingOrchestratorStartFailureFacts.cs`、`RunMutationTestingCancellationFacts.cs`、`BaselineExecutionFacts.cs` が固定し、信号を受けた走行全体は `tests/e2e/medium/interruption/verify-interruption.sh` が固定する。

## 継承

保存は、継承を求めない走行も含めて、全ての走行が対象ごとに `work/<対象名>/snapshot.json` へ書く。
`--with-baseline` を指定すると、次の実行がそれを再利用する。
保存と再利用の所在は `verifying/infrastructure/snapshot/` にある。
再利用には、全体の再構築と変異ごとの継承の二つの経路があり、どちらも、入力の閉包の同一性が前回と一致することを条件にする。

閉包の同一性は `mutating/infrastructure/fingerprint/CompilationFingerprint.cs` が、`InputClosure.cs` の記述から計算し、`ReuseIdentity` として返す。
閉包には、csc 引数の文字列と、ソース、additional file、embed する file、analyzer config、参照する assembly、analyzer と generator、resource、署名の鍵の内容を含める。
テスト assembly の directory の全 file の内容(`TestResults` の下を除く)、engine と worker と compiler の assembly の内容も含める。
さらに runtime の版と識別子、OS の版、環境変数の名前と値、作業 directory、設定の要約を含める。
環境変数は hash にだけ使い、値を平文で保存しない。
読めない入力が一つでもあれば同一性は不在になり、その走行は何も継承せず、保存も継承の根拠を持たない。
全体の指紋は、閉包に、対象のソースごとの変わった行の範囲と、`--since` が選んだ候補の範囲の鍵を加える。
同一性は `PreparedTarget` から `VerificationRequest` と `SnapshotSaveRequest` を通り、`SnapshotDocument.ClosureIdentity` に保存される。
判定を確定できなかった変異が残る保存も、同一性を持つ。

全体の再構築は、build と対象の特定の後、変異の生成より前に判定する。
全体の指紋、テスト assembly の byte hash、保存した変異 assembly の byte hash が全て前回と一致すると、`SnapshotReconstruction.cs` が変異の生成、compile、baseline、テスト実行を全て省いて前回の判定を復元する。

全体が一致しないときは、baseline の後に `SnapshotReuse.cs` が変異ごとに判定を継承する。
継承する変異は、閉包の同一性とテスト assembly の byte hash が前回と一致する保存の中で、そのファイルの内容 hash、ファイル内の変異列の演算子と位置と置換、変異を被覆する通常のテストの集合が、前回と全て一致するものに限る。
継承するのは `Killed`、`Survived`、`TimedOut` である。
設定、他のファイル、参照する assembly、analyzer と generator、additional file、analyzer config、テストの補助 file のどれが変わっても閉包の同一性が変わるので、継承しない。
同一性を持たない前の版の保存も、継承しない。
判定の合成は、今回の `CompileError`、`NoCoverage`、`Ignored` を先に置き、継承した判定は今回の分類のない変異にだけ置き、実行した判定を最後に置く。
所在は `verifying/application/workflows/verify-mutants/VerdictMerge.cs` の `Merge` である。
継承が届くのは今回の実行計画に載った変異だけで、`VerifyMutants` は継承した件数を実行計画に載った変異の中で数える。
振る舞いは `tests/Mutation.Tests/verifying/VerifyMutantsFacts.cs`、`SnapshotReuseFacts.cs`、`SnapshotStoreFacts.cs`、`VerifyMutantsResumeFacts.cs`、`tests/Mutation.Tests/mutating/CompilationFingerprintFacts.cs`、`tests/e2e/medium/reuse/verify-reuse.sh` が固定する。
閉包が見ない入力は [limitations.md](limitations.md) にある。

判定の確定ごとに、`SnapshotJournal.cs` が保存に並べた `work/<対象名>/snapshot.verdicts.jsonl` へ一行ずつ追記する。
一行目は、実行の前に分かっている判定(即時確定と継承した判定)を持ち、これから実行する変異を未判定の `Pending` で置いた保存の全体である。
二行目からは、確定した判定の一件ずつである。
一行を書くたびに flush するので、process が殺されても書き終えた行は残る。
次の実行では `SnapshotStore.Load` が、この記録があればそれを優先して保存を復元する(`SnapshotJournalReplay.cs`)。
書き終える前に殺された最後の行は捨てる。
走行が終わると `VerifyMutants` が最終の判定を保存し(`IVerdictJournal.Commit`)、記録を取り除く。
`Pending` が残る保存は、生成入力の指紋と変異 assembly の hash を持たないので、全体の再構築に一致しない。
`Pending` が前回の結果として再利用されることはなく、確定した判定だけが変異ごとの継承で次の実行に残り、残りの変異だけが実行される。
`--detection-matrix` の実行は継承を使わないので、この記録から続きを実行することはできない。
振る舞いは `tests/Mutation.Tests/verifying/SnapshotJournalFacts.cs`、`SnapshotStoreFacts.cs`、`VerifyMutantsResumeFacts.cs` が固定する。

## 出力の配置

出力 directory の配置は `verifying/application/workflows/run-mutation-testing/RunLayout.cs` が一点で所有する。
次の表の path は `--output` で指定した出力 directory からの相対であり、リポジトリ直下の作業記録用 `work/` とは別である。

| 置き場 | 内容 |
|---|---|
| `work/` | 対象間で共有する build の中間物と binlog |
| `work/<対象名>/` | 対象ごとの `snapshot.json`、走行の間だけ残る追記の記録 `snapshot.verdicts.jsonl`、テスト実行体の成果物 |
| `mutated/<対象名>/` | 変異 assembly と、失敗時の `failed-sources/` |
| `since/work/<対象名>/`、`since/mutated/<対象名>/` | `--since` を指定した実行の保存と変異 assembly |
| `reports/` | 対象をまたいで一つの `mutation-report.json` と `timings.json` |

`--since` の実行は、全量の実行の保存と変異 assembly を上書きしないよう、`since/` の下に置く。
`--mutate`、`--ignore-operators`、`--ignore-methods` だけが違う実行は同じ置き場を共有するが、設定の要約が入力の閉包の同一性に入るので、これらを切り替えた実行は、前回の保存を継承も再構築もせず、保存を上書きする。

## 報告

`reports/mutation-report.json` は mutation-testing-elements の report schema の書式で、`verifying/infrastructure/reports/StrykerReport.cs` が書き出す。
変異の id は `<対象名>:<連番>` で修飾して対象をまたいで一意にする。
`--detection-matrix` の実行では、検出行列の行を持つ変異が、失敗した全テストの表示名を `killedBy` へ、実行を求めたテストを `coveredBy` へ、結果を観測したテストの数を `testsCompleted` へ書く。
未実行のテストが残った変異は `statusReason` でそのテストを名指しし、行列が完全でないことを示す。
失敗したテストを特定できない検出は、既定の実行と同じ目印を `killedBy` に残す。
行を持たない変異と既定の実行の報告には、これらの欄を書かない。
形式は `tests/Mutation.Tests/verifying/StrykerReportFacts.cs` が固定する。
`reports/timings.json` は `TimingsReport.cs` が書き出し、`phases`、`counters`、`targets`、`mutants` を持つ。
`phases` は build、mutate、compile、baseline、testing、total の段階別時間、`counters` は変異数、テスト数、score、worker の再起動回数、判定別件数、`targets` は対象別の内訳と中断、`mutants` は変異別の所要時間である。
変異別の記録が持つ実行数、所要時間、隔離の実行だったかは、`tests/Mutation.Tests/verifying/TestingOrchestratorTimingFacts.cs` が固定する。
`mutation-report.json` と `timings.json` は、走行が全変異の判定を確定したかを `RunStateReport.cs` が組み立てる `run` に書く。
`state` は `MutationRunResult.State` で、全件が確定していれば `completed`、取り消しで止まっていれば `interrupted`、取り消し以外の失敗で判定に至らなかった対象があれば `failed`、競合で判定できなかった変異か test runner の失敗で判定できなかった変異が残れば `unjudged` である(この順に優先する)。
判定に至らなかった対象は `run.failedTargets`(取り消しで止まった対象は `run.interruptedTargets`)に名前を残し、変異の未判定がなくても `complete` は偽である。
判定を確定できなかった変異は `MutantVerdict.Unjudged` で表し、報告の status は `Pending`、`statusReason` に理由を書く。
検出にも未検出にも数えないので score の母数に入らず、`changed-lines` の判定は、未検出がなく未判定が残るときに合格と言わず失敗として返す。
形式は `tests/Mutation.Tests/verifying/RunStateReportFacts.cs` が固定する。
`--since` の実行では、`run.selection` が対象ごとに、選んだ範囲、理由、未分析の関係、入力でないと判断した file を持つ。
`StrykerReport.Parse` は、`run.selection[].unanalysed` を読み戻した報告の `Unanalysed` に取り出し、`changed-lines` が判定の後に出力する。

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
| `tests/Mutation.ScriptedWorker/` | 台本どおりに応える worker。共有 worker と隔離 process の応答を決め、応答の前に待たせることも、応えないことも、init に失敗することも、応答の後に出力を書くこともできる。`TestingOrchestrator` の各検査と `BaselineExecutionFacts.cs` が使う |
| `tests/e2e/medium/` | 判定カテゴリ、三つのテスト基盤、変更行の判定、検出行列、SIGTERM と SIGKILL で止めた走行を実 process で検証する fixture と script |
| `tests/performance/` | 合成対象と実 project への測定 script |

## 保守の注意

- 注入クラスの正本は `MutantSwitchSource.Template` である。
  変えたら、worker の `SwitchAccessor` の束縛を揃える。
- xunit v2 の message sink は `SynchronousMessageReporting` を有効にして、被覆の drain の順序を保証している。
  所在は `src/Mutation.Worker/xunit/XunitTestHost.cs` である。
- Linux の binlog では csc 実行 file の絶対 path が `/` で始まるため、csc 引数の option 判定は `BinlogCompilations.IsOptionLike` に集約している。
- `TestingOrchestrator.ExecuteAsync` は呼び出しごとに独立した集計を返し、survivor の再検証の合成は `OrchestrationResult.Overlaid` が行う。
