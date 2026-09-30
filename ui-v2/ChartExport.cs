using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ALP2
{
    /// <summary>
    /// 把首页时间线的数据导出成两种"真"文件：
    ///
    ///  · CSV —— 逐秒明细，交给 Excel/WPS 自己画图；
    ///  · HTML+SVG —— 由程序**按数据重新绘制**的矢量图表（含坐标轴、阈值线、图例、
    ///    汇总统计与数据表），浏览器直接打开、可缩放不糊、可插进文档。
    ///
    /// 刻意不做"截屏"：位图截图既不能改、也不能拿去算，对分析没有价值。
    /// </summary>
    internal static class ChartExport
    {
        private const int MaxBuckets = 1200;    // SVG 最多画这么多点（按桶取最大值，不会丢尖峰）

        // ------------------------------------------------------------------ CSV

        /// <summary>逐秒明细 CSV（时间列沿用 Excel/WPS 不会破坏的 ISO 格式）。</summary>
        public static string ExportCsv(string dir, List<TimelineChart.Sample> s)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Csv("Seq", "Time", "Date", "TimeHMS", "T+(s)", "MaxDpc(us)", "AvgDpc(us)", "MaxIsr(us)", "Spikes"));
            DateTime t0 = s.Count > 0 ? s[0].T : DateTime.Now;
            for (int i = 0; i < s.Count; i++)
            {
                TimelineChart.Sample x = s[i];
                sb.AppendLine(Csv((i + 1).ToString(),
                    x.T.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                    x.T.ToString("yyyy-MM-dd"),
                    x.T.ToString("HH:mm:ss"),
                    (x.T - t0).TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    x.MaxDpc.ToString("F2"), x.AvgDpc.ToString("F2"), x.MaxIsr.ToString("F2"),
                    x.Spikes.ToString()));
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string f = Path.Combine(dir, "ALPA_v2_Chart_" + stamp + ".csv");
            File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));

            try
            {
                StringBuilder d = new StringBuilder();
                d.AppendLine("ALPA v2 时间线数据 CSV - 列说明");
                d.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                d.AppendLine("样本数: " + s.Count + " 条（每秒一条）");
                if (s.Count > 0)
                    d.AppendLine("时间范围: " + s[0].T.ToString("yyyy-MM-dd HH:mm:ss") + " ~ " +
                                 s[s.Count - 1].T.ToString("yyyy-MM-dd HH:mm:ss"));
                d.AppendLine();
                d.AppendLine("Seq            序号，从 1 递增（每秒一条）");
                d.AppendLine("Time           ISO 8601 带 T 分隔的时刻（Excel/WPS 不会改写，零丢失）");
                d.AppendLine("Date / TimeHMS 日期与时分秒，Excel 原生类型，方便排序/筛选/画图");
                d.AppendLine("T+(s)          距首条的秒数，单调递增");
                d.AppendLine("MaxDpc(us)     该秒内 DPC 的最大执行时间（微秒）—— 曲线里那条主折线");
                d.AppendLine("AvgDpc(us)     该秒内 DPC 的平均执行时间（微秒）");
                d.AppendLine("MaxIsr(us)     该秒内 ISR 的最大执行时间（微秒）");
                d.AppendLine("Spikes         该秒内超过阈值的尖峰个数");
                d.AppendLine();
                d.AppendLine("参考阈值（微软官方对驱动的要求）：DPC 不超过 100us、ISR 不超过 25us；");
                d.AppendLine("1~3ms 视为警告，超过 3ms 视为错误。");
                File.WriteAllText(Path.Combine(dir, "ALPA_v2_Chart_" + stamp + "_列说明.txt"),
                    d.ToString(), new UTF8Encoding(true));
            }
            catch { }

            return f;
        }

        // ----------------------------------------------------------------- HTML

        /// <summary>
        /// 自绘矢量图表（HTML + 内联 SVG），含汇总统计、Top 秒与数据表。
        /// viewCount/offset 用来在图上标出"程序里当前看的那一段"，便于对照。
        /// </summary>
        public static string ExportHtml(string dir, List<TimelineChart.Sample> s, double warnUs, double critUs,
            int viewCount, int offset)
        {
            int n = s.Count;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string file = Path.Combine(dir, "ALPA_v2_Chart_" + stamp + ".html");

            // ---- 统计（用全量样本算，不用抽稀后的）----
            double maxDpc = 0, maxIsr = 0, sumAvg = 0;
            int worstDpcIdx = -1, worstIsrIdx = -1, overWarn = 0, overCrit = 0, spikeSum = 0;
            List<double> dpcs = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                TimelineChart.Sample x = s[i];
                dpcs.Add(x.MaxDpc);
                sumAvg += x.AvgDpc;
                spikeSum += x.Spikes;
                if (x.MaxDpc > maxDpc) { maxDpc = x.MaxDpc; worstDpcIdx = i; }
                if (x.MaxIsr > maxIsr) { maxIsr = x.MaxIsr; worstIsrIdx = i; }
                if (x.MaxDpc > critUs) overCrit++;
                else if (x.MaxDpc > warnUs) overWarn++;
            }
            double p50 = 0, p95 = 0, p99 = 0;
            if (n > 0)
            {
                dpcs.Sort();
                p50 = Pct(dpcs, 0.50); p95 = Pct(dpcs, 0.95); p99 = Pct(dpcs, 0.99);
            }

            // ---- 抽稀（按桶保留最大值，尖峰不会被抹掉）----
            int buckets = Math.Max(1, Math.Min(MaxBuckets, n));
            double[] bMax = new double[buckets], bAvg = new double[buckets], bIsr = new double[buckets];
            int[] bSpikes = new int[buckets];
            DateTime[] bT = new DateTime[buckets];
            for (int b = 0; b < buckets; b++)
            {
                int a = (int)((long)b * n / buckets);
                int e = (int)((long)(b + 1) * n / buckets);
                if (e <= a) e = Math.Min(n, a + 1);
                double mx = 0, mIsr = 0, sum = 0; int cnt = 0, sp = 0;
                for (int i = a; i < e && i < n; i++)
                {
                    if (s[i].MaxDpc > mx) mx = s[i].MaxDpc;
                    if (s[i].MaxIsr > mIsr) mIsr = s[i].MaxIsr;
                    sum += s[i].AvgDpc; cnt++; sp += s[i].Spikes;
                }
                bMax[b] = mx; bIsr[b] = mIsr; bAvg[b] = cnt > 0 ? sum / cnt : 0; bSpikes[b] = sp;
                bT[b] = n > 0 ? s[Math.Min(n - 1, a)].T : DateTime.Now;
            }

            // ---- 画布与坐标 ----
            int W = 1240, H = 430, pl = 74, pr = 22, pt = 34, pb = 40;
            int pw = W - pl - pr, ph = H - pt - pb;
            double top = Math.Max(critUs * 1.25, (maxDpc > maxIsr ? maxDpc : maxIsr) * 1.15);
            if (top < 200) top = 200;
            double step = NiceStep(top / 4.0);
            top = Math.Ceiling(top / step) * step;

            Func<double, double> yOf = delegate (double v) { return pt + ph - ph * (v / top); };
            Func<int, double> xOf = delegate (int i) { return buckets <= 1 ? pl + pw / 2.0 : pl + pw * (double)i / (buckets - 1); };

            StringBuilder g = new StringBuilder();
            // 网格 + 纵轴刻度
            for (double v = 0; v <= top + 1e-6; v += step)
            {
                double y = yOf(v);
                g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<line x1=\"{0}\" y1=\"{1:0.##}\" x2=\"{2}\" y2=\"{1:0.##}\" stroke=\"#e6e9ef\" stroke-width=\"1\"/>",
                    pl, y, pl + pw));
                string lab = v >= 1000 ? (v / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " ms"
                                        : v.ToString("0", CultureInfo.InvariantCulture) + " µs";
                g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<text x=\"{0}\" y=\"{1:0.##}\" text-anchor=\"end\" dominant-baseline=\"middle\" font-size=\"12\" fill=\"#6b7480\">{2}</text>",
                    pl - 8, y, lab));
            }
            // 阈值线
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<line x1=\"{0}\" y1=\"{1:0.##}\" x2=\"{2}\" y2=\"{1:0.##}\" stroke=\"#d98600\" stroke-width=\"1\" stroke-dasharray=\"5 4\"/>",
                pl, yOf(warnUs), pl + pw));
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0}\" y=\"{1:0.##}\" font-size=\"12\" fill=\"#d98600\">警告 {2:0} µs</text>",
                pl + pw - 4, yOf(warnUs) - 5, warnUs, pl));
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<line x1=\"{0}\" y1=\"{1:0.##}\" x2=\"{2}\" y2=\"{1:0.##}\" stroke=\"#d64545\" stroke-width=\"1\" stroke-dasharray=\"5 4\"/>",
                pl, yOf(critUs), pl + pw));
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0}\" y=\"{1:0.##}\" font-size=\"12\" fill=\"#d64545\">危险 {2:0} µs</text>",
                pl + pw - 4, yOf(critUs) - 5, critUs, pl));

            // 程序里当前查看的那一段（便于对照）
            if (n > 0 && viewCount > 0 && offset >= 0)
            {
                int vEnd = Math.Max(0, n - offset);
                int vStart = Math.Max(0, vEnd - viewCount);
                if (vEnd > vStart && (vStart > 0 || vEnd < n))
                {
                    double x1 = buckets <= 1 ? pl : pl + pw * (double)Math.Min(buckets - 1, vStart * buckets / Math.Max(1, n)) / (buckets - 1);
                    double x2 = buckets <= 1 ? pl + pw : pl + pw * (double)Math.Min(buckets - 1, vEnd * buckets / Math.Max(1, n)) / (buckets - 1);
                    g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<rect x=\"{0:0.##}\" y=\"{1}\" width=\"{2:0.##}\" height=\"{3}\" fill=\"#2f6df6\" opacity=\"0.07\"/>",
                        x1, pt, Math.Max(1.0, x2 - x1), ph));
                }
            }

            // 折线路径
            g.Append(PolyPath(bMax, xOf, yOf, "area", buckets));
            g.Append(PolyPath(bIsr, xOf, yOf, "isr", buckets));
            g.Append(PolyPath(bAvg, xOf, yOf, "avg", buckets));
            g.Append(PolyPath(bMax, xOf, yOf, "max", buckets));

            // 超过危险线的点 + 原生 tooltip
            for (int i = 0; i < buckets; i++)
            {
                if (bMax[i] <= critUs) continue;
                g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<circle cx=\"{0:0.##}\" cy=\"{1:0.##}\" r=\"4\" fill=\"#d64545\"><title>{2}\nDPC 最大 {3:0.#} µs\nDPC 均值 {4:0.#} µs\nISR 最大 {5:0.#} µs\n尖峰 {6}</title></circle>",
                    xOf(i), yOf(bMax[i]), bT[i].ToString("yyyy-MM-dd HH:mm:ss"), bMax[i], bAvg[i], bIsr[i], bSpikes[i]));
            }

            // 横轴时刻
            string ts0 = n > 0 ? s[0].T.ToString("MM-dd HH:mm:ss") : "-";
            string ts1 = n > 0 ? s[n - 1].T.ToString("MM-dd HH:mm:ss") : "-";
            string tsMid = n > 0 ? s[n / 2].T.ToString("MM-dd HH:mm") : "-";
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0}\" y=\"{1}\" font-size=\"12\" fill=\"#6b7480\">{2}</text>", pl, H - 12, ts0));
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0}\" y=\"{1}\" font-size=\"12\" fill=\"#6b7480\" text-anchor=\"middle\">{2}</text>", pl + pw / 2, H - 12, tsMid));
            g.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<text x=\"{0}\" y=\"{1}\" font-size=\"12\" fill=\"#6b7480\" text-anchor=\"end\">{2}</text>", pl + pw, H - 12, ts1));

            // ---- 数据表（抽稀后），放在可折叠区 ----
            StringBuilder tbl = new StringBuilder();
            tbl.AppendLine("<table><thead><tr><th>时刻</th><th>DPC 最大 (µs)</th><th>DPC 均值 (µs)</th><th>ISR 最大 (µs)</th><th>尖峰</th></tr></thead><tbody>");
            for (int i = 0; i < buckets; i++)
                tbl.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<tr><td>{0}</td><td>{1:0.#}</td><td>{2:0.#}</td><td>{3:0.#}</td><td>{4}</td></tr>",
                    bT[i].ToString("yyyy-MM-dd HH:mm:ss"), bMax[i], bAvg[i], bIsr[i], bSpikes[i]));
            tbl.AppendLine("</tbody></table>");

            // ---- 最坏的 20 秒 ----
            List<int> idx = new List<int>();
            for (int i = 0; i < n; i++) idx.Add(i);
            idx.Sort(delegate (int a, int b) { return s[b].MaxDpc.CompareTo(s[a].MaxDpc); });
            StringBuilder worst = new StringBuilder();
            worst.AppendLine("<table><thead><tr><th>#</th><th>时刻</th><th>DPC 最大 (µs)</th><th>DPC 均值 (µs)</th><th>ISR 最大 (µs)</th><th>尖峰</th></tr></thead><tbody>");
            for (int k = 0; k < idx.Count && k < 20; k++)
            {
                TimelineChart.Sample x = s[idx[k]];
                worst.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<tr><td>{0}</td><td>{1}</td><td>{2:0.#}</td><td>{3:0.#}</td><td>{4:0.#}</td><td>{5}</td></tr>",
                    k + 1, x.T.ToString("yyyy-MM-dd HH:mm:ss"), x.MaxDpc, x.AvgDpc, x.MaxIsr, x.Spikes));
            }
            worst.AppendLine("</tbody></table>");

            string span = n > 0 ? FmtSpan(s[n - 1].T - s[0].T) : "-";
            string worstDpcT = worstDpcIdx >= 0 ? s[worstDpcIdx].T.ToString("yyyy-MM-dd HH:mm:ss") : "-";
            string worstIsrT = worstIsrIdx >= 0 ? s[worstIsrIdx].T.ToString("yyyy-MM-dd HH:mm:ss") : "-";

            StringBuilder h = new StringBuilder();
            h.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            h.AppendLine("<title>ALPA v2 延迟时间线 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "</title>");
            h.AppendLine("<style>");
            h.AppendLine("body{font:14px/1.6 -apple-system,'Segoe UI','Microsoft YaHei',sans-serif;color:#1f2430;background:#f6f7fb;margin:0;padding:24px}");
            h.AppendLine(".wrap{max-width:1300px;margin:0 auto;background:#fff;border:1px solid #e6e9ef;border-radius:12px;padding:22px 26px}");
            h.AppendLine("h1{font-size:20px;margin:0 0 4px}h2{font-size:15px;margin:26px 0 10px;color:#2f6df6}");
            h.AppendLine(".sub{color:#6b7480;font-size:13px;margin-bottom:18px}");
            h.AppendLine(".cards{display:flex;flex-wrap:wrap;gap:12px;margin:8px 0 6px}");
            h.AppendLine(".c{flex:1 1 170px;border:1px solid #e6e9ef;border-radius:10px;padding:10px 14px;background:#fbfcfe}");
            h.AppendLine(".c b{display:block;font-size:12px;color:#6b7480;font-weight:400}.c .v{display:block;font-size:19px;font-weight:600;margin-top:2px}");
            h.AppendLine(".c i{display:block;font-size:12px;color:#8a90a0;font-style:normal;margin-top:2px}");
            h.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #eef1f5;padding:5px 8px;text-align:right}th:first-child,td:first-child{text-align:left}");
            h.AppendLine("th{background:#f4f6fa;color:#48505f;font-weight:600}details{margin-top:10px}summary{cursor:pointer;color:#2f6df6}");
            h.AppendLine(".legend{font-size:13px;color:#48505f;margin:10px 0 0}.legend span{display:inline-block;margin-right:18px}");
            h.AppendLine(".sw{display:inline-block;width:22px;height:3px;vertical-align:middle;margin-right:6px;border-radius:2px}");
            h.AppendLine("</style></head><body><div class=\"wrap\">");
            h.AppendLine("<h1>ALPA v2 延迟时间线</h1>");
            h.AppendLine("<div class=\"sub\">生成时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + " · 样本 " + n + " 秒（" + span + "）· 时间范围 " + ts0 + " ~ " + ts1
                + " · 本图由程序按数据重绘，非截图</div>");

            h.AppendLine("<div class=\"cards\">");
            h.AppendLine(Card("DPC 峰值", maxDpc.ToString("0.#") + " µs", worstDpcT, maxDpc >= critUs ? "crit" : ""));
            h.AppendLine(Card("DPC 均值(全期)", (n > 0 ? sumAvg / n : 0).ToString("0.##") + " µs", "P50 " + p50.ToString("0.#") + " · P95 " + p95.ToString("0.#") + " · P99 " + p99.ToString("0.#"), ""));
            h.AppendLine(Card("ISR 峰值", maxIsr.ToString("0.#") + " µs", worstIsrT, ""));
            h.AppendLine(Card("超过阈值", (overCrit + overWarn).ToString() + " 秒", "危险 " + overCrit + " 秒 · 警告 " + overWarn + " 秒", overCrit > 0 ? "crit" : ""));
            h.AppendLine(Card("尖峰总数", spikeSum.ToString(), "阈值 DPC≥" + warnUs.ToString("0") + "µs", ""));
            h.AppendLine("</div>");

            h.AppendLine("<div class=\"legend\">");
            h.AppendLine("<span><i class=\"sw\" style=\"background:#2f6df6\"></i>DPC 该秒最大</span>");
            h.AppendLine("<span><i class=\"sw\" style=\"background:#2f8ad6\"></i>DPC 该秒均值</span>");
            h.AppendLine("<span><i class=\"sw\" style=\"background:#d98600\"></i>ISR 该秒最大</span>");
            h.AppendLine("<span><i class=\"sw\" style=\"background:#d64545\"></i>超过危险线的点</span>");
            h.AppendLine("</div>");

            h.AppendLine("<svg viewBox=\"0 0 " + W + " " + H + "\" width=\"100%\" style=\"margin-top:10px\" font-family=\"-apple-system,'Segoe UI','Microsoft YaHei',sans-serif\">");
            h.AppendLine("<rect x=\"0\" y=\"0\" width=\"" + W + "\" height=\"" + H + "\" fill=\"#ffffff\"/>");
            h.Append(g);
            h.AppendLine("</svg>");

            if (n == 0)
                h.AppendLine("<p class=\"sub\">本次没有采集到样本（内核追踪需要管理员权限）。</p>");

            h.AppendLine("<h2>最坏的 20 秒</h2>");
            h.AppendLine(n > 0 ? worst.ToString() : "<p class=\"sub\">无数据</p>");
            h.AppendLine("<h2>完整数据（按 " + buckets + " 个桶抽稀，每桶保留最大值）</h2>");
            h.AppendLine("<details><summary>展开数据表</summary>" + tbl + "</details>");
            h.AppendLine("<p class=\"sub\" style=\"margin-top:18px\">逐秒原始数据请用同目录导出的 CSV（ALPA_v2_Chart_*.csv），可直接在 Excel/WPS 里画图。</p>");
            h.AppendLine("</div></body></html>");

            File.WriteAllText(file, h.ToString(), new UTF8Encoding(true));
            return file;
        }

        private static string Card(string title, string value, string sub, string cls)
        {
            string color = cls == "crit" ? " style=\"color:#d64545\"" : "";
            return "<div class=\"c\"><b>" + Esc(title) + "</b><span class=\"v\"" + color + ">" + Esc(value) + "</span><i>" + Esc(sub) + "</i></div>";
        }

        private static string PolyPath(double[] v, Func<int, double> xOf, Func<double, double> yOf, string kind, int n)
        {
            if (n < 1) return "";
            StringBuilder sb = new StringBuilder();
            string stroke, extra = "";
            switch (kind)
            {
                case "max": stroke = "#2f6df6"; break;
                case "avg": stroke = "#2f8ad6"; break;
                case "isr": stroke = "#d98600"; extra = " stroke-dasharray=\"4 3\""; break;
                default: stroke = ""; break;
            }
            sb.Append("<path d=\"");
            for (int i = 0; i < n; i++)
                sb.Append((i == 0 ? "M" : "L") + xOf(i).ToString("0.##", CultureInfo.InvariantCulture)
                    + " " + yOf(v[i]).ToString("0.##", CultureInfo.InvariantCulture) + " ");
            if (kind == "area")
                sb.Append("L" + xOf(n - 1).ToString("0.##", CultureInfo.InvariantCulture) + " " + (yOf(0) + 0).ToString("0.##", CultureInfo.InvariantCulture)
                    + " L" + xOf(0).ToString("0.##", CultureInfo.InvariantCulture) + " " + yOf(0).ToString("0.##", CultureInfo.InvariantCulture) + " Z");
            sb.Append("\" fill=\"" + (kind == "area" ? "#2f6df6" : "none") + "\""
                + (kind == "area" ? " opacity=\"0.12\"" : "")
                + " stroke=\"" + stroke + "\" stroke-width=\"" + (kind == "max" ? "2" : "1.4") + "\"" + extra + " stroke-linejoin=\"round\"/>");
            return sb.ToString() + "\n";
        }

        private static double Pct(List<double> sorted, double q)
        {
            if (sorted.Count == 0) return 0;
            int i = (int)Math.Ceiling(q * sorted.Count) - 1;
            if (i < 0) i = 0;
            if (i >= sorted.Count) i = sorted.Count - 1;
            return sorted[i];
        }

        private static string FmtSpan(TimeSpan t)
        {
            if (t.TotalHours >= 1) return t.TotalHours.ToString("0.#") + " 小时";
            if (t.TotalMinutes >= 1) return t.TotalMinutes.ToString("0.#") + " 分钟";
            return Math.Max(1, (int)t.TotalSeconds) + " 秒";
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // 与图表控件保持同一套刻度取整
        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / mag;
            double nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
            return nf * mag;
        }

        private static string Csv(params object[] f)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string v = f[i] == null ? "" : f[i].ToString();
                if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0)
                    v = "\"" + v.Replace("\"", "\"\"") + "\"";
                sb.Append(v);
            }
            return sb.ToString();
        }
    }
}
