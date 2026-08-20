#!/usr/bin/env python3
"""同一対象への本ツールと Stryker.NET の mutation-report.json を位置で突合する。

usage: compare_reports.py OURS_JSON STRYKER_JSON

行単位で両者の mutant を対応付け、「Stryker が検出(Killed/Timeout)した行のうち
本ツールが検出しなかった行」を検出漏れの疑いとして列挙する。演算子集合が違うため
mutant 単位の全単射は取れず、行単位の保守的な比較になる。
"""
import json
import sys
from collections import defaultdict

DETECTED = {"Killed", "Timeout"}
TESTED = DETECTED | {"Survived", "NoCoverage"}


def spans_by_status(report: dict, wanted: set[str]) -> dict[str, list[tuple[int, int]]]:
    result: dict[str, list[tuple[int, int]]] = defaultdict(list)
    for path, entry in report["files"].items():
        for mutant in entry["mutants"]:
            if mutant["status"] in wanted:
                loc = mutant["location"]
                result[path].append((loc["start"]["line"], loc["end"]["line"]))
    return result


def overlaps(span: tuple[int, int], spans: list[tuple[int, int]]) -> bool:
    return any(span[0] <= end and start <= span[1] for start, end in spans)


def normalize(report: dict) -> dict:
    # 両ツールで path の基準が違う(project 相対と絶対)ため、末尾の file 名で突合する。
    # 同名 file が複数あると衝突するので、その場合は親 directory を 1 段ずつ付けて一意化する。
    files: dict[str, dict] = {}
    for path, entry in report["files"].items():
        parts = path.replace("\\", "/").split("/")
        key = parts[-1]
        depth = 2
        while key in files and depth <= len(parts):
            key = "/".join(parts[-depth:])
            depth += 1
        files[key] = entry
    return {"files": files}


def main() -> int:
    ours = normalize(json.load(open(sys.argv[1])))
    theirs = normalize(json.load(open(sys.argv[2])))
    ours_detected = spans_by_status(ours, DETECTED)
    ours_tested = spans_by_status(ours, TESTED)
    theirs_detected = spans_by_status(theirs, DETECTED)
    theirs_tested = spans_by_status(theirs, TESTED)

    missed = []
    for path, spans in theirs_detected.items():
        for span in sorted(set(spans)):
            if not overlaps(span, ours_detected.get(path, [])):
                missed.append((path, span))

    extra = []
    for path, spans in ours_detected.items():
        for span in sorted(set(spans)):
            if not overlaps(span, theirs_tested.get(path, [])):
                extra.append((path, span))

    print(f"ours: detected mutants={sum(len(v) for v in ours_detected.values())} "
          f"tested mutants={sum(len(v) for v in ours_tested.values())}")
    print(f"stryker: detected mutants={sum(len(v) for v in theirs_detected.values())} "
          f"tested mutants={sum(len(v) for v in theirs_tested.values())}")
    print(f"stryker が検出したのに本ツールの検出 span と重ならない箇所: {len(missed)}")
    for path, span in missed[:50]:
        print(f"  MISSED {path}:{span[0]}-{span[1]}")
    print(f"本ツールのみが検査・検出した span (stryker は未検査): {len(extra)}")
    return 1 if missed else 0


if __name__ == "__main__":
    sys.exit(main())
