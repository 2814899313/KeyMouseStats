import argparse
import sqlite3
from pathlib import Path


def totals(path: Path):
    con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    row = con.execute(
        "SELECT IFNULL(SUM(keys),0), IFNULL(SUM(clicks),0), IFNULL(SUM(wheel),0) FROM daily_summary"
    ).fetchone()
    con.close()
    return tuple(int(value) for value in row)


def main():
    parser = argparse.ArgumentParser(description="对比 v3 与 v4 probe 的键/点击/滚轮总数")
    parser.add_argument("--v3", default=str(Path.home() / "AppData/Local/KeyMouseStats/kmstats.db"))
    parser.add_argument("--v4", default=".tmp-input-probe/kmstats.db")
    args = parser.parse_args()

    v3 = Path(args.v3)
    v4 = Path(args.v4)
    if not v3.exists():
        raise SystemExit(f"v3 库不存在：{v3}")
    if not v4.exists():
        raise SystemExit(f"v4 probe 库不存在：{v4}")

    a = totals(v3)
    b = totals(v4)
    print("          keys      clicks    wheel")
    print(f"v3   {a[0]:10d} {a[1]:10d} {a[2]:10d}")
    print(f"v4   {b[0]:10d} {b[1]:10d} {b[2]:10d}")

    for name, left, right in zip(("keys", "clicks", "wheel"), a, b):
        if left == 0 and right == 0:
            print(f"{name}: 0 / 0")
            continue
        base = max(left, right, 1)
        error = abs(left - right) / base * 100
        print(f"{name}: 误差 {error:.3f}%")


if __name__ == "__main__":
    main()
