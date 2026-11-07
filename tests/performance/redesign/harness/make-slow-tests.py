#!/usr/bin/env python3
"""合成ターゲットのテストに 20ms の待機を挟み、テスト実行が支配的な workload に変える。

usage: make-slow-tests.py TARGET_DIR
"""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1]) / "Bench.Target.Tests"
count = 0
for path in root.glob("Mod*Tests.cs"):
    text = path.read_text()
    text = re.sub(
        r"\[Fact\] public void (\w+)\(\) => (.+?);\n",
        lambda m: f"[Fact] public void {m.group(1)}() {{ System.Threading.Thread.Sleep(20); {m.group(2)}; }}\n",
        text,
    )
    for name in ("InRange_true_at_bounds", "InRange_false_outside"):
        text = text.replace(
            f"public void {name}()\n    {{", f"public void {name}()\n    {{\n        System.Threading.Thread.Sleep(20);"
        )
    count += text.count("Sleep(20)")
    path.write_text(text)
print(f"sleep-injected tests: {count}")
