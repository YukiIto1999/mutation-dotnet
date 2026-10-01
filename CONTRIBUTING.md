# CONTRIBUTING

## 開発環境

devenv が .NET SDK と検証入口を管理する。作業前にリポジトリの root で `devenv shell` を実行する。

## ブランチ

統合ブランチは `develop`、リリースブランチは `main`。変更は `develop` から枝を切り、作業後に `develop` へ戻す。`main` への直接コミットはしない。

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

`型: 要約` の一行で書き、型はブランチの prefix に揃える。要約は変更の目的を日本語の体言止めで書き、本文は付けない。要約の書き方は、README が示す architecture-standard の `principles/documentation/commit-purpose.md` と `principles/documentation/sentence-endings.md` に従う。一つのコミットには一つの関心のみを含め、無関係な変更は分ける。`Co-authored-by` などの自動生成痕跡は残さない(`commit-msg` フックが拒否する)。

## マージ

フックはマージコミットにも一行の `型: 要約` を求めるため、`develop` へは `--no-ff` でマージし、マージコミットは `chore: <作業名>の枝を統合` と書く。作業ブランチはマージ後に削除する。

## 検証

検証は時間の予算で三段に分け、リポジトリの root で実行する。

| 入口 | 予算 | 実行する時点 | 中身 |
| --- | --- | --- | --- |
| `devenv shell verify` | 2 分 | commit の前、agent が作業を止める前 | 警告 0 の build(S3776 の認知的複雑度 15 を含む)とユニットテスト |
| `devenv shell verify-push` | 15 分 | push の前(作業機の pre-push hook が起動) | `verify`、E2E 検証、push の基点から変わった行の mutation。変わった行の未検出 mutant が 0 件であること |
| `devenv shell verify-full` | なし | release の準備の前 | E2E 検証と全量の mutation。gate にせず、前回から増えた失敗と未検出 mutant を `docs/backlog.md` へ追記する |

予算を超えた段は失敗として扱い、より安い検証へ置き換えるか後の段へ移す。S3776 の既存違反は `docs/conformance-baseline.json` の基線台帳に記録した member だけを、台帳の id を Justification に書いた `SuppressMessage` で抑止する。台帳にない member へ抑止を加えない。

CI は置いていない。置く場合は `verify` だけを実行する。commit の規約と secret の漏洩の検査は、作業機の global hook(commit-msg、pre-commit)が担う。

## 文書

公開する文書は README(英語)・README.ja(日本語)・CHANGELOG(英語)・CONTRIBUTING・LICENSE に限る。設計メモと決定の記録は git 管理外の `docs/` に置く。公開文書の日本語は体言止めを基調にする。

## リリース

1. `devenv shell verify-full` を実行し、`docs/backlog.md` に追記された項目を確認する
2. `develop` で `CHANGELOG.md` に該当バージョンの節を追記(セマンティックバージョニング)
3. `chore: X.Y.Z のリリースを準備` でコミットし `develop` へマージ
4. `main` を該当コミットへ進め、`vX.Y.Z` タグを付ける

registry への配布は行わない。利用側は checkout したリポジトリのリリースタグを参照する。
