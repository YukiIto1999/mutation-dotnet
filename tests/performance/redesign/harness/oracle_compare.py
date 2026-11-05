#!/usr/bin/env python3
"""Oracle の真の判定と各ツールの判定を突き合わせ、誤判定(False Survived / False Killed 等)を数える。"""
import json, sys
from collections import Counter, defaultdict

DETECTED = {"Killed", "Timeout"}

def bucket(s):
    if s in DETECTED: return "detected"
    if s in ("Survived", "NoCoverage"): return "undetected"
    return s

truth = json.load(open(sys.argv[1]))
per_tool = defaultdict(Counter)
mismatches = defaultdict(list)
oracle_counts = Counter()
for key, v in truth.items():
    oracle_counts[v["oracle"]] += 1
    seen_tools = set()
    for s in v["sources"]:
        tool = s["tool"]
        if tool in seen_tools: continue
        seen_tools.add(tool)
        o, t = v["oracle"], s["status"]
        per_tool[tool]["total"] += 1
        if o == "CompileError":
            per_tool[tool]["oracle_compile_error"] += 1
            mismatches[tool].append((v["file"], s["line"], s["mutator"], s["replacement"], t, o, v["detail"][:80]))
            continue
        if bucket(o) == bucket(t):
            per_tool[tool]["agree"] += 1
            if o != t: per_tool[tool]["agree_but_killed_vs_timeout"] += 1
        else:
            kind = "false_undetected" if bucket(t) == "undetected" else "false_detected"
            per_tool[tool][kind] += 1
            mismatches[tool].append((v["file"], s["line"], s["mutator"], s["replacement"], t, o, v["detail"][:80]))
print("oracle verdicts:", dict(oracle_counts))
for tool, c in per_tool.items():
    print(f"== {tool}: {dict(c)}")
    for m in sorted(mismatches[tool]):
        print("   ", m)
