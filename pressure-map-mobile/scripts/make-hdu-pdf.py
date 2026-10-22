"""从真实的 .ics 反推一份「杭电教务课表」版式的 PDF，用于回归 PDF 导入。

为什么要反推：手头没有原始的教务 PDF（只有它导出的 156 条 .ics），
而 ICS 里每条事件都带着「第 W 周 · 第 S—E 节」「教师」「地点」，
正好能把「某门课周几、第几节、哪几周」还原出来，再按教务 PDF 的版式排成表格。

于是形成闭环：真实 ICS → 合成 PDF → pm-hdu.js 解析 → 生成的 ICS 必须和原始 ICS 一致。

用法： python scripts/make-hdu-pdf.py [--ics 路径] [--out 路径]
"""
import argparse
import datetime as dt
import os
import re
import sys

from fpdf import FPDF

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_ICS = os.path.join(os.path.dirname(ROOT), "pressure-map", "output", "孙金辰(2026-2027-1).ics")
DEFAULT_OUT = os.path.join(ROOT, "build", "hdu-sample.pdf")
FONT = "C:/Windows/Fonts/simhei.ttf"

WEEK_ONE = dt.date(2026, 9, 14)
WEEK_CHARS = "一二三四五六日"
START = ["08:05", "08:55", "10:00", "10:50", "11:40", "13:30", "14:20", "15:15", "16:05", "18:30", "19:20", "20:10"]
END = ["08:50", "09:40", "10:45", "11:35", "12:25", "14:15", "15:05", "16:00", "16:50", "19:15", "20:05", "20:55"]


def adjust(nominal):
    key = nominal.isoformat()
    if key == "2026-10-06":
        return dt.date(2026, 9, 20)
    if key == "2026-10-07":
        return dt.date(2026, 10, 10)
    if ("2026-09-25" <= key <= "2026-09-27") or ("2026-10-01" <= key <= "2026-10-07") \
            or key == "2027-01-01" or key >= "2027-01-10" \
            or key == "2026-09-20" or key == "2026-10-10":
        return None
    return nominal


def parse_ics(path):
    raw = open(path, encoding="utf-8").read().replace("\r\n", "\n")
    raw = re.sub(r"\n[ \t]", "", raw)  # 折叠行还原
    events = []
    for block in re.findall(r"BEGIN:VEVENT(.*?)END:VEVENT", raw, re.S):
        fields = {}
        for line in block.strip().split("\n"):
            if ":" not in line:
                continue
            key, value = line.split(":", 1)
            fields[key] = value
        start = dt.datetime.strptime(fields["DTSTART"], "%Y%m%dT%H%M%SZ")
        end = dt.datetime.strptime(fields["DTEND"], "%Y%m%dT%H%M%SZ")
        desc = fields.get("DESCRIPTION", "").replace("\\n", "\n").replace("\\,", ",").replace("\\;", ";")
        week = re.search(r"第(\d+)周", desc)
        events.append({
            "title": fields.get("SUMMARY", "").replace("\\,", ","),
            "room": fields.get("LOCATION", "").replace("\\,", ","),
            "teacher": (re.search(r"教师：([^\n]+)", desc) or [None, ""])[1],
            "campus": (re.search(r"校区：([^\n]+)", desc) or [None, "下沙"])[1],
            "week": int(week.group(1)) if week else None,
            "start": start + dt.timedelta(hours=8),   # ICS 里是 UTC
            "end": end + dt.timedelta(hours=8),
        })
    return events


def period_of(moment, table=START):
    """钟点 → 第几节（1 起）。开课时间查 START，下课时间查 END。"""
    clock = moment.strftime("%H:%M")
    for i, value in enumerate(table):
        if value == clock:
            return i + 1
    raise SystemExit("取不到节次：" + clock)


def group(events):
    """把每条事件还原成「周几 + 第几节 + 哪几周」。"""
    cells = {}
    for e in events:
        if e["week"] is None:
            continue
        nominal = None
        for day in range(7):
            candidate = WEEK_ONE + dt.timedelta(days=(e["week"] - 1) * 7 + day)
            mapped = adjust(candidate)
            if mapped == e["start"].date():
                nominal = candidate
                break
        if nominal is None:
            raise SystemExit("反推不出周几：%s %s" % (e["title"], e["start"]))
        day = (nominal - (WEEK_ONE + dt.timedelta(days=(e["week"] - 1) * 7))).days
        start, end = period_of(e["start"], START), period_of(e["end"], END)
        key = (e["title"], day, start, end, e["room"], e["teacher"])
        cells.setdefault(key, set()).add(e["week"])
    return cells


def weeks_text(weeks):
    """周次集合 → 教务版式的写法：每一段都带「周」，单双周单独标注。

    解析端（ParseWeeks）要求每个逗号段都匹配 `数字[-数字]周[(单|双)]`，
    所以「1-3,5-7周」这种偷懒写法是解析不了的，必须写成「1-3周,5-7周」。
    """
    weeks = sorted(weeks)
    if len(weeks) > 2 and weeks[-1] - weeks[0] == 2 * (len(weeks) - 1):
        parity = "单" if weeks[0] % 2 == 1 else "双"
        return "%d-%d周(%s)" % (weeks[0], weeks[-1], parity)
    runs, index = [], 0
    while index < len(weeks):
        last = index
        while last + 1 < len(weeks) and weeks[last + 1] == weeks[last] + 1:
            last += 1
        runs.append("%d周" % weeks[index] if last == index else "%d-%d周" % (weeks[index], weeks[last]))
        index = last + 1
    return ",".join(runs)


def render(cells, out):
    pdf = FPDF(orientation="L", unit="mm", format="A4")
    pdf.add_font("hei", "", FONT)
    pdf.set_auto_page_break(False)
    pdf.add_page()
    pdf.set_font("hei", size=12)
    pdf.set_xy(10, 8)
    pdf.cell(0, 8, "杭州电子科技大学 2026-2027学年第1学期 课表（合成样本）")

    left, top = 22.0, 26.0
    col_width = (277.0 - left) / 7.0

    # 表头：星期一 … 星期日（必须和正文不同行，且整行能被拼成一段文字）
    pdf.set_font("hei", size=8)
    pdf.set_xy(10, top)
    pdf.cell(12, 6, "节次")
    for d in range(7):
        pdf.set_xy(left + d * col_width, top)
        pdf.cell(col_width, 6, "星期" + WEEK_CHARS[d], align="C")

    # 每列按纵向堆叠课程格子
    pdf.set_font("hei", size=6.2)
    for d in range(7):
        y = top + 12
        column = sorted([c for c in cells if c[1] == d], key=lambda c: (c[2], c[0]))
        for cell in column:
            title, _, start, end, room, teacher = cell
            weeks = weeks_text(cells[cell])
            lines = [
                title,
                "(%d-%d节)%s" % (start, end, weeks),
                "/ 校 区 :%s" % "下沙",
                "/ 场 地 :%s" % room,
                "/ 教 师 :%s" % teacher,
                "/ 教 学 班 :01",
            ]
            for text in lines:
                pdf.set_xy(left + d * col_width, y)
                pdf.cell(col_width, 3.6, text)
                y += 3.6
            y += 2.0

    # 页脚：解析时要被排除掉的那一行
    pdf.set_font("hei", size=6)
    pdf.set_xy(10, 202)
    pdf.cell(0, 5, "其他课程：无")

    os.makedirs(os.path.dirname(out), exist_ok=True)
    pdf.output(out)
    return len(cells)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ics", default=DEFAULT_ICS)
    parser.add_argument("--out", default=DEFAULT_OUT)
    args = parser.parse_args()
    if not os.path.exists(args.ics):
        raise SystemExit("找不到 ICS：%s" % args.ics)
    events = parse_ics(args.ics)
    cells = group(events)
    count = render(cells, args.out)
    print("ICS 事件 %d 条 → 汇总成 %d 个课程格 → %s（%d 字节）"
          % (len(events), count, args.out, os.path.getsize(args.out)))


if __name__ == "__main__":
    main()
