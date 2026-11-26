"""生成 TextPage 的合成 Rime 日志固件。

真实 Rime 日志含个人上屏文字，不能入库；本脚本只生成可复现的合成样本。
用法（仓库根目录）：python tools/gen-rime-fixture.py
"""

import datetime
import pathlib
import random

random.seed(20261122)

root = pathlib.Path(__file__).resolve().parent.parent
out = root / "tests" / "Fixtures" / "rime-log-sample.csv"

lines = []
start = datetime.date(2026, 10, 24)
end = datetime.date(2026, 11, 22)
apps = ["code.exe", "msedge.exe", "wechat.exe", "notepad.exe", "explorer.exe"]
words = [
    "数据", "精度", "输入", "统计", "键盘", "鼠标", "时间", "工作", "记录",
    "分析", "今天", "明天", "问题", "结果", "窗口", "应用", "文字", "内容",
    "专注", "节奏", "心跳", "节奏感", "可视化", "性能", "界面", "主题",
    "值得", "继续", "完成", "验证", "回归", "基线", "采样", "聚合",
    "键盘热力", "鼠标轨迹", "输入法", "候选词", "上屏", "词频", "字数",
    "数据精度", "工作时间", "统计口径", "可视化面板", "真实记录",
]
phrases = [
    "数据精度", "输入统计", "键盘热力", "工作时间", "可视化面板",
    "真实记录", "候选词", "上屏次数", "词频排行", "应用场景",
]

day = start
while day <= end:
    count = random.randint(18, 34)
    hour = 9
    minute = random.randint(0, 30)
    for i in range(count):
        minute += random.randint(1, 9)
        if minute >= 60:
            minute -= 60
            hour = min(23, hour + 1)
        if hour > 22:
            hour = 9
            minute = random.randint(0, 59)
        text = random.choice(words if random.random() < 0.75 else phrases)
        app = random.choice(apps)
        hanzi = sum(1 for ch in text if "\u4e00" <= ch <= "\u9fff")
        candidate = 0 if random.random() < 0.78 else random.randint(1, 3)
        stamp = f"{day:%Y-%m-%d} {hour:02d}:{minute:02d}:{random.randint(0, 59):02d}"
        lines.append(f"{stamp},{hanzi},{text},{app},rime_ice,{8 + (i % 9)},{candidate}")
    day += datetime.timedelta(days=1)

out.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
print(f"{len(lines)} lines -> {out}")
