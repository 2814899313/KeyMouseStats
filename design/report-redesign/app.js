/* ==========================================================================
   键鼠统计 · 报告设计原型
   图表、主题、报表切换、浮层。无依赖。
   ========================================================================== */

(function () {
  "use strict";

  if ("scrollRestoration" in history) history.scrollRestoration = "manual";

  var NS = "http://www.w3.org/2000/svg";

  function svgEl(tag, attrs) {
    var node = document.createElementNS(NS, tag);
    if (attrs) {
      Object.keys(attrs).forEach(function (key) {
        node.setAttribute(key, attrs[key]);
      });
    }
    return node;
  }

  function textEl(parent, x, y, text, className, anchor) {
    var node = svgEl("text", { x: x, y: y });
    if (className) node.setAttribute("class", className);
    if (anchor) node.setAttribute("text-anchor", anchor);
    node.textContent = text;
    parent.appendChild(node);
    return node;
  }

  function smoothPath(points) {
    if (!points.length) return "";
    if (points.length === 1) return "M " + points[0][0] + " " + points[0][1];
    var d = "M " + points[0][0] + " " + points[0][1];
    for (var i = 0; i < points.length - 1; i++) {
      var p0 = points[i - 1] || points[i];
      var p1 = points[i];
      var p2 = points[i + 1];
      var p3 = points[i + 2] || p2;
      var c1x = p1[0] + (p2[0] - p0[0]) / 6;
      var c1y = p1[1] + (p2[1] - p0[1]) / 6;
      var c2x = p2[0] - (p3[0] - p1[0]) / 6;
      var c2y = p2[1] - (p3[1] - p1[1]) / 6;
      d += " C " + c1x + " " + c1y + ", " + c2x + " " + c2y + ", " + p2[0] + " " + p2[1];
    }
    return d;
  }

  function makeGradient(defs, id, stops) {
    var grad = svgEl("linearGradient", { id: id, x1: "0", y1: "0", x2: "0", y2: "1" });
    stops.forEach(function (stop) {
      var node = svgEl("stop", { offset: stop.offset });
      node.setAttribute("style", "stop-color:" + stop.color + ";stop-opacity:" + stop.opacity);
      grad.appendChild(node);
    });
    defs.appendChild(grad);
    return grad;
  }

  /* ---------------------------------------------------------------- 日报数据 */

  var dailyHours = [
    0, 0, 0, 0, 0, 0, 0, 60, 540, 1200, 980, 760, 380,
    540, 1912, 1420, 1280, 1100, 860, 620, 980, 560, 220, 8
  ];

  var dailySessions = [
    { start: 8.5, end: 9.0, label: "08:30–09:00", minutes: 30 },
    { start: 9.1667, end: 11.1667, label: "09:10–11:10", minutes: 120 },
    { start: 11.6667, end: 12.25, label: "11:40–12:15", minutes: 35 },
    { start: 13.0833, end: 16.25, label: "13:05–16:15", minutes: 190 },
    { start: 16.5833, end: 17.75, label: "16:35–17:45", minutes: 70 },
    { start: 19.1667, end: 20.0, label: "19:10–20:00", minutes: 50 },
    { start: 20.3333, end: 21.4667, label: "20:20–21:28", minutes: 68 }
  ];

  /* ---------------------------------------------------------------- 日报：脉搏图 */

  function renderDailyPulse() {
    var host = document.getElementById("dailyPulse");
    if (!host) return;
    var W = 1000, H = 300, L = 42, R = 18, T = 22, B = 42;
    var plotW = W - L - R;
    var plotH = H - T - B;
    var max = 2000;
    var step = plotW / 24;
    var svg = svgEl("svg", { viewBox: "0 0 " + W + " " + H, role: "img" });
    svg.setAttribute("class", "chart");

    var defs = svgEl("defs");
    makeGradient(defs, "barGrad", [
      { offset: "0%", color: "var(--accent)", opacity: ".92" },
      { offset: "100%", color: "var(--accent)", opacity: ".1" }
    ]);
    svg.appendChild(defs);

    for (var v = 0; v <= max; v += 500) {
      var gy = T + plotH - (v / max) * plotH;
      svg.appendChild(svgEl("line", {
        x1: L, y1: gy, x2: W - R, y2: gy, "class": "grid-line"
      }));
      textEl(svg, L - 9, gy + 3, v === 0 ? "0" : v.toLocaleString("en-US"), "", "end");
    }

    var total = 0, activeHours = 0;
    dailyHours.forEach(function (value) {
      total += value;
      if (value > 0) activeHours++;
    });
    var avg = activeHours ? total / activeHours : 0;
    var avgY = T + plotH - (avg / max) * plotH;

    var avgLine = svgEl("line", {
      x1: L, y1: avgY, x2: W - R, y2: avgY, "class": "avg-line"
    });
    svg.appendChild(avgLine);
    textEl(svg, W - R - 2, avgY - 6, "活跃小时均值 " + Math.round(avg), "avg-label", "end");

    dailyHours.forEach(function (value, hour) {
      if (value <= 0) return;
      var barW = Math.max(5, step * 0.54);
      var x = L + step * hour + (step - barW) / 2;
      var h = (value / max) * plotH;
      var y = T + plotH - h;
      var rect = svgEl("rect", {
        x: x, y: y, width: barW, height: h, rx: 4,
        "class": "bar",
        fill: hour === 14 ? "var(--accent)" : "url(#barGrad)",
        "data-tip": hour + ":00 · " + value.toLocaleString("en-US") + " 次击键",
        style: "--i:" + hour
      });
      svg.appendChild(rect);
    });

    // 活跃时段带直接画在柱状图下方,与柱状图共用同一套 3 小时刻度。
    var stripY = T + plotH + 8;
    var stripH = 8;
    svg.appendChild(svgEl("rect", {
      x: L, y: stripY, width: plotW, height: stripH, rx: 4,
      fill: "var(--surface-2)"
    }));
    dailySessions.forEach(function (session) {
      var sx = L + (session.start / 24) * plotW;
      var sw = Math.max(3, ((session.end - session.start) / 24) * plotW);
      svg.appendChild(svgEl("rect", {
        x: sx, y: stripY, width: sw, height: stripH, rx: 3,
        fill: "var(--mint)", opacity: ".55",
        "data-tip": session.label + " · " + session.minutes + " 分钟"
      }));
    });

    var peakX = L + step * 14.5;
    var peakY = T + plotH - (1912 / max) * plotH;
    svg.appendChild(svgEl("line", {
      x1: peakX, y1: peakY + 8, x2: peakX, y2: T + plotH,
      stroke: "var(--accent)", "stroke-width": 1, "stroke-dasharray": "3 5", opacity: ".55"
    }));
    var tagW = 116;
    var tagX = peakX + 10;
    if (tagX + tagW > W - R - 6) tagX = peakX - tagW - 10;
    if (tagX < L + 4) tagX = L + 4;
    var tagY = Math.max(T + 12, peakY - 30);
    var tag = svgEl("g");
    tag.appendChild(svgEl("rect", {
      x: tagX, y: tagY, width: tagW, height: 24, rx: 7, "class": "annotation-bg"
    }));
    textEl(tag, tagX + tagW / 2, tagY + 16, "14:00 · 1,912 次", "annotation-text", "middle");
    svg.appendChild(tag);

    for (var hour = 0; hour < 24; hour += 3) {
      textEl(svg, L + step * (hour + 0.5), stripY + stripH + 10,
        hour < 10 ? "0" + hour : String(hour), "", "middle");
    }

    svg.appendChild(svgEl("line", {
      x1: L, y1: T + plotH, x2: W - R, y2: T + plotH, "class": "axis-line"
    }));

    host.innerHTML = "";
    host.appendChild(svg);
  }

  /* ---------------------------------------------------------------- 周报数据 */

  var weekDays = [
    { day: "三", date: "11.11", hours: 7.7, keys: 9820 },
    { day: "四", date: "11.12", hours: 8.1, keys: 12460 },
    { day: "五", date: "11.13", hours: 9.9, keys: 16840, best: true },
    { day: "六", date: "11.14", hours: 5.2, keys: 9240 },
    { day: "日", date: "11.15", hours: 6.4, keys: 11780 },
    { day: "一", date: "11.16", hours: 4.2, keys: 6120 },
    { day: "二", date: "11.17", hours: 7.3, keys: 12160 }
  ];

  var lastWeekKeys = [8200, 10400, 11300, 8600, 9900, 5400, 10100];

  function formatHours(h) {
    var whole = Math.floor(h);
    var minutes = Math.round((h - whole) * 60);
    return whole + "h " + (minutes < 10 ? "0" + minutes : minutes) + "m";
  }

  function renderWeekStrip() {
    var host = document.getElementById("weekStrip");
    if (!host) return;
    host.innerHTML = "";
    var max = 9.9;
    weekDays.forEach(function (item, index) {
      var col = document.createElement("div");
      col.className = "week-col" + (item.best ? " is-best" : "");
      col.style.setProperty("--i", index);
      col.setAttribute("data-tip", "周" + item.day + " " + item.date + " · " + formatHours(item.hours) + " · " + item.keys.toLocaleString("en-US") + " 次击键");

      var bar = document.createElement("div");
      bar.className = "week-bar";
      bar.style.setProperty("--h", Math.round(item.hours / max * 100) + "%");

      var val = document.createElement("span");
      val.className = "week-val";
      val.textContent = item.hours.toFixed(1) + "h";
      bar.appendChild(val);

      var day = document.createElement("span");
      day.className = "week-day";
      day.textContent = "周" + item.day;

      var date = document.createElement("span");
      date.className = "week-date";
      date.textContent = item.date;

      col.appendChild(bar);
      col.appendChild(day);
      col.appendChild(date);
      host.appendChild(col);
    });
  }

  function renderWeekTrend() {
    var host = document.getElementById("weekTrend");
    if (!host) return;
    var W = 1000, H = 280, L = 44, R = 18, T = 24, B = 42;
    var plotW = W - L - R;
    var plotH = H - T - B;
    var max = 18000;
    var step = plotW / (weekDays.length - 1);
    var svg = svgEl("svg", { viewBox: "0 0 " + W + " " + H, role: "img" });
    svg.setAttribute("class", "chart");

    var defs = svgEl("defs");
    makeGradient(defs, "weekGrad", [
      { offset: "0%", color: "var(--accent)", opacity: ".24" },
      { offset: "100%", color: "var(--accent)", opacity: "0" }
    ]);
    svg.appendChild(defs);

    for (var v = 0; v <= max; v += 6000) {
      var gy = T + plotH - (v / max) * plotH;
      svg.appendChild(svgEl("line", { x1: L, y1: gy, x2: W - R, y2: gy, "class": "grid-line" }));
      textEl(svg, L - 9, gy + 3, v === 0 ? "0" : (v / 1000) + "k", "", "end");
    }

    var prevPoints = lastWeekKeys.map(function (value, index) {
      return [L + step * index, T + plotH - (value / max) * plotH];
    });
    svg.appendChild(svgEl("path", {
      d: smoothPath(prevPoints),
      fill: "none",
      stroke: "var(--muted)",
      "stroke-width": 1.4,
      "stroke-dasharray": "4 5",
      opacity: ".42"
    }));

    var thisPoints = weekDays.map(function (item, index) {
      return [L + step * index, T + plotH - (item.keys / max) * plotH];
    });
    var line = smoothPath(thisPoints);
    var area = line + " L " + thisPoints[thisPoints.length - 1][0] + " " + (T + plotH) +
      " L " + thisPoints[0][0] + " " + (T + plotH) + " Z";

    var areaNode = svgEl("path", { d: area, "class": "spark-area", fill: "url(#weekGrad)" });
    svg.appendChild(areaNode);

    var lineNode = svgEl("path", {
      d: line, "class": "spark-line", stroke: "var(--accent)"
    });
    svg.appendChild(lineNode);

    thisPoints.forEach(function (point, index) {
      var item = weekDays[index];
      var dot = svgEl("circle", {
        cx: point[0], cy: point[1], r: item.best ? 5 : 3.5,
        fill: item.best ? "var(--accent)" : "var(--surface)",
        stroke: "var(--accent)", "stroke-width": 2, "class": "dot-point"
      });
      dot.setAttribute("data-tip", "周" + item.day + " " + item.date + " · " + item.keys.toLocaleString("en-US") + " 次");
      svg.appendChild(dot);
      textEl(svg, point[0], H - 22, "周" + item.day, "", "middle");
      textEl(svg, point[0], H - 8, item.date, "week-label", "middle");
    });

    var best = thisPoints[2];
    svg.appendChild(svgEl("line", {
      x1: best[0], y1: best[1] + 10, x2: best[0], y2: T + plotH,
      stroke: "var(--accent)", "stroke-width": 1, "stroke-dasharray": "3 5", opacity: ".5"
    }));
    var tag = svgEl("g");
    tag.appendChild(svgEl("rect", {
      x: best[0] - 48, y: best[1] - 40, width: 96, height: 24, rx: 7, "class": "annotation-bg"
    }));
    textEl(tag, best[0], best[1] - 24, "16,840 次", "annotation-text", "middle");
    svg.appendChild(tag);

    svg.appendChild(svgEl("line", {
      x1: L, y1: T + plotH, x2: W - R, y2: T + plotH, "class": "axis-line"
    }));

    host.innerHTML = "";
    host.appendChild(svg);
  }

  /* ---------------------------------------------------------------- 月报数据 */

  var monthHours = [
    8.6, 8.4, 6.1, 9.3, 6.8, 7.4, 8.2, 5.9, 4.2, 3.1,
    6.0, 7.1, 8.0, 6.4, 5.6, 4.8, 2.4, 4.5, 8.1, 6.6,
    9.8, 6.0, 5.4, 3.0, 5.2, 4.1, 2.2, 0, 1.8, 1.5, 1.5
  ];

  var monthKeys = [
    10660, 12100, 8800, 14200, 9800, 10600, 11800, 8500, 6100, 4500,
    8700, 10200, 11500, 9200, 8100, 6900, 3500, 6500, 11600, 9500,
    18940, 8600, 7800, 4300, 7500, 5900, 3200, 0, 2600, 2200, 2200
  ];

  var weeks = [
    { label: "W40", range: "10.01 — 10.04", total: 32.4, days: 4, values: [8.6, 8.4, 6.1, 9.3] },
    { label: "W41", range: "10.05 — 10.11", total: 41.6, days: 7, values: [6.8, 7.4, 8.2, 5.9, 4.2, 3.1, 6.0] },
    { label: "W42", range: "10.12 — 10.18", total: 38.8, days: 7, values: [7.1, 8.0, 6.4, 5.6, 4.8, 2.4, 4.5] },
    { label: "W43", range: "10.19 — 10.25", total: 44.1, days: 7, values: [8.1, 6.6, 9.8, 6.0, 5.4, 3.0, 5.2], peak: true },
    { label: "W44", range: "10.26 — 10.31", total: 11.1, days: 6, values: [4.1, 2.2, 0, 1.8, 1.5, 1.5] }
  ];

  function renderMonthCalendar() {
    var host = document.getElementById("monthCalendar");
    if (!host) return;
    host.innerHTML = "";

    ["一", "二", "三", "四", "五", "六", "日"].forEach(function (name) {
      var head = document.createElement("div");
      head.className = "cal-head";
      head.textContent = name;
      host.appendChild(head);
    });

    // 2026-10-01 是周四，周一为一周起始，前面空 3 格
    for (var blank = 0; blank < 3; blank++) {
      var empty = document.createElement("div");
      empty.className = "cal-cell blank";
      host.appendChild(empty);
    }

    monthHours.forEach(function (hours, index) {
      var day = index + 1;
      var cell = document.createElement("div");
      var level = hours <= 0 ? "" : hours < 3 ? "l1" : hours < 6 ? "l2" : hours < 8 ? "l3" : "l4";
      cell.className = "cal-cell" + (hours > 0 ? " has " + level : "") + (day === 22 ? " is-best" : "");
      cell.setAttribute("data-tip", "10 月 " + day + " 日 · " + (hours > 0 ? hours.toFixed(1) + " 小时 · " + monthKeys[index].toLocaleString("en-US") + " 次击键" : "没有记录"));

      var number = document.createElement("span");
      number.textContent = day;
      cell.appendChild(number);
      host.appendChild(cell);
    });

    var trailing = document.createElement("div");
    trailing.className = "cal-cell blank";
    host.appendChild(trailing);
  }

  function renderWeekCards() {
    var host = document.getElementById("weekCards");
    if (!host) return;
    host.innerHTML = "";
    var dayMax = 9.8;

    weeks.forEach(function (week) {
      var card = document.createElement("article");
      card.className = "week-card" + (week.peak ? " is-peak" : "");

      var head = document.createElement("header");
      var label = document.createElement("span");
      label.textContent = week.label + " · " + week.range;
      var total = document.createElement("b");
      total.textContent = week.total.toFixed(1) + "h";
      head.appendChild(label);
      head.appendChild(total);

      var bars = document.createElement("div");
      bars.className = "mini-bars";
      week.values.forEach(function (value) {
        var bar = document.createElement("i");
        bar.style.setProperty("--h", Math.max(2, Math.round(value / dayMax * 100)) + "%");
        bar.setAttribute("data-tip", week.label + " · " + value.toFixed(1) + " 小时");
        bars.appendChild(bar);
      });

      var foot = document.createElement("small");
      foot.textContent = week.days + " 天 · 日均 " + (week.total / week.days).toFixed(1) + "h";

      card.appendChild(head);
      card.appendChild(bars);
      card.appendChild(foot);
      host.appendChild(card);
    });
  }

  function renderMonthTrend() {
    var host = document.getElementById("monthTrend");
    if (!host) return;
    var W = 1000, H = 280, L = 44, R = 18, T = 30, B = 42;
    var plotW = W - L - R;
    var plotH = H - T - B;
    var max = 12;
    var step = plotW / (monthHours.length - 1);
    var svg = svgEl("svg", { viewBox: "0 0 " + W + " " + H, role: "img" });
    svg.setAttribute("class", "chart");

    var defs = svgEl("defs");
    makeGradient(defs, "monthGrad", [
      { offset: "0%", color: "var(--accent)", opacity: ".22" },
      { offset: "100%", color: "var(--accent)", opacity: "0" }
    ]);
    svg.appendChild(defs);

    for (var v = 0; v <= max; v += 3) {
      var gy = T + plotH - (v / max) * plotH;
      svg.appendChild(svgEl("line", { x1: L, y1: gy, x2: W - R, y2: gy, "class": "grid-line" }));
      textEl(svg, L - 9, gy + 3, v === 0 ? "0" : v + "h", "", "end");
    }

    var weekStarts = [4, 11, 18, 25];
    var weekNames = ["W40", "W41", "W42", "W43", "W44"];
    weekStarts.forEach(function (index, i) {
      var x = L + step * index;
      svg.appendChild(svgEl("line", {
        x1: x, y1: T, x2: x, y2: T + plotH, "class": "week-sep"
      }));
      var center = i === 0 ? (L + (x - L) / 2) : (L + step * (weekStarts[i - 1] + index) / 2);
      textEl(svg, center, T - 10, weekNames[i], "week-label", "middle");
    });
    textEl(svg, L + step * (25 + 30) / 2, T - 10, "W44", "week-label", "middle");

    var points = monthHours.map(function (hours, index) {
      return [L + step * index, T + plotH - (hours / max) * plotH];
    });
    var line = smoothPath(points);
    var area = line + " L " + points[points.length - 1][0] + " " + (T + plotH) +
      " L " + points[0][0] + " " + (T + plotH) + " Z";

    svg.appendChild(svgEl("path", { d: area, "class": "spark-area", fill: "url(#monthGrad)" }));

    var lineNode = svgEl("path", {
      d: line, "class": "spark-line", stroke: "var(--accent)"
    });
    svg.appendChild(lineNode);

    // 7 日均线：跳过没有记录的日子
    var avgPoints = [];
    monthHours.forEach(function (_, index) {
      var start = Math.max(0, index - 6);
      var windowValues = monthHours.slice(start, index + 1).filter(function (value) { return value > 0; });
      if (!windowValues.length) return;
      var sum = windowValues.reduce(function (a, b) { return a + b; }, 0);
      var avg = sum / windowValues.length;
      avgPoints.push([L + step * index, T + plotH - (avg / max) * plotH]);
    });
    svg.appendChild(svgEl("path", {
      d: smoothPath(avgPoints),
      fill: "none",
      stroke: "var(--muted)",
      "stroke-width": 1.3,
      "stroke-dasharray": "4 5",
      opacity: ".55"
    }));

    [1, 5, 10, 15, 20, 25, 31].forEach(function (day) {
      var x = L + step * (day - 1);
      textEl(svg, x, H - 16, String(day), "", "middle");
    });

    var bestPoint = points[21];
    svg.appendChild(svgEl("line", {
      x1: bestPoint[0], y1: bestPoint[1] + 10, x2: bestPoint[0], y2: T + plotH,
      stroke: "var(--accent)", "stroke-width": 1, "stroke-dasharray": "3 5", opacity: ".5"
    }));
    var tag = svgEl("g");
    tag.appendChild(svgEl("rect", {
      x: bestPoint[0] - 44, y: bestPoint[1] - 40, width: 88, height: 24, rx: 7, "class": "annotation-bg"
    }));
    textEl(tag, bestPoint[0], bestPoint[1] - 24, "9.8 小时", "annotation-text", "middle");
    svg.appendChild(tag);

    svg.appendChild(svgEl("line", {
      x1: L, y1: T + plotH, x2: W - R, y2: T + plotH, "class": "axis-line"
    }));

    host.innerHTML = "";
    host.appendChild(svg);
  }

  /* ---------------------------------------------------------------- 切换与主题 */

  var reportNames = { daily: "日报", weekly: "周报", monthly: "月报" };

  function showReport(target) {
    if (!reportNames[target]) return;
    document.documentElement.setAttribute("data-report", target);

    document.querySelectorAll(".nav-item").forEach(function (item) {
      var on = item.getAttribute("data-target") === target;
      item.classList.toggle("is-active", on);
      if (on) item.setAttribute("aria-current", "page");
      else item.removeAttribute("aria-current");
    });

    document.querySelectorAll(".report").forEach(function (report) {
      var on = report.id === target;
      report.hidden = !on;
      report.classList.toggle("is-active", on);
    });

    document.getElementById("crumbReport").textContent = reportNames[target];
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function setTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
    document.querySelectorAll("[data-theme-set]").forEach(function (button) {
      button.classList.toggle("is-active", button.getAttribute("data-theme-set") === theme);
    });
    try { localStorage.setItem("kms-report-theme", theme); } catch (error) { /* 本地文件可能禁用存储 */ }
  }

  function initTabs() {
    document.querySelectorAll(".nav-item").forEach(function (item) {
      item.addEventListener("click", function () {
        showReport(item.getAttribute("data-target"));
      });
    });

    document.addEventListener("keydown", function (event) {
      if (event.target && /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName)) return;
      if (event.key === "1") showReport("daily");
      if (event.key === "2") showReport("weekly");
      if (event.key === "3") showReport("monthly");
    });
  }

  function initTheme() {
    var requested = null;
    try {
      requested = new URLSearchParams(window.location.search).get("theme");
    } catch (error) { /* 忽略 */ }

    var stored = null;
    try { stored = localStorage.getItem("kms-report-theme"); } catch (error) { /* 忽略 */ }

    var theme = requested === "light" || requested === "dark"
      ? requested
      : (stored === "light" ? "light" : "dark");
    setTheme(theme);

    document.querySelectorAll("[data-theme-set]").forEach(function (button) {
      button.addEventListener("click", function () {
        setTheme(button.getAttribute("data-theme-set"));
      });
    });
  }

  /* ---------------------------------------------------------------- 浮层 */

  var toastTimer = null;

  function toast(message) {
    var node = document.getElementById("toast");
    node.textContent = message;
    node.classList.add("is-on");
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(function () {
      node.classList.remove("is-on");
    }, 2400);
  }

  function initToast() {
    document.getElementById("exportBtn").addEventListener("click", function () {
      toast("原型版本：导出功能会在接入程序后可用。");
    });

    document.querySelectorAll("[data-fake]").forEach(function (button) {
      button.addEventListener("click", function () {
        toast("原型只展示固定样例数据，翻页将在接入程序后启用。");
      });
    });
  }

  function initTooltip() {
    var tip = document.getElementById("tooltip");
    var current = null;

    function place(event) {
      var x = event.clientX;
      var y = event.clientY;
      // 侧边弹出,避免遮住最高柱顶;靠近右边缘时自动翻到左侧。
      tip.classList.toggle("is-left", x > window.innerWidth - 230);
      if (y < 80) y = 80;
      if (y > window.innerHeight - 80) y = window.innerHeight - 80;
      tip.style.left = x + "px";
      tip.style.top = y + "px";
    }

    document.addEventListener("mouseover", function (event) {
      var target = event.target.closest ? event.target.closest("[data-tip]") : null;
      if (!target || target === current) return;
      current = target;
      tip.textContent = target.getAttribute("data-tip");
      tip.classList.add("is-on");
      place(event);
    });

    document.addEventListener("mousemove", function (event) {
      if (current) place(event);
    });

    document.addEventListener("mouseout", function (event) {
      var target = event.target.closest ? event.target.closest("[data-tip]") : null;
      if (!target || target !== current) return;
      var next = event.relatedTarget;
      if (next && next.closest && next.closest("[data-tip]") === target) return;
      current = null;
      tip.classList.remove("is-on");
    });

    window.addEventListener("scroll", function () {
      if (current) {
        current = null;
        tip.classList.remove("is-on");
      }
    }, { passive: true });
  }

  /* ---------------------------------------------------------------- 启动 */

  function init() {
    renderDailyPulse();
    renderWeekStrip();
    renderWeekTrend();
    renderMonthCalendar();
    renderWeekCards();
    renderMonthTrend();
    initTabs();
    initTheme();
    initToast();
    initTooltip();

    // 支持 ?report=weekly&theme=light 这种静态打开方式，方便截图和分享。
    // 不再使用 #hash 作为报告入口，避免浏览器自动锚点滚动把顶栏藏起来。
    var params = new URLSearchParams(window.location.search);
    var wanted = params.get("report");
    if (reportNames[wanted]) showReport(wanted);
    window.scrollTo(0, 0);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
