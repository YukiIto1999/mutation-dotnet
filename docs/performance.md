# 性能の測定

この文書は、mutation-dotnet の実行時間を測る保守者が、測定の手順と出力の場所を確かめるための手順書である。
測定値は版、対象、machine、負荷に依存するので、この文書には転記しない。
測るたびに、実行の出力を一時的な記録として扱う。
測定の script と合成対象の生成は `tests/performance/` が正本で、script を変える変更と同じ変更でこの文書を直す。
測定は検証入口 `verify`、`verify-push`、`verify-full` に含まれず、SLO と退行の判定も持たない。

## 合成対象での比較

合成対象を決定的に生成し、mutation-dotnet と Stryker.NET を同じ対象へ実行して、実行時間と判定を比べる。

```bash
dotnet build Mutation.slnx
bash tests/performance/medium/run.sh
bash tests/performance/medium/run.sh --modules 4 --skip-stryker
```

一行目で CLI を build する。
二行目が既定の測定で、対象の生成、mutation-dotnet の既定の並列度での実行、並列度 1 での実行、Stryker.NET の実行の順に進む。
三行目は小さい対象で Stryker.NET を省く動作確認である。

必要なものは、.NET SDK、`python3`、`jq` である。
Stryker.NET は `.config/dotnet-tools.json` が版を固定し、script が `dotnet tool restore` で用意する。

| option | 意味 |
|---|---|
| `--modules N` | 生成する module 数。既定は 40 |
| `--concurrency N` | 比較の並列度。既定は論理コア数の半分 |
| `--skip-stryker` | Stryker.NET の実行を省く |

script は実行のたびに、リポジトリ直下の `work/performance/medium/` の `target/` と `results/` を作り直す。
`work/` は追跡されない作業場所なので、生成物と結果は commit に入らない。

| 出力 | 内容 |
|---|---|
| `work/performance/medium/target/` | 生成した合成 project |
| `work/performance/medium/results/summary.md` | wall time と、mutation-dotnet の段階別内訳と、Stryker.NET の判定別件数 |
| `work/performance/medium/results/ours-c<N>/reports/timings.json` | mutation-dotnet の段階別と変異別の実測 |
| `work/performance/medium/results/ours-c1/` | 並列度 1 の実行の出力 |
| `work/performance/medium/results/stryker-c<N>/` | Stryker.NET の報告 |

## 検出の突合

両者の `mutation-report.json` を、変異の行の範囲の重なりで突き合わせる。

```bash
python3 tests/performance/medium/compare_reports.py <mutation-dotnet の報告> <Stryker.NET の報告>
```

演算子の集合が違うので、変異ごとの一対一の対応は取れない。
`compare_reports.py` は、Stryker.NET が検出した変異の行の範囲に、mutation-dotnet の検出した変異の行の範囲が重ならない箇所を数える。
その箇所が一件でもあれば終了コード 1 になる。
行の範囲の重なりは粗い検査であり、変異の集合の等価や検出品質の同等を示さない。

## 実 project での測定

module ごとに project を分けた実 project へ、mutation-dotnet を順に実行し、module ごとの実測を表にまとめる。

```bash
bash tests/performance/large/real/run-modules.sh <結果の directory> "<project> <test-project>" ...
```

`--mutate` のような追加の option は、環境変数 `MUTATION_EXTRA_ARGS` で渡す。
結果の directory には、module ごとの出力と log、全 module の `summary.tsv` が書かれる。
結果の directory は呼び出し側が決める。
追跡されない場所を指定し、`work/performance/large/real/` の下を例とする。

## 合成対象の設計

合成対象は `tests/performance/medium/generate_target.py` が生成する。
同じ引数なら同じ project になる。
判定の妥当性を測れるよう、次の変異を意図して織り込む。

- 生存する変異として、assert をしない実行だけのテストを持つ method と、境界の等価変異になる比較を置く。
- 被覆なしの変異として、どのテストも呼ばない method を置く。
- timeout の変異として、`while` の加算が無限ループになる method を、10 module に一つの割合で置く。

テストは全て決定的で、I/O と時刻に依存しない。
生成先の root には独自の `Directory.Build.props` を書き、リポジトリの root にある analyzer 構成と central package management を、生成物が継承しないようにしている。
