# -*- coding: utf-8 -*-
"""第二部分：CSV 改逗号+引号转义；新增 LatencyMon 式报告导出。"""
import io

P = 'ui-v2/Engine.cs'
s = io.open(P, encoding='utf-8').read()

def rep(a, b, what):
    global s
    if a not in s:
        raise SystemExit('NOT FOUND [%s]:\n%s' % (what, a[:260]))
    s = s.replace(a, b, 1)
    print('ok:', what)

# ------------------------------------------------ 1. 驱动 CSV → 逗号 + 转义
rep('''        public string ExportDriversCsv(string dir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Driver;Type;Count;Current(us);Avg(us);P50(us);P95(us);P99(us);Max(us)");
            lock (_statLock)
            {
                Append(sb, _dpc);
                Append(sb, _isr);
            }''',
    '''        public string ExportDriversCsv(string dir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(CsvLine("Driver", "Type", "Count", "Current(us)", "Avg(us)", "P50(us)", "P95(us)", "P99(us)", "Max(us)"));
            lock (_statLock)
            {
                Append(sb, _dpc);
                Append(sb, _isr);
            }''', 'drivers header')

# ------------------------------------------------ 2. Append 行也走 CsvLine
rep('''        private static void Append(StringBuilder sb, Dictionary<string, DriverStat> d)
        {
            foreach (DriverStat s in list)
            {
                sb.AppendLine(string.Format("{0};{1};{2};{3:F2};{4:F2};{5:F2};{6:F2};{7:F2};{8:F2}",
                    s.Name, s.Type, s.Count, s.Cur, s.Avg, s.P50, s.P95, s.P99, s.Max));
            }
        }''',
    '''        private static void Append(StringBuilder sb, Dictionary<string, DriverStat> d)
        {
            List<DriverStat> list = new List<DriverStat>(d.Values);
            list.Sort(delegate (DriverStat a, DriverStat b) { return b.Max.CompareTo(a.Max); });
            foreach (DriverStat st in list)
            {
                sb.AppendLine(CsvLine(st.Name, st.Type, st.Count.ToString(),
                    st.Cur.ToString("F2"), st.Avg.ToString("F2"), st.P50.ToString("F2"),
                    st.P95.ToString("F2"), st.P99.ToString("F2"), st.Max.ToString("F2")));
            }
        }

        /// <summary>
        /// 标准_CSV 字段转义：含逗号/引号/换行的字段用引号包起来并把内部引号翻倍。
        ///
        /// 这里必须较真：分隔符选错，用户打开就是「整行挤在一列里」。
        /// 之前用分号，中文版 Excel 的列表分隔符是逗号，结果整份文件只显示一列 ——
        /// 所以这里统一用逗号，并按 RFC 4180 做转义。
        /// </summary>
        private static string CsvLine(params object[] fields)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string v = fields[i] == null ? "" : fields[i].ToString();
                bool need = v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\\n') >= 0 || v.IndexOf('\\r') >= 0;
                if (need) v = '"' + v.Replace("\\"", "\\"\\"") + '"';
                sb.Append(v);
            }
            return sb.ToString();
        }''', 'drivers rows + CsvLine')

io.open(P, 'w', encoding='utf-8').write(s)
print('Engine.cs part 2a done, len=%d' % len(s))
