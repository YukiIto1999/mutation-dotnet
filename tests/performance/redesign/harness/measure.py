#!/usr/bin/env python3
"""cgroup v1 で process tree 全体の wall / CPU / peak memory / process 数 / I/O を計測して JSON で出す。

usage: measure.py --out result.json [--cpus 0-3] [--mem-limit-mb N] [--cwd DIR] [--env K=V ...] -- command args...
"""
import argparse, json, os, signal, subprocess, sys, threading, time, uuid

CG = "/sys/fs/cgroup"
CONTROLLERS = ["memory", "cpuacct", "pids", "blkio", "cpuset"]

def rd(path):
    try:
        with open(path) as f:
            return f.read()
    except OSError:
        return ""

def wr(path, value):
    with open(path, "w") as f:
        f.write(str(value))

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--cpus", default=None)
    p.add_argument("--mem-limit-mb", type=int, default=None)
    p.add_argument("--cwd", default=None)
    p.add_argument("--env", action="append", default=[])
    p.add_argument("--interval", type=float, default=0.1)
    p.add_argument("cmd", nargs=argparse.REMAINDER)
    a = p.parse_args()
    cmd = a.cmd[1:] if a.cmd and a.cmd[0] == "--" else a.cmd
    name = "bench_" + uuid.uuid4().hex[:8]
    paths = {c: os.path.join(CG, c, name) for c in CONTROLLERS}
    for c, path in paths.items():
        os.makedirs(path, exist_ok=True)
    cpus = a.cpus or rd(os.path.join(CG, "cpuset", "cpuset.cpus")).strip()
    wr(os.path.join(paths["cpuset"], "cpuset.cpus"), cpus)
    wr(os.path.join(paths["cpuset"], "cpuset.mems"), rd(os.path.join(CG, "cpuset", "cpuset.mems")).strip())
    if a.mem_limit_mb:
        wr(os.path.join(paths["memory"], "memory.limit_in_bytes"), a.mem_limit_mb * 1024 * 1024)
        try:
            wr(os.path.join(paths["memory"], "memory.memsw.limit_in_bytes"), a.mem_limit_mb * 1024 * 1024)
        except OSError:
            pass
    env = dict(os.environ)
    for kv in a.env:
        k, v = kv.split("=", 1)
        env[k] = v

    def enter():
        pid = os.getpid()
        for path in paths.values():
            wr(os.path.join(path, "cgroup.procs"), pid)

    samples = {"rss": 0, "cache": 0, "procs": 0, "tasks": 0}
    timeline = []
    stop = threading.Event()
    t0 = time.monotonic()

    def sample():
        while not stop.is_set():
            stat = {}
            for line in rd(os.path.join(paths["memory"], "memory.stat")).splitlines():
                k, _, v = line.partition(" ")
                stat[k] = int(v) if v.isdigit() else 0
            rss = stat.get("total_rss", stat.get("rss", 0))
            cache = stat.get("total_cache", stat.get("cache", 0))
            procs = len([x for x in rd(os.path.join(paths["cpuset"], "cgroup.procs")).split() if x])
            tasks = len([x for x in rd(os.path.join(paths["cpuset"], "tasks")).split() if x])
            samples["rss"] = max(samples["rss"], rss)
            samples["cache"] = max(samples["cache"], cache)
            samples["procs"] = max(samples["procs"], procs)
            samples["tasks"] = max(samples["tasks"], tasks)
            cpu = int(rd(os.path.join(paths["cpuacct"], "cpuacct.usage")) or 0)
            timeline.append((round(time.monotonic() - t0, 2), rss // (1024 * 1024), procs, tasks, cpu // 1_000_000))
            stop.wait(a.interval)

    th = threading.Thread(target=sample, daemon=True)
    th.start()
    proc = subprocess.Popen(cmd, cwd=a.cwd, env=env, preexec_fn=enter,
                            stdout=open(a.out + ".log", "w"), stderr=subprocess.STDOUT)
    rc = proc.wait()
    wall = time.monotonic() - t0
    # 残存する子孫(build server 等)を検出して記録してから止める
    leftover = [x for x in rd(os.path.join(paths["cpuset"], "cgroup.procs")).split() if x]
    time.sleep(0.2)
    stop.set(); th.join()
    def num(c, f):
        v = rd(os.path.join(paths[c], f)).strip()
        return int(v) if v.isdigit() else None
    io = {"read": 0, "write": 0}
    for line in rd(os.path.join(paths["blkio"], "blkio.throttle.io_service_bytes_recursive")).splitlines():
        parts = line.split()
        if len(parts) == 3 and parts[1] in ("Read", "Write"):
            io[parts[1].lower()] += int(parts[2])
    mstat = {}
    for line in rd(os.path.join(paths["memory"], "memory.stat")).splitlines():
        k, _, v = line.partition(" ")
        mstat[k] = v
    result = {
        "cmd": cmd, "cwd": a.cwd, "exit": rc, "wall_s": round(wall, 3),
        "cpu_user_s": round((num("cpuacct", "cpuacct.usage_user") or 0) / 1e9, 3),
        "cpu_sys_s": round((num("cpuacct", "cpuacct.usage_sys") or 0) / 1e9, 3),
        "cpu_total_s": round((num("cpuacct", "cpuacct.usage") or 0) / 1e9, 3),
        "mem_peak_usage_mb": round((num("memory", "memory.max_usage_in_bytes") or 0) / 2**20, 1),
        "rss_peak_sampled_mb": round(samples["rss"] / 2**20, 1),
        "cache_peak_sampled_mb": round(samples["cache"] / 2**20, 1),
        "pids_peak_tasks": num("pids", "pids.peak"),
        "procs_peak_sampled": samples["procs"], "tasks_peak_sampled": samples["tasks"],
        "io_read_mb": round(io["read"] / 2**20, 1), "io_write_mb": round(io["write"] / 2**20, 1),
        "oom_events": mstat.get("total_oom_kill") or rd(os.path.join(paths["memory"], "memory.oom_control")).split("oom_kill")[-1].strip() if "oom_kill" in rd(os.path.join(paths["memory"], "memory.oom_control")) else None,
        "leftover_procs": len(leftover), "cpus": cpus, "mem_limit_mb": a.mem_limit_mb,
    }
    for pid in leftover:
        try:
            os.kill(int(pid), signal.SIGKILL)
        except OSError:
            pass
    time.sleep(0.3)
    with open(a.out, "w") as f:
        json.dump(result, f, indent=1)
    with open(a.out + ".timeline.tsv", "w") as f:
        f.write("t_s\trss_mb\tprocs\ttasks\tcpu_ms\n")
        for row in timeline:
            f.write("\t".join(map(str, row)) + "\n")
    for path in paths.values():
        try:
            os.rmdir(path)
        except OSError:
            pass
    print(json.dumps(result))
    sys.exit(0)

if __name__ == "__main__":
    main()
