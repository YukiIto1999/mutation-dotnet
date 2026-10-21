# 標準からの逸脱

この文書は、現在確認できる architecture-standard との差を、保守者と監査者向けに記録する。
承認済みの逸脱や、網羅的な適合監査の結果を示すものではなく、採用経緯の根拠が確認できない項目はその旨を記す。
撤回条件が成立した項目は、取り除く。
個別の違反と抑止の対応は、[conformance-baseline.json](conformance-baseline.json) が持つ。
標準の規律は、標準の root にある現在の本文を基準にし、各項目はその file を相対 path で示す。

## task の正本を Nx に置かない

- 現在の事実として、`verify`、`verify-push`、`verify-full` は `devenv.nix` の `scripts` に定義している。
- 置き換える標準の規律は、`tools/build/nx.md` の「task の正本」である。
- 採用経緯の一次資料と、Nx を導入できない技術的制約は、今回確認できていない。
  現在の入口と実行内容の正本は `devenv.nix` と CONTRIBUTING.md である。
- 撤回条件は、Nx を導入する移行を実施し、task の定義を Nx の設定へ移すことである。

## 性能測定を検証入口に含めない

- 現在の事実として、`tests/performance/` の測定は手動の比較計測で、SLO の定めと退行の判定を持たず、`verify-full` に含まれない。
  手順は [performance.md](performance.md) にある。
- 置き換える標準の規律は、`process/verification.md` の T3 が示す SLO 計測である。
- 理由は、測定が手動の比較計測にとどまり、退行を判定する基準を定めていないことである。
- 技術的制約の実証はない。
  基準が未定であることが理由である。
- 撤回条件は、SLO と退行の判定を定め、`verify-full` に組み込むことである。

## CI を置かない

- 現在の事実として、リポジトリに CI の workflow が無い。
  commit の規約と secret の漏洩の検査は、作業機の global hook の commit-msg と pre-commit が担う。
- 置き換える標準の規律は、`process/verification.md` が CI に求める、commit の規約と secret の漏洩と T1 の検査である。
- 共通基盤の提供待ちとした採用経緯の一次資料と、CI を置けない技術的制約は、今回確認できていない。
  現在の運用上の契約は CONTRIBUTING.md の「検証」にある。
- 撤回条件は、CI を置くことである。
  置く場合は、`verify` だけを実行する。

## 既存の複雑度違反

- 現在の事実として、`SonarAnalyzer` の S3776 が認知的複雑度の上限 15 を超える member が二つあり、どちらも member 単位の `SuppressMessage` で抑止している。
- 対象と解消条件は [conformance-baseline.json](conformance-baseline.json) の `S3776-001` と `S3776-002` にある。
- 抑止は、台帳に記録した member に限る。
  台帳にない member へ抑止を加えない。
