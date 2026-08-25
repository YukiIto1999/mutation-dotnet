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

`型: 要約` の一行で書き、型はブランチの prefix に揃える。要約は変更内容が読み取れる日本語の体言止めにし、本文は付けない。一つのコミットには一つの関心のみを含め、無関係な変更は分ける。`Co-authored-by` などの自動生成痕跡は残さない(`commit-msg` フックが拒否する)。

## マージ

フックはマージコミットにも一行の `型: 要約` を求めるため、`develop` へは `--no-ff` でマージし、マージコミットは `chore: <作業名>の枝を統合` と書く。作業ブランチはマージ後に削除する。

## 検証

マージ前にリポジトリの root で `devenv shell verify` を通す。build が警告 0、全テストが緑、mutation-dotnet の mutation testing が基準を満たすことを条件にする。

## 文書

公開する文書は README(英語)・README.ja(日本語)・CHANGELOG(英語)・CONTRIBUTING・LICENSE に限る。設計メモと決定の記録は git 管理外の `docs/` に置く。公開文書の日本語は体言止めを基調にする。

## リリース

1. `develop` で `CHANGELOG.md` に該当バージョンの節を追記(セマンティックバージョニング)
2. `chore: X.Y.Z のリリースを準備` でコミットし `develop` へマージ
3. `main` を該当コミットへ進め、`vX.Y.Z` タグを付ける

registry への配布は行わない。利用側は checkout したリポジトリのリリースタグを参照する。
