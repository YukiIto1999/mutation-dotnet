# CONTRIBUTING

## 開発環境

devenv が .NET SDK と検証入口を管理する。
作業前にリポジトリの root で `devenv shell` を実行する。

## ブランチ

統合ブランチは `develop`、リリースブランチは `main` である。
変更は `develop` から枝を切り、作業後に `develop` へ戻す。
`main` への直接コミットはしない。

ブランチ名は commit の型と同じ prefix を付ける。

| prefix      | 用途                         |
| ----------- | ---------------------------- |
| `feat/`     | 機能追加                     |
| `fix/`      | バグ修正                     |
| `refactor/` | 挙動を変えない構造改善       |
| `docs/`     | ドキュメント                 |
| `test/`     | テスト                       |
| `style/`    | 挙動に影響しない表記の統一   |
| `chore/`    | 雑務・依存更新・リリース準備 |
| `ci/`       | CI 設定                      |

## コミット

コミットは `型: 要約` の一行で書き、型はブランチの prefix に揃える。
要約は変更の目的を日本語の体言止めで書き、本文は付けない。
要約の書き方は、README が示す architecture-standard の `principles/documentation/commit-purpose.md` と `principles/documentation/sentence-endings.md` に従う。
一つのコミットには一つの関心のみを含め、無関係な変更は分ける。
`Co-authored-by` などの自動生成痕跡は残さない(`commit-msg` フックが拒否する)。

## マージ

フックはマージコミットにも一行の `型: 要約` を求めるため、`develop` へは `--no-ff` でマージし、マージコミットは `chore: <作業名>の枝を統合` と書く。
作業ブランチはマージ後に削除する。

## 検証

検証は三段に分け、リポジトリの root で実行する。
`verify` と `verify-push` は、時間の目標(`feedbackTargetSeconds`)と一走行の上限(`seconds`)を [`tiergate.json`](tiergate.json) に分けて宣言する。目標を宣言していない入口は上限だけを持つ。

| 入口 | 目標と上限 | 実行する時点 | 中身 |
| --- | --- | --- | --- |
| `devenv shell verify` | 目標 2 分、上限 11 分 30 秒 | commit の前、agent が作業を止める前 | 警告 0 の build(S3776 の認知的複雑度 15 を含む)とユニットテスト |
| `devenv shell verify-push` | 上限 15 分 | push の前(作業機の pre-push hook が起動) | `verify` と E2E 検証。mutation は実行せず、mutation の結果で合否を決めない |
| `devenv shell verify-full` | 上限 46 分 | release の準備の前 | `regression-full` の後に `mutation-select` を別の走行で実行する。gate にせず、終了 status は `regression-full` のもの |
| `devenv shell regression-full` | 上限 15 分 | `verify-full` の一段として、または単独で | 全量回帰。`verify` と E2E 検証を影響範囲で絞らず全て実行する。終了 status と失敗した段を `.mutation-output/regression-full/` へ残し、失敗した段を `work/backlog.md` へ追記する |
| `devenv shell mutation-select` | 上限 30 分 | `verify-full` の一段として、または単独で | 選択型の mutation の分析。比較元から変わった `src/Mutation` の行に重なる mutant を engine の `--since --changed-lines` で選び、`--with-baseline` で前回の判定を継承し、`changed-lines` で判定する。比較元、候補、生成対象、未分析の範囲と生の終了 status を `.mutation-output/select/` へ、分析ごとの記録を `.mutation-output/select/runs/` へ残し、未検出 mutant と失敗を `work/backlog.md` へ追記する。全量回帰の結果は変えない |
| `devenv shell mutation-full` | なし | 全量の分析が必要な問いがある場合に明示して | 全量の mutation の分析。生の終了 status を `.mutation-output/full/exit-status` へ残し、前回から増えた失敗と未検出 mutant を `work/backlog.md` へ追記する。`verify-full` からは呼ばない |

mutation は検出力の分析であり、commit、push、release の合否を決めない。
`regression-full` と `mutation-select` は上限、取消、終了 status を共有しない別の走行で、`verify-full` は両者を順に実行して `regression-full` の終了 status で終わる。
T3 の各走行の上限と根拠は [`tiergate.json`](tiergate.json) の `regression-full`、`mutation-select`、`verify-full` に宣言する。
上限で止まった走行は未完了として、timeout の生の終了 status(124 か 137)を残す。

`mutation-select` の比較元は、前回完了した分析が `.mutation-output/select/analyzed-commit` に記録した commit である。
記録が無ければ upstream との merge-base(未 push の変更)、upstream も無ければ既定 branch まで遡らず、導入時点の HEAD を比較元にして同じ `analyzed-commit` へ記録する。
既定 branch まで遡ると、記録の無い初回に前回の分析からの変更ではなく分岐からの全変更を分析することになるため遡らない。
導入時点の HEAD より前の source は選択型の分析の対象外として、`selection.json` の `baseSource` と未分析の範囲に記録する。全量が要る問いには `mutation-full` を使う。
比較元と作業木の差分と未追跡 file のうち、内容が変わった `src/Mutation` の file の一覧は候補として記録するだけで、選択には使わない。
engine は依存先と逆依存から対象を選べない([#1](https://github.com/YukiIto1999/mutation-dotnet/issues/1))ので、変わった file の変わっていない行、生成対象の依存先と逆依存、削除した file の変更前の参照、`src/Mutation` の外の production、production でない変更入力の影響は、未分析の範囲として `selection.json` に記録する。
差分を取得できない場合は不成立(`diff-failed`)、変更が無い場合は変更なし(`no-change`)、候補はあるが変異できる箇所が無い場合は `no-mutant`、上限で止まった場合は未完了(`incomplete`)として区別する。
未検出 mutant は分析の結果で、終了 status を変えず、`selection.json` と `work/backlog.md` に残す。
生成対象は engine の `--since <比較元> --changed-lines` が選ぶ、変わった行に重なる mutant で、変わった file の一覧(`candidates`)は参考の記録として分けて残す。
変わった行の未検出は `changed-lines --report <報告> --since <比較元>` の判定で決め、その生の終了 status(未検出があれば 2)を残し、報告か差分を得られない 1 と上限切れは分析の不成立と未完了として区別する。
分析ごとに `.mutation-output/select/runs/<UTC 時刻>-<HEAD>/` を作って上書きせずに残し、比較元と HEAD を持つ `selection.json`、生の報告(`reports/`)、`changed-lines` の出力と終了 status、未検出 mutant ごとの記録 `undetected.json` を置く。
`undetected.json` は mutant ごとに、`changed-lines` が未検出として出したか(`onChangedLine`、変更行の gate があれば止めていた push か)と、後から書く原因(`cause`、architecture-standard の `structure/tests/methods.md` の原因表)、根拠(`causeEvidence`)、重大度(`severity`)の欄を持つ。
分析が完了すると HEAD を次回の比較元として記録し、不成立と未完了の場合は記録を進めない。
ただし導入時点の HEAD は、走行が完了しなくても次の走行が同じ commit から測るよう、走行の前に記録する。
選択の結果は全量の分析ではなく、`selection.json` の `scope` は `selected` である。

目標を超えた走行は取り消さず、超過が続く段は、検証をより安い検証へ置き換えるか後の段へ移す。
上限に達した走行は取り消され、合格として扱わない。取り消された走行が進行中だった場合は、検査の失敗として直さず、その経過時間を下限に上限を決め直す。
S3776 の既存違反は、`docs/conformance-baseline.json` の基線台帳に記録した member だけを、台帳の id を Justification に書いた `SuppressMessage` で抑止する。
台帳にない member へ抑止を加えない。

CI は置いていない。
置く場合は `verify` だけを実行する。
commit の規約と secret の漏洩の検査は、作業機の global hook(commit-msg、pre-commit)が担う。

## 文書

追跡する文書は、README(英語)、README.ja(日本語)、CHANGELOG(英語)、CONTRIBUTING、LICENSE、`docs/` である。
README は英語を正本とし、README.ja は同じ変更で正本に合わせる控えとする。

`docs/` は、保守者向けに現在の source と test を投影した日本語の文書を置く。
入口は `docs/README.md` で、アーキテクチャ、制約、性能の測定手順、標準からの逸脱、標準への適合の基線台帳を持つ。
`docs/` の文書は、対応する source や test を変える変更と同じ変更で更新する。
読み手がいなくなった文書は、必要な情報を移してから取り除く。

計画、調査、実験、下書き、作業台帳は、追跡されない `work/` に置く。
`work/` は各 checkout に任意で存在する一時的な場所で、`devenv shell verify-full`(`regression-full` と `mutation-select`)と `mutation-full` が追記する `work/backlog.md` を含む。
追跡する文書は `work/` の内容に依存しない。
作業が終わったら、保守に必要な意味を移してから、不要になった作業記録を取り除く。

移す先は、現在の実現方法ならコード、保証する振る舞いならテスト、変更の理由なら commit の件名、採らなかった理由なら判断を所有するコードの近くの実装コメント、読者向けの説明なら `docs/` である。
独立した決定の記録の文書は作らない。

文書の日本語は、段落と箇条書きを句点で終わる完結した文で書き、一つの文を一行に置く。
commit の件名、実装コメント、ドキュメントコメントの日本語は、句点も読点も使わない体言止めで書く。
見出しと表のセルは、文の規律の対象にしない。

## リリース

1. `devenv shell verify-full` を実行し、全量回帰の終了 status と、`work/backlog.md` に追記された項目を確認する。
2. `develop` で `CHANGELOG.md` に該当バージョンの節を追記する(セマンティックバージョニング)。
3. `chore: X.Y.Z のリリースを準備` でコミットし `develop` へマージする。
4. `main` を該当コミットへ進め、`vX.Y.Z` タグを付ける。

registry への配布は行わない。
利用側は checkout したリポジトリのリリースタグを参照する。
