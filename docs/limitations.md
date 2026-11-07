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
- VSTest adapter ホストは実行のたびに executor を作り直すので、共有ホストの利点は xunit v2 と Microsoft.Testing.Platform より小さい見込みである。
  現在の値は測定していない。
- probe を通らないループと、hit limit の例外を握り潰す包括的な catch は、probe の上限で捕らえられない。
  実行時間の予算に応答の猶予を足した期限と、worker の終了で捕らえる。
- probe の記録は、同期を持たない共有の配列と計数器に書く。
  テストが複数の thread を使う場合の被覆と probe 回数の帰属が正確かは、確かめていない。
  所在は `Mutation/mutating/infrastructure/roslyn/MutantSwitchSource.cs` の `Template` である。
- worker への各指示には応答の期限があり、期限を超えると worker process を終了する。
  build と git の差分取得を含む実行全体を包む、取り消しと期限の契約は確認できない。
  所在は `Mutation/verifying/infrastructure/workers/WorkerClient.cs` である。
- worker の異常終了は `Killed`、worker の `Failed` 応答と応答の期限切れは `TimedOut` に分類され、どちらも score の検出に数える。
  件数だけでは、テストが変異を検出した場合と区別できない。
  所在は `Mutation/verifying/infrastructure/workers/VerdictInterpretation.cs` と `Mutation/verifying/domain/aggregates/run-result/TargetResult.cs` である。
- `--exclude-static` が外す変異は、static 初期化でしか実行されない変異より広い。
  構文上 static の文脈にある変異は、通常の被覆テストがあっても外す。
  所在は `Mutation/verifying/domain/services/MutantScheduler.cs` の `Plan` である。

## 判定と継承

- 型付けの事前検査を通った変異でも emit に失敗することがあり、その変異は rollback で無効化されて `CompileError` になる。
  README の「mostly excluded」は、事前検査で減らすことを指し、`CompileError` が発生しない保証ではない。
- `--with-baseline` の再利用で照合する入力は、全体の再構築と変異ごとの継承で違う。
  全体の再構築は、csc 引数の文字列、csc が読む全ソースの内容、設定、テスト assembly、変異 assembly を照合する。
  変異ごとの継承は、そのファイルの内容、変異列、テスト assembly、被覆するテストの集合だけを照合し、設定、他のファイル、参照する assembly の内容を照合しない。
  所在は `Mutation/mutating/infrastructure/fingerprint/CompilationFingerprint.cs` と `Mutation/verifying/infrastructure/snapshot/SnapshotReuse.cs` である。
- 参照する assembly、analyzer、generator、`AdditionalFiles`、analyzer config、テストの補助 file の内容は、どちらの経路でも hash しない。
  path が同じまま内容だけが変わり、テスト assembly の byte が変わらないときに、再検査を強制する仕組みはない。
  これは source の読取りから導いた推論で、実行で確かめていない。
- 変異ごとの継承では、あるファイルの変異は、そのファイルと変異列とテスト assembly と被覆するテストの集合が前回と同じなら、同じ対象の別のファイルの変更があっても前回の判定を継承する。
- 継承した判定は、今回の `CompileError`、`NoCoverage`、`Ignored` の判定を上書きし得る。
  `--validate-survivors` は、継承した `Survived` を再検証しない。
  どちらも source の読取りから導いた推論で、実行で確かめていない。
  所在は `Mutation/verifying/application/workflows/verify-mutants/VerifyMutants.cs` の `MergedVerdicts` と `RevalidateSurvivorsAsync` である。
- 全量の実行の保存は、`--mutate`、`--ignore-operators`、`--ignore-methods` だけが違う実行の間で共有される。
  これらを切り替えると全体の再構築は起きなくなり、変異ごとの継承は設定を照合しないので、前回の判定を継承し得る。
  所在は `Mutation/verifying/application/workflows/run-mutation-testing/RunLayout.cs` である。
