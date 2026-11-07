#!/usr/bin/env python3
"""独立 Oracle: 各 mutant をソースへ文字列適用し、個別に build し、新規 process で全テストを実行して真の判定を得る。

schemata・被覆による選択・worker 再利用・継承を一切使わない(最も遅く最も単純な strong mutation)。
usage: oracle.py --workload DIR --target-dir REL --test-project REL --out OUT.json --jobs N REPORT.json [REPORT.json ...]
"""
import argparse, concurrent.futures as cf, hashlib, json, os, re, shutil, subprocess, sys, time

def norm(s):
    return re.sub(r"\s+", "", s)

def apply(text, loc, replacement):
    lines = text.split("\n")
    def offset(line, col):
        return sum(len(l) + 1 for l in lines[: line - 1]) + (col - 1)
    s = offset(loc["start"]["line"], loc["start"]["column"])
    e = offset(loc["end"]["line"], loc["end"]["column"])
    return text[:s] + replacement + text[e:]

def candidates(mutator, replacement, original_span):
    reps = [replacement]
    if mutator == "MethodBodyNullifier":
        reps = ["{ " + replacement + " }"]
    elif replacement == "{}" and original_span.lstrip().startswith("{"):
        reps = ["{}", "{ return default; }", "{ yield break; }"]
    return reps

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--workload", required=True)
    p.add_argument("--target-dir", required=True)
    p.add_argument("--test-project", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--jobs", type=int, default=3)
    p.add_argument("--timeout", type=int, default=60)
    p.add_argument("reports", nargs="+")
    a = p.parse_args()
    variants = {}  # key -> {file, content, sources:[(report, id, mutator, status)]}
    for rp in a.reports:
        tool = os.path.basename(os.path.dirname(os.path.dirname(rp)))
        d = json.load(open(rp))
        for path, fe in d["files"].items():
            fname = os.path.basename(path)
            src = fe["source"]
            for m in fe["mutants"]:
                if m["status"] in ("Ignored", "CompileError"):
                    continue
                loc = m["location"]
                original = apply(src, loc, "\x00")
                span_text = src[len(original.split("\x00")[0]):][: len(src) - len(original) + 1]
                reps = candidates(m["mutatorName"], m["replacement"], span_text)
                contents = [apply(src, loc, r) for r in reps]
                key = hashlib.sha1((fname + "\n" + norm(contents[0])).encode()).hexdigest()
                v = variants.setdefault(key, {"file": fname, "contents": contents, "sources": []})
                v["sources"].append({"tool": tool, "id": m["id"], "mutator": m["mutatorName"], "replacement": m["replacement"],
                                     "line": loc["start"]["line"], "status": m["status"]})
    keys = sorted(variants)
    print(f"{len(keys)} distinct mutated programs from {len(a.reports)} reports", flush=True)
    work = os.path.join(os.path.dirname(os.path.abspath(a.out)), "oracle-work")
    shutil.rmtree(work, ignore_errors=True)
    copies = []
    for j in range(a.jobs):
        dst = os.path.join(work, f"copy{j}")
        shutil.copytree(a.workload, dst, ignore=shutil.ignore_patterns("bin", "obj", "StrykerOutput", ".mutation-output"))
        subprocess.run(["dotnet", "build", os.path.join(dst, a.test_project), "-v", "q", "-nologo"], check=True, capture_output=True)
        copies.append(dst)
    files = {}
    for root, _, fs in os.walk(os.path.join(copies[0], a.target_dir)):
        for f in fs:
            if f.endswith(".cs") and "/obj/" not in root and "/bin/" not in root:
                files[f] = os.path.relpath(os.path.join(root, f), copies[0])

    import queue
    free = queue.Queue()
    for c in copies:
        free.put(c)

    def run_one(key):
        c = free.get()
        try:
            v = variants[key]
            path = os.path.join(c, files[v["file"]])
            original = open(path).read()
            verdict, detail, t0 = None, "", time.time()
            try:
                for content in v["contents"]:
                    open(path, "w").write(content)
                    b = subprocess.run(["dotnet", "build", os.path.join(c, a.test_project), "--no-restore", "-v", "q", "-nologo"],
                                       capture_output=True, text=True)
                    if b.returncode != 0:
                        verdict, detail = "CompileError", (b.stdout[-300:])
                        continue
                    try:
                        r = subprocess.run(["dotnet", "test", os.path.join(c, a.test_project), "--no-build", "--no-restore"],
                                           capture_output=True, text=True, timeout=a.timeout)
                        verdict = "Survived" if r.returncode == 0 else "Killed"
                        if verdict == "Killed":
                            failed = re.findall(r"Failed (\S+) \[", r.stdout)
                            detail = ",".join(failed[:3]) or r.stdout[-200:]
                    except subprocess.TimeoutExpired:
                        verdict = "Timeout"
                        subprocess.run(["pkill", "-f", os.path.join(c, a.test_project.split("/")[0])], capture_output=True)
                    break
            finally:
                open(path, "w").write(original)
            return key, verdict, detail, round(time.time() - t0, 1)
        finally:
            free.put(c)

    results = {}
    with cf.ThreadPoolExecutor(max_workers=a.jobs) as ex:
        for n, (key, verdict, detail, secs) in enumerate(ex.map(run_one, keys), 1):
            results[key] = {**variants[key], "oracle": verdict, "detail": detail, "secs": secs}
            del results[key]["contents"]
            if n % 20 == 0:
                print(f"{n}/{len(keys)}", flush=True)
    json.dump(results, open(a.out, "w"), indent=1)
    print("done", a.out)

if __name__ == "__main__":
    main()
