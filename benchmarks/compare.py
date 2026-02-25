"""
Regression comparison script for BenchmarkDotNet JSON exports.

Usage:
    python benchmarks/compare.py --baseline-dir benchmarks/baseline \
        --current-dir BenchmarkDotNet.Artifacts/results --threshold 0.15
"""

import argparse
import json
import os
import sys


def load_benchmarks(json_path: str) -> dict[str, float]:
    with open(json_path, encoding="utf-8") as f:
        data = json.load(f)
    return {
        b["FullName"]: b["Statistics"]["Mean"]
        for b in data.get("Benchmarks", [])
        if b.get("Statistics") and b["Statistics"].get("Mean") is not None
    }


def load_benchmarks_memory(json_path: str) -> dict[str, int]:
    """Returns allocated bytes per operation; omits benchmarks with no memory data."""
    with open(json_path, encoding="utf-8") as f:
        data = json.load(f)
    result = {}
    for b in data.get("Benchmarks", []):
        alloc = (b.get("Memory") or {}).get("BytesAllocatedPerOperation")
        if alloc is not None:
            result[b["FullName"]] = int(alloc)
    return result


def compare_files(baseline_path: str, current_path: str, threshold: float) -> bool:
    """Returns True if all benchmarks pass, False if any regressed."""
    baseline = load_benchmarks(baseline_path)
    current = load_benchmarks(current_path)
    baseline_mem = load_benchmarks_memory(baseline_path)
    current_mem = load_benchmarks_memory(current_path)

    print(f"\nComparing: {os.path.basename(current_path)}")
    print(f"{'Benchmark':<70} {'Baseline (ns)':>14} {'Current (ns)':>14} {'Change':>8}  {'Base (B)':>10} {'Curr (B)':>10} {'Mem Chg':>8}")
    print("-" * 142)

    passed = True
    for name, current_mean in sorted(current.items()):
        if name not in baseline:
            print(f"{name:<70} {'N/A':>14} {current_mean:>14.1f} {'NEW':>8}")
            continue
        baseline_mean = baseline[name]
        pct_change = (current_mean - baseline_mean) / baseline_mean
        flag = ""
        if pct_change > threshold:
            flag = " *** REGRESSION ***"
            passed = False

        # Memory comparison — only when both sides have data
        mem_str = ""
        base_b = baseline_mem.get(name)
        curr_b = current_mem.get(name)
        if curr_b is not None and base_b is not None:
            if base_b == 0:
                mem_pct_str = "+∞%" if curr_b > 0 else " +0.0%"
                if curr_b > 0:
                    flag = flag or " *** REGRESSION ***"
                    passed = False
            else:
                mem_pct = (curr_b - base_b) / base_b
                mem_pct_str = f"{mem_pct:>+7.1%}"
                if mem_pct > threshold:
                    flag = (flag + " *** REGRESSION ***").strip() if not flag else flag
                    passed = False
            mem_str = f"  {base_b:>10} {curr_b:>10} {mem_pct_str:>8}"
        elif curr_b is not None:
            mem_str = f"  {'N/A':>10} {curr_b:>10} {'NEW':>8}"

        print(f"{name:<70} {baseline_mean:>14.1f} {current_mean:>14.1f} {pct_change:>+7.1%}{mem_str}{flag}")

    return passed


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare BenchmarkDotNet JSON results for regressions.")
    parser.add_argument("--baseline-dir", required=True, help="Directory containing baseline *-report-full.json files")
    parser.add_argument("--current-dir", required=True, help="Directory containing current *-report-full.json files")
    parser.add_argument("--threshold", type=float, default=0.15, help="Regression threshold (default: 0.15 = 15%%)")
    args = parser.parse_args()

    current_files = [
        f for f in os.listdir(args.current_dir) if f.endswith("-report-full.json")
    ]

    if not current_files:
        print(f"No *-report-full.json files found in {args.current_dir}")
        return 1

    all_passed = True
    for filename in sorted(current_files):
        current_path = os.path.join(args.current_dir, filename)
        baseline_path = os.path.join(args.baseline_dir, filename)
        if not os.path.exists(baseline_path):
            print(f"WARNING: No baseline found for {filename} — skipping regression check.")
            continue
        if not compare_files(baseline_path, current_path, args.threshold):
            all_passed = False

    print()
    if all_passed:
        print("✅ All benchmarks within threshold.")
        return 0
    else:
        print(f"❌ One or more benchmarks regressed by more than {args.threshold:.0%}.")
        return 1


if __name__ == "__main__":
    sys.exit(main())
