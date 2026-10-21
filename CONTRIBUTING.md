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

検証は時間の予算で三段に分け、リポジトリの root で実行する。

| 入口 | 予算 | 実行する時点 | 中身 |
| --- | --- | --- | --- |
| `devenv shell verify` | 2 分 | commit の前、agent が作業を止める前 | 警告 0 の build(S3776 の認知的複雑度 15 を含む)とユニットテスト |
| `devenv shell verify-push` | 15 分 | push の前(作業機の pre-push hook が起動) | `verify`、E2E 検証、push の基点から変わった行の mutation。変わった行の未検出 mutant が 0 件であること |
| `devenv shell verify-full` | なし | release の準備の前 | E2E 検証と全量の mutation。gate にせず、前回から増えた失敗と未検出 mutant を `work/backlog.md` へ追記する |

予算を超えた段は失敗として扱い、より安い検証へ置き換えるか後の段へ移す。
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
`work/` は各 checkout に任意で存在する一時的な場所で、`devenv shell verify-full` が追記する `work/backlog.md` を含む。
追跡する文書は `work/` の内容に依存しない。
作業が終わったら、保守に必要な意味を移してから、不要になった作業記録を取り除く。

移す先は、現在の実現方法ならコード、保証する振る舞いならテスト、変更の理由なら commit の件名、採らなかった理由なら判断を所有するコードの近くの実装コメント、読者向けの説明なら `docs/` である。
独立した決定の記録の文書は作らない。

文書の日本語は、段落と箇条書きを句点で終わる完結した文で書き、一つの文を一行に置く。
commit の件名、実装コメント、ドキュメントコメントの日本語は、句点も読点も使わない体言止めで書く。
見出しと表のセルは、文の規律の対象にしない。

## リリース

1. `devenv shell verify-full` を実行し、`work/backlog.md` に追記された項目を確認する。
2. `develop` で `CHANGELOG.md` に該当バージョンの節を追記する(セマンティックバージョニング)。
3. `chore: X.Y.Z のリリースを準備` でコミットし `develop` へマージする。
4. `main` を該当コミットへ進め、`vX.Y.Z` タグを付ける。

registry への配布は行わない。
利用側は checkout したリポジトリのリリースタグを参照する。
