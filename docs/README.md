# 文書の案内

この directory は、mutation-dotnet の現在の実装を保守者向けに投影した、追跡対象の文書を置く。
正本は source、test、設定であり、各文書は正本の所在を示し、対応する変更と同じ変更で更新する。
利用者向けの導入と option は、リポジトリ直下の README.md と README.ja.md にある。
ブランチ、commit、検証、release の手順は、CONTRIBUTING.md にある。
計画、調査、実験、下書き、作業台帳は、追跡されない `work/` に置く。
`work/` は各 checkout に任意で存在する一時的な場所であり、この directory の文書は `work/` の内容に依存しない。
読み手がいなくなった文書は、必要な情報を source、test、commit の件名、実装コメントの Why not、この directory の文書へ移してから取り除く。

| 文書 | 読み手と場面 | 内容 | 読み手がいなくなる条件 |
|---|---|---|---|
| [architecture.md](architecture.md) | engine を変更する保守者が、変更の前に理解する | 実行の流れ、変異の生成、検査の実行、継承、出力の配置、構造と依存方向。source と test の所在つき | engine が存在しなくなったとき |
| [limitations.md](limitations.md) | 利用者と保守者が、結果を読むときと変更の前に確かめる | 現在の実装に残る制約と、実測していない項目 | 各項目は解消した変更で取り除き、全項目が無くなったとき |
| [performance.md](performance.md) | 実行時間を測る保守者が、測定の作業中に参照する | 測定 script の手順、出力の場所、合成対象の設計 | `tests/performance/` が無くなったとき |
| [exceptions.md](exceptions.md) | 標準との差を確かめる監査者と保守者 | 標準からの逸脱、理由、撤回条件 | 全ての撤回条件が成立したとき |
| [conformance-baseline.json](conformance-baseline.json) | 標準への適合を監査する人 | 既存違反と抑止の対応 | 全ての違反を解消したとき |

conformance-baseline.json は、2026-10-01に記録された抑止の台帳であり、今回の文書整理による新たな適合検査の結果ではない。
記録された revision や過去の検査結果を、現行標準への適合の証明として扱わない。
