# 制約

この文書は、利用者と保守者が結果の読み方を誤らないように、現在の実装に残る制約を示す。
README の制約事項に加えて、source から確かめられるものを置く。
各項目は対応する source の所在を持ち、制約を解消する変更と同じ変更で取り除く。
実測していない項目と、source の読取りから導いた推論の項目は、その旨を文中に示す。
以下の path は、特記がなければ `src/` からの相対である。

## 対象の特定

- テスト assembly の実行時 path は、binlog の出力 path の `obj` の directory を `bin` に置き換えて得る。
  出力 path に `obj` の directory が無いときは、置き換えずに出力 path をそのまま使う。
  実行時の配置が `bin/` の標準の配置と食い違うテスト project は、解決できない可能性がある。
  所在は `Mutation/mutating/infrastructure/msbuild/TargetLocation.cs` の `ToBinPath` である。
- 同じ project の csc 呼び出しが binlog に複数あるとき、たとえば複数の target framework を build するときは、最後の一回だけを対象にする。
  全ての target framework を検査する保証はない。
  所在は `Mutation/mutating/infrastructure/msbuild/BinlogCompilations.cs` の `Read` である。
- 対象の source generator を再実行するときに、analyzer config の option を渡していない。
  build property や `.editorconfig` の値に依存する生成結果は、実際の build と一致する保証がない。
  所在は `Mutation/mutating/infrastructure/roslyn/GeneratorExecution.cs` である。
- worker は `Microsoft.NETCore.App` だけを framework として持つ。
  ASP.NET Core のような別の shared framework を参照するテスト assembly が動くかは、実測していない。
  所在は `Mutation.Worker/Mutation.Worker.csproj` である。
- worker process の `TRUSTED_PLATFORM_ASSEMBLIES` は、worker の依存だけを列挙し、テスト assembly の依存を含まない。
  この値からメタデータ参照を作るテストは、通常の実行と違う参照の集合を得る。
  この差で、Roslyn を参照する source generator を test 内でコンパイルするテストが baseline で失敗することを、自己 mutation の実行で確かめた。
  所在は `Mutation.Worker/Mutation.Worker.csproj` と `Mutation.Worker/hosts/HostEnvironment.cs` の `Prepare` である。
- 変異を一件も生成しなかった対象でも、baseline のテストを全件実行する。
  所在は `Mutation/verifying/application/workflows/run-mutation-testing/TargetRun.cs` の `MutateAndTestAsync` である。

## テストの実行

- 既定の実行は共有の worker で、テスト間の static 状態の汚染を排除しない。
  活性の変異の枝への未到達を観測した生存・検出は新規 process で再判定するが、到達した場合の状態汚染は、この再判定の対象にならない。
  `--validate-survivors` を指定すると、今回生存した隔離でない変異を新規 process で再検証する。
- Microsoft.Testing.Platform の baseline は、テストごとに実行体の entry point を呼ぶ。
  そのため baseline の所要時間は、テスト数に比例する呼び出しの固定費を含む。
  所在は `Mutation.Worker/platform/PlatformTestHost.cs` の `Baseline` である。
- Microsoft.Testing.Platform の killer test の名前は、実行体の端末出力の `failed <名前> (<所要時間>)` の行から取り出す。
  この形で出さない基盤では、検出の経緯が `(unknown test)` になる。
  所在は `Mutation/verifying/infrastructure/workers/TerminalOutput.cs` である。
- 最初の失敗での打ち切りは、xunit v2、TUnit、xunit v3 にある。
  他の Microsoft.Testing.Platform 基盤には付けない。
  VSTest adapter は `Cancel` を使わないので、残りの被覆テストを走らせ切る。
  所在は `Mutation.Worker/platform/PlatformFlavor.cs` と `Mutation.Worker/vstest/VstestTestHost.cs` である。
- `--detection-matrix` の Microsoft.Testing.Platform の run は、テストごとに実行体の entry point を呼ぶ。
  そのため所要時間は、baseline と同じく、テスト数に比例する呼び出しの固定費を含む。
  probe の回数もテストごとに数え直すので、全 uid を一度に渡す既定の run と、hit limit に達する境目が違い得る。
  後者は source の読取りから導いた推論で、実行で確かめていない。
  所在は `Mutation.Worker/platform/PlatformTestHost.cs` の `RunEach` である。
- `--detection-matrix` の `killedBy` と `coveredBy` は、テストの一意識別子ではなく表示名を書く。
  表示名が重なるテストは、報告の上で区別できない。
  所在は `Mutation/verifying/infrastructure/reports/StrykerReport.cs` の `Describe` である。
- VSTest adapter ホストは実行のたびに executor を作り直すので、共有ホストの利点は xunit v2 と Microsoft.Testing.Platform より小さい見込みである。
  現在の値は測定していない。
- probe を通らないループと、hit limit の例外を握り潰す包括的な catch は、probe の上限で捕らえられない。
  実行時間の予算に応答の猶予を足した期限と、worker の終了で捕らえる。
- probe の記録は、同期を持たない共有の配列と計数器に書く。
  テストが複数の thread を使う場合の被覆と probe 回数の帰属が正確かは、確かめていない。
  所在は `Mutation/mutating/infrastructure/roslyn/MutantSwitchSource.cs` の `Template` である。
- worker への各指示には応答の期限があり、期限を超えるか取り消しの要求を受けると worker process を終了する。
  実行全体を包む期限は持たず、外側の期限は SIGTERM などの信号で与える。
  build と変異の生成は取り消しの要求を受けても終えるまで走り、その後で対象は判定に至らなかったものとして止まる。
  所在は `Mutation/verifying/infrastructure/workers/WorkerClient.cs` と `Mutation/verifying/application/workflows/run-mutation-testing/TargetRun.cs` である。
- worker process が mutant の実行中に落ちた場合は `Killed`(`(process crash)`)で、score の検出に数える。
  worker が `Failed` を返した場合、`killed`、`survived`、`hitlimit` 以外の結末の値、共有 worker と隔離 process の初期化の失敗、期限切れでも取り消しでもない応答の欠落は、`Killed` にも `TimedOut` にも `Survived` にも数えず、`UnjudgedCause.RunnerFailure` の未判定(報告の `Pending`、`unjudgedBy.runnerFailure`)にして score の母数に入れない。
  worker が落ちた原因を、mutant によるものと環境によるもの(たとえば OOM killer)に区別する手段は持たないので、環境の理由で落ちた場合も `Killed` に数える。
  この区別は実測しておらず、source の読取りから導いた推論である。
  期限切れだけは、競合による遅れと区別して未判定にする。
  対照の実行が予算に収まり、再実行も期限を超えた場合に限って、期限切れを検出に数える。
  競合が一時で、対照の実行が収まった後に再び遅れた場合は、検出と区別できない。
  所在は `Mutation/verifying/infrastructure/workers/VerdictInterpretation.cs`、`TimeoutAdjudication.cs` と `Mutation/verifying/domain/aggregates/run-result/TargetResult.cs` である。
- `--exclude-static` が外す変異は、static 初期化でしか実行されない変異より広い。
  構文上 static の文脈にある変異は、通常の被覆テストがあっても外す。
  所在は `Mutation/verifying/domain/services/MutantScheduler.cs` の `Plan` である。

## 判定と継承

- 型付けの事前検査を通った変異でも emit に失敗することがあり、その変異は rollback で無効化されて `CompileError` になる。
  README の「mostly excluded」は、事前検査で減らすことを指し、`CompileError` が発生しない保証ではない。
- `--with-baseline` の再利用は、入力の閉包の同一性が前回と一致するときだけ許す。
  全体の再構築は、閉包の同一性に、対象のソースごとの変わった行の範囲と `--since` が選んだ候補の範囲を加えた指紋と、テスト assembly と変異 assembly の hash を照合する。
  変異ごとの継承は、閉包の同一性、テスト assembly の hash、そのファイルの内容、変異列、被覆するテストの集合を照合する。
  閉包は、csc 引数、ソース、additional file、embed する file、analyzer config、参照する assembly、analyzer と generator、resource、署名の鍵、test 出力の全 file、engine・worker・compiler の assembly、runtime と OS の版、環境変数、作業 directory、設定である。file は内容、環境変数は名前と値を hash に含める。
  読めない入力が一つでもあれば同一性を示せないので、何も継承せず、全体の再構築もしない。
  同一性を持たない前の版の保存は、継承の根拠にしない。
  所在は `Mutation/mutating/infrastructure/fingerprint/CompilationFingerprint.cs`、`InputClosure.cs` と `Mutation/verifying/infrastructure/snapshot/SnapshotReuse.cs` である。
- 閉包に入れない入力がある。
  テスト assembly の directory と csc の引数が指す file の外にある file、network、時刻、SDK の props と targets のうち csc の引数と bin の内容に現れないものである。
  `--with-baseline` は、これらの外部状態から判定が独立しているテストに限って使う。実行時に必要な file は MSBuild の `CopyToOutputDirectory` などで test 出力に含め、外部状態を読むテストでは `--with-baseline` を付けず毎回判定する。
  テストの実行体が成果物を書く `TestResults` directory の下は、実行のたびに変わるので閉包に入れない。
- テスト assembly の directory は、対象の assembly の複製も含めて hash する。
  deterministic でない compile は、build のたびに内容が変わり、同一性が一致しないので再利用できない。
  再検査は強制されるので判定は壊れないが、再利用の利点を失う。
  `tests/e2e/medium/reuse/verify-reuse.sh` の fixture では、同じ入力の再利用と、設定を変えた後の同じ入力の再利用を実行して確かめている。
- 同一性は、継承を求めない走行も求めて保存する。
  参照する assembly とテストの directory の hash は、build の後の走行ごとに読むので、大きな directory では時間がかかる。
  所要時間は測定していない。
- 設定の要約(`--mutate`、`--ignore-operators`、`--ignore-methods`、`--exclude-static`、`--validate-survivors`、`--since` の基点と `--changed-lines`)は閉包に入る。
  これらを切り替えた走行は、同じ出力先の保存を継承せず、保存を上書きする。
  `--validate-survivors` を求めなかった走行の `Survived` を、求めた走行が継承することはない。
- 今回の `CompileError`、`NoCoverage`、`Ignored` の分類は、継承した判定より常に優先する。
  継承が届くのは、今回の実行計画に載った変異だけである。
  所在は `Mutation/verifying/application/workflows/verify-mutants/VerdictMerge.cs` の `Merge` と `VerifyMutants.cs` である。
- 判定の確定ごとの追記の記録は、`--validate-survivors` の再検証の結果と `--detection-matrix` の行列の行を持たない。
  `--detection-matrix` の実行は継承を使わないので、止まった行列の実行を続きから実行することはできない。
  所在は `Mutation/verifying/infrastructure/snapshot/SnapshotJournal.cs` と `Mutation/composition/operations/MutationTesting.cs` である。

## 変更による選別

- `--since` は、変わったファイルを対象とテストと参照する project の build graph に照らして分類し、対象へ届く変更があれば対象の production の全範囲を候補にする。
  変えたソースの未変更の呼び出し元と、変えたテストが検出する production は、file より細かく選べないので、これらの関係では全範囲が最小である。
  `--changed-lines` はこの広げ方を行わず、届く関係を未分析として報告に残す。
  所在は `Mutation/mutating/domain/services/ChangeImpact.cs` である。
- project 参照は StructuredLogger の `ProjectReferenceGraph` を使い、MSBuild の評価に記録された `ProjectReference` を推移的に辿る。
  評価後の target が追加した参照など、graph に現れない関係は project 参照として分類できない。ただし、関係が不明な file の変更を影響なしにはせず、対象全体へ広げる。
  所在は `Mutation/mutating/infrastructure/msbuild/TargetGraphs.cs` である。
- 増分 build は、変更のない参照 project の compile を省き、その csc の呼び出しが binlog に残らない。
  参照先 project の compiler 呼出しが binlog に無いときは、`--no-incremental` で再 build して入力の回収を試みる。
  なお回収できない project の source は関係不明として全範囲へ広げる。再 build の追加時間は測定していない。
  所在は `Mutation/mutating/application/usecases/PrepareTarget.cs` の `NeedsRebuild` である。
- 複数の target framework を build するとき、csc の呼び出しは最後の一回だけを使うので、分類もそのソースの一覧だけを見る。
  他の framework でだけ compile されるソースも、関係不明なら対象全体を選ぶが、生成する mutant は最後の compilation の範囲に限られる。
- project の外にある import や fixture は、compiler 入力に直接現れない場合がある。
  名前や配置で「入力ではない」と推測せず、関係不明の変更を全範囲へ広げる。このため文書だけの変更でも全範囲を分析しうる。
  `bin` と `obj` の下の file は、既知の compiler 入力なら先にその関係を採り、それ以外の build 成果物だけを除外する。
- 消えたファイルの変更前の参照は追わず、build 成果物以外の削除は、場所や拡張子によらず全範囲を選ぶ。
- 候補が空でも、build と baseline のテストは全件実行する。
- 分類の結果は、報告の `run.selection` と console 要約に残る。
  `changed-lines` が出力する未分析の記述は、報告の `run.selection[].unanalysed` を読むので、この項を持たない前の版の報告では出力しない。
