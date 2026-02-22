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


def compare_files(baseline_path: str, current_path: str, threshold: float) -> bool:
    """Returns True if all benchmarks pass, False if any regressed."""
    baseline = load_benchmarks(baseline_path)
    current = load_benchmarks(current_path)

    print(f"\nComparing: {os.path.basename(current_path)}")
    print(f"{'Benchmark':<70} {'Baseline (ns)':>14} {'Current (ns)':>14} {'Change':>8}")
    print("-" * 110)

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
        print(f"{name:<70} {baseline_mean:>14.1f} {current_mean:>14.1f} {pct_change:>+7.1%}{flag}")

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
