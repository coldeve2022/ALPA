using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ALP2
{
    internal class TableColumn
    {
        public string Header = "";
        public int Width = 90;
        public bool Right;
        public bool Sortable = true;
        /// <summary>该列吸收剩余宽度（可以有多列，按 Width 作为权重分配）。</summary>
        public bool Flex;
        /// <summary>列宽下限（逻辑单位）。低于它就该出横向滚动条，而不是把内容截断。</summary>
        public int MinWidth = 42;
        /// <summary>列宽上限（0 = 不限）。AutoFit 时防止某一列吃掉整个表格。</summary>
        public int MaxWidth;
        /// <summary>按表头 + 实际单元格内容自动量出列宽，避免不同 DPI / 字号下文字被截成「类…」。</summary>
        public bool AutoFit;

        public TableColumn() { }
        public TableColumn(string h, int w) { Header = h; Width = w; MinWidth = w; }
        public TableColumn(string h, int w, bool right) { Header = h; Width = w; Right = right; MinWidth = w; }

        public TableColumn Fit(int min, int max)
        {
            AutoFit = true; MinWidth = min; MaxWidth = max;
            return this;
        }
    }

    internal class TableRow
    {
        public string[] Cells = new string[0];
        /// <summary>每格的文字颜色，null 表示用默认色。</summary>
        public Color?[] Colors;
        /// <summary>数值排序列的排序键（避免 "1.2k" 这类字符串比较出错）。</summary>
        public double[] Keys;
        /// <summary>每格背后的底色条，>0 时按 0..1 画一条浅色底纹。</summary>
        public double[] Bars;
        public Color? Tint;
        public object Tag;
        /// <summary>稳定的行标识。表格每秒重建行对象，靠它把「选中项」保持住。</summary>
        public string RowKey = "";

        public static TableRow Of(string[] cells)
        {
            TableRow r = new TableRow();
            r.Cells = cells;
            return r;
        }
    }

    /// <summary>
    /// 自绘表格。v1 用 ListView + GridLines + 黑底，格子线又重、文字又细，
    /// 而且每 1 秒要 EndUpdate 重排一次。这里改成：固定表头、斑马纹 + 悬停、
    /// 只有可见行才绘制、排序在控件内部完成（不再整表重建）。
    ///
    /// 列宽策略：AutoFit 列按「表头 + 所有单元格」实测宽度定宽；
    /// 总量超出可视宽度时不压缩文字，而是打开横向滚动条 —— 窄屏下宁可横向滚，
    /// 也不要把 PID / 内存这类关键数字截成 "4402…"。
    /// </summary>
    internal class TableView : SkinnedControl
    {
        private readonly List<TableColumn> _cols = new List<TableColumn>();
        private List<TableRow> _rows = new List<TableRow>();
        private readonly VScrollBar _sb = new VScrollBar();
        private readonly HScrollBar _hb = new HScrollBar();
        private int _scroll;    // 纵向：行
        private int _scrollX;   // 横向：像素
        private int _hoverRow = -1;
        private int _selRow = -1;
        private int _sortCol = -1;
        private bool _sortAsc;

        public int RowHeight = 26;
        public string EmptyText = "暂无数据";
        public bool ShowHeaderLine = true;
        public Func<TableRow, bool> Filter;

        public event EventHandler SortChanged;
        public event EventHandler SelectionChanged;
        public event EventHandler RowActivated;

        private static Font HeaderFont { get { return Theme.F(8.4f, FontStyle.Bold); } }
        private static Font CellFont { get { return Theme.F(8.8f, FontStyle.Regular); } }

        private static int CellPad { get { return Theme.Px(8); } }
        private static int HeaderH { get { return Theme.Px(29); } }
        private static int HBarH { get { return Math.Max(12, Theme.Px(13)); } }

        public TableView()
        {
            _sb.Width = Math.Max(12, Theme.Px(13));
            _sb.SmallChange = 1;
            _sb.Visible = false;
            _sb.ValueChanged += delegate { _scroll = _sb.Value; Invalidate(); };
            Controls.Add(_sb);

            _hb.Height = HBarH;
            _hb.SmallChange = Math.Max(8, Theme.Px(16));
            _hb.Visible = false;
            _hb.ValueChanged += delegate { _scrollX = _hb.Value; Invalidate(); };
            Controls.Add(_hb);

            Theme.Changed += delegate
            {
                try
                {
                    if (IsDisposed) return;
                    _sb.Width = Math.Max(12, Theme.Px(13));
                    _hb.Height = HBarH;
                    LayoutBars();
                    UpdateScrollBar();
                    Invalidate();
                }
                catch { }
            };
        }

        public int SortColumn { get { return _sortCol; } }
        public bool SortAscending { get { return _sortAsc; } }

        public TableRow SelectedRow
        {
            get
            {
                if (_selRow >= 0 && _selRow < _rows.Count) return _rows[_selRow];
                return null;
            }
        }

        public void AddColumn(TableColumn c)
        {
            _cols.Add(c);
            AutoWidth();
        }

        public void SetRows(List<TableRow> rows)
        {
            string keep = (_selRow >= 0 && _selRow < _rows.Count) ? _rows[_selRow].RowKey : null;

            // 滚动锚定：数据每秒重建、还会重新排序，只保住 _scroll 这个行号的话，
            // 用户看到的行会整体漂移（尤其排序键在变的时候）。用 RowKey 锚定视口顶部那行。
            string scrollKey = (_scroll > 0 && _scroll < _rows.Count && !string.IsNullOrEmpty(_rows[_scroll].RowKey))
                ? _rows[_scroll].RowKey : null;
            int oldScroll = _scroll;

            _rows = rows != null ? rows : new List<TableRow>();
            FitColumns();
            ApplySort();
            if (!string.IsNullOrEmpty(keep))
            {
                int found = -1;
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].RowKey == keep) { found = i; break; }
                }
                _selRow = found;
            }
            else if (_rows.Count == 0)
            {
                _selRow = -1;
            }
            ClampSelection();

            // 锚点回定位（放在排序之后，因为 RowKey 的新位置要等排序才确定）
            if (_scroll > 0 && oldScroll > 0)
            {
                if (!string.IsNullOrEmpty(scrollKey))
                {
                    int found = -1;
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        if (_rows[i].RowKey == scrollKey) { found = i; break; }
                    }
                    _scroll = found >= 0 ? found : Math.Min(oldScroll, Math.Max(0, _rows.Count - 1));
                }
                else
                {
                    _scroll = Math.Min(oldScroll, Math.Max(0, _rows.Count - 1));
                }
            }
            else if (oldScroll <= 0)
            {
                _scroll = 0;
            }

            AutoWidth();
            UpdateScrollBar();
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        public void ClearRows()
        {
            _rows.Clear();
            _selRow = -1;
            _hoverRow = -1;
            _scroll = 0;
            _scrollX = 0;
            if (_sb.Visible) _sb.Value = 0;
            if (_hb.Visible) _hb.Value = 0;
            UpdateScrollBar();
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        private List<TableRow> View()
        {
            if (Filter == null) return _rows;
            List<TableRow> outp = new List<TableRow>(_rows.Count);
            foreach (TableRow r in _rows) if (Filter(r)) outp.Add(r);
            return outp;
        }

        // ------------------------------------------------------------------ 列宽

        /// <summary>按内容实测列宽。只在数据变化时做一次，不在 OnPaint 里做。</summary>
        private void FitColumns()
        {
            if (_cols.Count == 0) return;
            Font fh = HeaderFont;
            Font fc = CellFont;
            int extra = CellPad * 2 + Theme.Px(10);  // 左右内边距 + GDI 内边距余量（留足，别让最后一两像素触发省略号）

            for (int i = 0; i < _cols.Count; i++)
            {
                TableColumn c = _cols[i];
                if (!c.AutoFit) continue;

                int max = Draw.GdiSize(c.Header, fh).Width;
                for (int r = 0; r < _rows.Count; r++)
                {
                    string[] cells = _rows[r].Cells;
                    if (cells == null || i >= cells.Length) continue;
                    string s = cells[i];
                    if (string.IsNullOrEmpty(s)) continue;
                    int w = Draw.GdiSize(s, fc).Width;
                    if (w > max) max = w;
                }

                int need = max + extra;
                int lo = Theme.Px(c.MinWidth);
                int hi = c.MaxWidth > 0 ? Theme.Px(c.MaxWidth) : int.MaxValue;
                if (need < lo) need = lo;
                if (need > hi) need = hi;
                c.Width = (int)Math.Ceiling(need / Math.Max(0.01f, Theme.S));
            }
        }

        private void AutoWidth()
        {
            if (Width <= 0) return;
            int vw = Width - (_sb.Visible ? _sb.Width : 0);

            int flexWeight = 0, flexCount = 0, fixedW = 0, minSum = 0;
            for (int i = 0; i < _cols.Count; i++)
            {
                TableColumn c = _cols[i];
                if (c.Flex) { flexCount++; flexWeight += Math.Max(1, c.Width); minSum += Theme.Px(c.MinWidth); }
                else fixedW += Theme.Px(c.Width);
            }

            int avail = vw - fixedW - Theme.Px(2);
            if (flexCount == 0) return;
            if (avail < minSum) avail = minSum;   // 装不下就溢出，由横向滚动条兜底

            int given = 0, lastFlex = -1;
            for (int i = 0; i < _cols.Count; i++)
            {
                TableColumn c = _cols[i];
                if (!c.Flex) continue;
                int w = (int)((long)avail * Math.Max(1, c.Width) / Math.Max(1, flexWeight));
                w = Math.Max(Theme.Px(c.MinWidth), w);
                c.Width = (int)Math.Round(w / Math.Max(0.01f, Theme.S));
                given += Theme.Px(c.Width);
                lastFlex = i;
            }
            // 取整误差补给最后一个 flex 列，避免表格右侧留下一条缝
            if (lastFlex >= 0 && given < avail)
            {
                TableColumn c = _cols[lastFlex];
                c.Width += (int)Math.Round((avail - given) / Math.Max(0.01f, Theme.S));
            }
        }

        private int TotalColumnWidth()
        {
            int t = 0;
            for (int i = 0; i < _cols.Count; i++) t += Theme.Px(_cols[i].Width);
            return t;
        }

        private void LayoutBars()
        {
            _sb.SetBounds(Math.Max(0, Width - _sb.Width), 0, _sb.Width, Math.Max(1, Height));
            _hb.SetBounds(0, Math.Max(0, Height - _hb.Height), Math.Max(1, Width), _hb.Height);
        }

        private void UpdateScrollBar()
        {
            LayoutBars();

            int viewH = Math.Max(1, Height - HeaderH - (_hb.Visible ? _hb.Height : 0));

            // 纵向
            int total = View().Count * Theme.Px(RowHeight);
            bool needV = total > viewH;
            if (_sb.Visible != needV) _sb.Visible = needV;
            if (!needV) { _scroll = 0; _sb.Value = 0; _sb.Maximum = 0; }
            else
            {
                int rh = Math.Max(1, Theme.Px(RowHeight));
                _sb.Minimum = 0;
                _sb.Maximum = Math.Max(0, (total - viewH) / rh + 2);
                _sb.LargeChange = Math.Max(1, viewH / rh);
                if (_scroll > _sb.Maximum) { _scroll = _sb.Maximum; _sb.Value = _sb.Maximum; }
            }

            // 横向：先把纵向滚动条占的宽度扣掉，再判断总列宽是否超出
            int viewW = Math.Max(1, Width - (_sb.Visible ? _sb.Width : 0));
            int totalW = TotalColumnWidth();
            bool needH = totalW > viewW;
            if (_hb.Visible != needH) _hb.Visible = needH;
            if (!needH) { _scrollX = 0; _hb.Value = 0; _hb.Maximum = 0; }
            else
            {
                _hb.Minimum = 0;
                _hb.Maximum = Math.Max(0, totalW - viewW + Theme.Px(4));
                _hb.LargeChange = Math.Max(1, viewW);
                _hb.SmallChange = Math.Max(8, Theme.Px(16));
                if (_scrollX > _hb.Maximum) { _scrollX = _hb.Maximum; _hb.Value = _hb.Maximum; }
            }
            LayoutBars();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            AutoWidth();
            UpdateScrollBar();
            base.OnSizeChanged(e);
        }

        protected override void OnResize(EventArgs e)
        {
            AutoWidth();
            UpdateScrollBar();
            base.OnResize(e);
        }

        // ------------------------------------------------------------------ 交互

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = RowAt(e.Y);
            if (idx != _hoverRow) { _hoverRow = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Y < HeaderH)
            {
                int c = ColAt(e.X);
                if (c >= 0 && _cols[c].Sortable)
                {
                    if (_sortCol == c) _sortAsc = !_sortAsc;
                    else { _sortCol = c; _sortAsc = false; }
                    ApplySort();
                    _scroll = 0;
                    if (_sb.Visible) _sb.Value = 0;
                    if (SortChanged != null) SortChanged(this, EventArgs.Empty);
                    Invalidate();
                }
                return;
            }
            int idx = RowAt(e.Y);
            if (idx >= 0)
            {
                _selRow = idx;
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        /// <summary>给宿主页面的滚轮转发入口（无焦点时滚轮消息到不了这里）。</summary>
        public void Wheel(int delta)
        {
            if (_hb.Visible) return;                 // 横向滚动优先时不动纵向
            if (!_sb.Visible) return;
            int nv = _sb.Value - Math.Sign(delta) * 3;
            _sb.Value = Math.Max(_sb.Minimum, Math.Min(_sb.Maximum, nv));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int idx = RowAt(e.Y);
            if (idx >= 0 && RowActivated != null)
            {
                _selRow = idx;
                RowActivated(this, EventArgs.Empty);
            }
            base.OnMouseDoubleClick(e);
        }

        private int ColAt(int x)
        {
            int cx = 0;
            int px = x + _scrollX;
            for (int i = 0; i < _cols.Count; i++)
            {
                int w = Theme.Px(_cols[i].Width);
                if (px >= cx && px < cx + w) return i;
                cx += w;
            }
            return -1;
        }

        private int RowAt(int y)
        {
            if (y < HeaderH) return -1;
            List<TableRow> v = View();
            int rh = Theme.Px(RowHeight);
            int idx = (y - HeaderH) / rh + _scroll;
            if (idx < 0 || idx >= v.Count) return -1;
            return idx;
        }

        // ------------------------------------------------------------------ 绘制

        private void ApplySort()
        {
            if (_sortCol < 0 || _cols.Count == 0) return;
            int c = _sortCol;
            bool hasKeys = true;
            foreach (TableRow r in _rows) { if (r.Keys == null || c >= r.Keys.Length) { hasKeys = false; break; } }

            Comparison<TableRow> cmp;
            if (hasKeys)
            {
                cmp = delegate (TableRow a, TableRow b) { return a.Keys[c].CompareTo(b.Keys[c]); };
            }
            else
            {
                cmp = delegate (TableRow a, TableRow b)
                {
                    string x = c < a.Cells.Length ? a.Cells[c] : "";
                    string y = c < b.Cells.Length ? b.Cells[c] : "";
                    return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                };
            }
            _rows.Sort(cmp);
            if (!_sortAsc) _rows.Reverse();
        }

        private void ClampSelection()
        {
            if (_selRow >= _rows.Count) _selRow = _rows.Count - 1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);

            int rh = Theme.Px(RowHeight);
            int hh = HeaderH;
            int vbW = _sb.Visible ? _sb.Width : 0;
            int hbH = _hb.Visible ? _hb.Height : 0;
            int contentW = Math.Max(1, Width - vbW);
            int contentH = Math.Max(1, Height - hbH);
            int ox = -_scrollX;

            // ---- 表头 ----
            using (SolidBrush b = new SolidBrush(p.SurfaceAlt)) g.FillRectangle(b, 0, 0, Width, hh);
            Draw.HLine(g, 0, Width, hh - 1, p.Border);

            Font fh = HeaderFont;
            g.SetClip(new Rectangle(0, 0, contentW, contentH));
            int x = ox;
            for (int i = 0; i < _cols.Count; i++)
            {
                int w = Theme.Px(_cols[i].Width);
                TableColumn c = _cols[i];
                TextFormatFlags flags = (c.Right ? TextFormatFlags.Right : TextFormatFlags.Left) |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                int arrowW = (_sortCol == i) ? Theme.Px(13) : 0;
                Rectangle tr = new Rectangle(x + CellPad, 0, Math.Max(1, w - CellPad * 2 - arrowW), hh);
                Draw.Text(g, c.Header, fh, _sortCol == i ? p.Accent : p.TextSec, tr, flags);
                if (_sortCol == i)
                {
                    int isz = Theme.Px(11);
                    int ix = c.Right ? x + CellPad : x + w - CellPad - isz;
                    IconArt.Paint(g, _sortAsc ? Icon.SortAsc : Icon.SortDesc,
                        new Rectangle(ix, (hh - isz) / 2, isz, isz), p.Accent);
                }
                if (i > 0) Draw.HLine(g, x, x, Theme.Px(8), p.Border);
                x += w;
            }

            // ---- 行 ----
            List<TableRow> v = View();
            if (v.Count == 0)
            {
                Font fe = Theme.F(9f, FontStyle.Regular);
                Draw.Text(g, EmptyText, fe, p.TextMuted,
                    new Rectangle(0, hh, contentW, Math.Max(1, contentH - hh)),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                g.ResetClip();
                PaintBarTracks(g, p, contentW, contentH, vbW, hbH);
                return;
            }

            Font fr = CellFont;
            g.SetClip(new Rectangle(0, hh, contentW, Math.Max(1, contentH - hh)));

            int first = Math.Max(0, _scroll);
            int visible = (contentH - hh) / Math.Max(1, rh) + 2;
            int last = Math.Min(v.Count, first + visible);

            using (SolidBrush zebra = new SolidBrush(p.SurfaceAlt))
            using (SolidBrush hoverB = new SolidBrush(p.SurfaceHover))
            using (SolidBrush selB = new SolidBrush(p.Selection))
            {
                for (int i = first; i < last; i++)
                {
                    TableRow row = v[i];
                    int y = hh + (i - _scroll) * rh;
                    Rectangle rr = new Rectangle(0, y, Width, rh);

                    if (i == _selRow) g.FillRectangle(selB, rr);
                    else if (i == _hoverRow) g.FillRectangle(hoverB, rr);
                    else if ((i & 1) == 1) g.FillRectangle(zebra, rr);
                    else if (row.Tint.HasValue) { using (SolidBrush tb = new SolidBrush(row.Tint.Value)) g.FillRectangle(tb, rr); }

                    if (i == _selRow)
                    {
                        using (SolidBrush ab = new SolidBrush(p.Accent)) g.FillRectangle(ab, new Rectangle(0, y, Theme.Px(3), rh));
                    }

                    int cx = ox;
                    for (int c = 0; c < _cols.Count && c < row.Cells.Length; c++)
                    {
                        int w = Theme.Px(_cols[c].Width);

                        // 数值底纹条：不用读数字也能看出谁大谁小
                        if (row.Bars != null && c < row.Bars.Length && row.Bars[c] > 0)
                        {
                            double frac = Math.Max(0, Math.Min(1.0, row.Bars[c]));
                            Color bc = row.Colors != null && c < row.Colors.Length && row.Colors[c].HasValue
                                ? row.Colors[c].Value : p.Accent;
                            using (SolidBrush bb = new SolidBrush(Color.FromArgb(38, bc)))
                            {
                                int bw = (int)Math.Round((w - CellPad) * frac);
                                int by = y + Math.Max(1, rh / 6);
                                int bh = Math.Max(2, rh - Math.Max(2, rh / 3));
                                if (_cols[c].Right) g.FillRectangle(bb, new Rectangle(cx + w - CellPad / 2 - bw, by, bw, bh));
                                else g.FillRectangle(bb, new Rectangle(cx + CellPad / 2, by, bw, bh));
                            }
                        }

                        Color fg = row.Colors != null && c < row.Colors.Length && row.Colors[c].HasValue
                            ? row.Colors[c].Value : (i == _selRow ? p.SelectionText : p.TextPri);
                        Rectangle tr = new Rectangle(cx + CellPad, y, Math.Max(1, w - CellPad * 2), rh);
                        TextFormatFlags flags = (_cols[c].Right ? TextFormatFlags.Right : TextFormatFlags.Left) |
                            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                        Draw.Text(g, row.Cells[c], fr, fg, tr, flags);
                        cx += w;
                    }
                    Draw.HLine(g, 0, Width, y + rh - 1, p.GridLine);
                }
            }
            g.ResetClip();
            PaintBarTracks(g, p, contentW, contentH, vbW, hbH);
        }

        private void PaintBarTracks(Graphics g, Palette p, int contentW, int contentH, int vbW, int hbH)
        {
            if (vbW > 0)
            {
                using (SolidBrush b = new SolidBrush(p.SurfaceAlt)) g.FillRectangle(b, contentW, 0, vbW, Height);
            }
            if (hbH > 0)
            {
                using (SolidBrush b = new SolidBrush(p.SurfaceAlt)) g.FillRectangle(b, 0, contentH, Width, hbH);
            }
        }

        /// <summary>筛选条件变化后重建滚动条范围。</summary>
        public void RefreshFilter()
        {
            _scroll = 0;
            if (_sb.Visible) _sb.Value = 0;
            UpdateScrollBar();
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }
    }
}
