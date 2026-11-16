#!/usr/bin/env python3
"""計測結果 JSON 群を表にする。mutation-dotnet は timings.json、Stryker.NET はログの時刻から段階を復元する。"""
import json, os, re, sys, glob
from datetime import datetime

def stryker_phases(log):
    t = {}
    pat = re.compile(r"\[(\d\d:\d\d:\d\d)\s+INF\]\s+(.*)")
    first = None
    for line in open(log, errors="replace"):
        m = pat.search(line)
        if not m: continue
        ts = datetime.strptime(m.group(1), "%H:%M:%S")
        msg = m.group(2)
        first = first or ts
        def mark(k):
            t.setdefault(k, (ts - first).total_seconds())
        if msg.startswith("Analysis starting"): mark("analysis_start")
        elif msg.startswith("Building test project") or msg.startswith("Building project") or msg.startswith("Building solution"): mark("build_start")
        elif "Initial test run started" in msg or msg.startswith("Number of tests found"): mark("initial_test_start")
        elif re.match(r"\d+ mutants created", msg): mark("mutants_created")
        elif msg.startswith("Capture mutant coverage"): mark("coverage_start")
        elif "total mutants will be tested" in msg: mark("testing_start")
        elif msg.startswith("Time Elapsed"): mark("end")
        m2 = re.match(r"(\d+)\s+mutants created", msg)
        if m2: t["mutants_created_n"] = int(m2.group(1))
    return t

def counts(report):
    from collections import Counter
    if not os.path.exists(report): return {}
    d = json.load(open(report))
    return dict(Counter(m["status"] for f in d["files"].values() for m in f["mutants"]))

rows = []
for j in sorted(glob.glob(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "work", "out", "runs", "*.json"))):
    if j.endswith(".timeline.tsv"): continue
    r = json.load(open(j))
    name = os.path.basename(j)[:-5]
    out = j[:-5]
    rep = (glob.glob(out + "/reports/mutation-report.json") or [""])[0]
    c = counts(rep)
    ph = ""
    if os.path.exists(out + "/reports/timings.json"):
        tj = json.load(open(out + "/reports/timings.json"))
        p = tj.get("phases", {})
        ph = " ".join(f"{k[:-2]}={v/1000:.1f}" for k, v in p.items() if k.endswith("Ms") and isinstance(v, (int, float)))
    elif os.path.exists(j + ".log"):
        sp = stryker_phases(j + ".log")
        ph = " ".join(f"{k}={v}" for k, v in sp.items())
    rows.append((name, r["exit"], r["wall_s"], r["cpu_total_s"], r["rss_peak_sampled_mb"], r["mem_peak_usage_mb"], r["procs_peak_sampled"], r["tasks_peak_sampled"], c, ph))
for row in rows:
    print("\t".join(str(x) for x in row))
