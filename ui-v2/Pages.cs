using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ALP2
{
    internal abstract class PageBase : Panel
    {
        protected readonly Engine Eng;

        protected PageBase(Engine e)
        {
            Eng = e;
            DoubleBuffered = true;
            BackColor = Theme.Cur.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public abstract string NavLabel { get; }
        public abstract Icon NavIcon { get; }

        protected string HeadTitle = "";
        protected string HeadSub = "";
        protected string HeadRight = "";

        protected int Pad { get { return Theme.Px(14); } }
        protected int Gap { get { return Theme.Px(12); } }
        /// <summary>正文区起点。跟随 PageHead 的实测高度走，避免标题副标题被工具栏压住。</summary>
        protected int ContentTop { get { return PageHead.Height + Theme.Px(1); } }

        public virtual void OnSample(Snapshot s) { }
        public virtual void ApplyTheme() { Invalidate(); }

        protected abstract void DoLayout();

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > Theme.Px(60) && Height > Theme.Px(60)) DoLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Cur.Bg);
            PageHead.Paint(e.Graphics, HeadTitle, HeadSub, Pad, Theme.Px(4), Width - Pad * 2, HeadRight);
        }
    }

    /// <summary>两列「标签：数值」小卡，用于概览页底部。</summary>
    internal class MiniStats : SkinnedControl
    {
        public class KV
        {
            public string K = "";
            public string V = "";
            public Sev Sev = Sev.Neutral;
        }

        private List<KV> _rows = new List<KV>();
        private string _empty = "等待数据…";

        public MiniStats() { Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } }; }

        public void Set(List<KV> rows)
        {
            _rows = rows != null ? rows : new List<KV>();
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            if (_rows.Count == 0)
            {
                Draw.Text(g, _empty, Theme.F(8.8f, FontStyle.Regular), p.TextMuted, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            Font fk = Theme.F(8.6f, FontStyle.Regular);
            Font fv = Theme.Fm(8.8f, FontStyle.Bold);
            int fkH = Draw.LineH(fk);
            int fvH = Draw.LineH(fv);
            int gapx = Theme.Px(10);

            // 先量最大宽度：卡片窄的时候不能硬塞「左标签 + 右数值」，否则数值会被截成 "0…"
            int widestK = 0, widestV = 0;
            foreach (KV kv in _rows)
            {
                int w1 = Draw.Measure(kv.K, fk).Width;
                int w2 = Draw.Measure(kv.V, fv).Width;
                if (w1 > widestK) widestK = w1;
                if (w2 > widestV) widestV = w2;
            }
            bool stacked = widestK + widestV + gapx > Width;

            int y = Theme.Px(1);
            if (!stacked)
            {
                int rh = Math.Min(Theme.Px(26), Math.Max(Theme.Px(17), (Height - Theme.Px(2)) / Math.Max(1, _rows.Count)));
                rh = Math.Max(rh, fkH);
                int labelW = Math.Min(widestK + Theme.Px(3), Math.Max(Theme.Px(40), Width - widestV - gapx));
                foreach (KV kv in _rows)
                {
                    if (y + rh > Height) break;
                    Draw.Text(g, kv.K, fk, p.TextSec, new Rectangle(0, y, labelW, rh),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    Draw.Text(g, kv.V, fv, Theme.SevColor(kv.Sev), new Rectangle(labelW, y, Math.Max(1, Width - labelW), rh),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += rh;
                }
            }
            else
            {
                // 纵向两行：标签在上、数值在下，窄卡片下每个字都看得全
                int rowH = fkH + fvH + Theme.Px(3);
                foreach (KV kv in _rows)
                {
                    if (y + rowH > Height + Theme.Px(2)) break;
                    Draw.Text(g, kv.K, fk, p.TextSec, new Rectangle(0, y, Width, fkH),
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    Draw.Text(g, kv.V, fv, Theme.SevColor(kv.Sev), new Rectangle(0, y + fkH + Theme.Px(1), Width, fvH),
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += rowH;
                }
            }
        }
    }

    /// <summary>
    /// 尖峰流水（时间 / 驱动 / 时长）。
    ///
    /// 之前只能看到塞进来的最后几十条，更早的既滚动不到也没被传进来 ——
    /// 用户想回看历史只能去翻导出的 CSV。现在：①页面把引擎保留的完整列表传进来；
    /// ②控件自己带纵向滚动条与滚轮；③每行两行式排版，把「模块+偏移」也展示出来。
    /// </summary>
    internal class SpikeFeed : SkinnedControl
    {
        private List<SpikeRec> _items = new List<SpikeRec>();
        private string _empty = "还没有超过阈值的尖峰";
        private readonly VScrollBar _sb = new VScrollBar();
        private int _scroll;

        public SpikeFeed()
        {
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
            _sb.Width = Math.Max(11, Theme.Px(12));
            _sb.SmallChange = 1;
            _sb.Visible = false;
            _sb.ValueChanged += delegate { _scroll = _sb.Value; Invalidate(); };
            Controls.Add(_sb);
        }

        public void Set(List<SpikeRec> items, string empty)
        {
            // 锚定视口顶部那条尖峰：Set 每秒被采样调一次，如果在这里把 _scroll 清零，
            // 用户手动滚下去的位置 1 秒后就被拽回最上面（实测就是这样）。
            // 规则：停在顶部 = 跟随最新；滚到别处 = 锚住那一条，新数据来了也原地不动。
            int oldScroll = _scroll;
            string anchor = (oldScroll > 0 && oldScroll < _items.Count) ? KeyOf(_items[oldScroll]) : null;

            _items = items != null ? items : new List<SpikeRec>();
            _items.Reverse();          // 新的在上

            if (oldScroll <= 0 || anchor == null)
            {
                _scroll = 0;           // 本来就在顶部 → 继续跟随最新
            }
            else
            {
                int idx = -1;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (KeyOf(_items[i]) == anchor) { idx = i; break; }
                }
                // 锚点还在就回到它（新尖峰只会在顶部插入，老条目顺延）；
                // 锚点被 600 条上限挤掉了就保持原行号，交给 LayoutScroll 收敛
                _scroll = idx >= 0 ? idx : oldScroll;
            }

            if (empty != null) _empty = empty;
            LayoutScroll();
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        private static string KeyOf(SpikeRec r)
        {
            return r.AtSec.ToString("0.####") + "|" + r.Driver + "|" + r.Type + "|" + r.Us.ToString("0.#");
        }

        /// <summary>自检/测试用：模拟用户滚动到某一行。</summary>
        public void ScrollTo(int row) { _scroll = Math.Max(0, row); LayoutScroll(); }

        /// <summary>自检/测试用：当前视口顶部对应的条目序号（0 = 最新）。</summary>
        public int TopIndex { get { return _scroll; } }

        /// <summary>自检/测试用：当前视口顶部那条尖峰（判定"位置是否保持"用它，别用序号）。</summary>
        public SpikeRec TopItem
        {
            get { return (_scroll >= 0 && _scroll < _items.Count) ? _items[_scroll] : null; }
        }

        /// <summary>给宿主页面的滚轮转发入口（无焦点时滚轮消息到不了这里）。</summary>
        public void Wheel(int delta)
        {
            if (!_sb.Visible) return;
            _sb.Value = Math.Max(_sb.Minimum, Math.Min(_sb.Maximum, _sb.Value - Math.Sign(delta) * 3));
        }

        private int RowH { get { return Theme.Px(34); } }

        private void LayoutScroll()
        {
            _sb.SetBounds(Math.Max(0, Width - _sb.Width), 0, _sb.Width, Math.Max(1, Height));
            int view = Math.Max(1, (Height - Theme.Px(2)) / RowH);
            bool need = _items.Count > view;
            if (_sb.Visible != need) _sb.Visible = need;
            if (!need) { _scroll = 0; _sb.Value = 0; _sb.Maximum = 0; }
            else
            {
                _sb.Minimum = 0;
                _sb.Maximum = Math.Max(0, _items.Count - view);
                _sb.LargeChange = Math.Max(1, view);
                if (_scroll > _sb.Maximum) _scroll = _sb.Maximum;
                if (_scroll < 0) _scroll = 0;
                _sb.Value = _scroll;   // 每次都同步，别让滚动条和内部状态各走各的
            }
        }

        protected override void OnResize(EventArgs e)
        {
            LayoutScroll();
            Invalidate();
            base.OnResize(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            if (_items.Count == 0)
            {
                Draw.Text(g, _empty, Theme.F(8.8f, FontStyle.Regular), p.TextMuted, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                return;
            }

            Font ft = Theme.Fm(8.2f, FontStyle.Regular);
            Font fn = Theme.F(8.6f, FontStyle.Regular);
            Font fv = Theme.Fm(8.6f, FontStyle.Bold);
            int line1 = Math.Max(Draw.LineH(ft), Draw.LineH(fv));
            int line2 = Draw.LineH(fn);
            int rh = RowH;
            int sbw = _sb.Visible ? _sb.Width : 0;
            int timeW = Theme.Px(58);
            int valW = Theme.Px(67);
            int y = Theme.Px(1);

            g.SetClip(new Rectangle(0, 0, Math.Max(1, Width - sbw), Math.Max(1, Height)));
            for (int idx = _scroll; idx < _items.Count && y + rh <= Height + Theme.Px(2); idx++, y += rh)
            {
                SpikeRec r = _items[idx];
                Sev sv = Engine.GradeFor(r.Type, r.Us);
                Draw.Text(g, r.T.ToString("HH:mm:ss"), ft, p.TextMuted, new Rectangle(0, y, timeW, line1),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                Draw.Text(g, Fmt.Us(r.Us) + " µs", fv, Theme.SevColor(sv), new Rectangle(Width - sbw - valW, y, valW, line1),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                string nm = string.IsNullOrEmpty(r.Where) ? r.Driver : r.Where;
                Draw.Text(g, nm, fn, p.TextPri, new Rectangle(0, y + line1, Math.Max(Theme.Px(30), Width - sbw - Theme.Px(6)), line2),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                Draw.HLine(g, 0, Width - sbw, y + rh - 1, p.GridLine);
            }
            g.ResetClip();

            using (SolidBrush b = new SolidBrush(p.SurfaceAlt))
                g.FillRectangle(b, Width - sbw, 0, sbw, Height);
        }
    }

    /// <summary>选中驱动的详情：P50/P95/P99/Max 的分布形态一眼可见（长尾还是整体偏高）。</summary>
    internal class DriverDetail : SkinnedControl
    {
        private DriverStat _st;
        private string _empty = "在上方表格里点一行驱动，这里显示它的延迟分布";

        public DriverDetail() { Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } }; }

        public void Set(DriverStat st)
        {
            _st = st;
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);

            if (_st == null)
            {
                Draw.Text(g, _empty, Theme.F(8.8f, FontStyle.Regular), p.TextMuted, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }

            bool isr = string.Equals(_st.Type, "ISR", StringComparison.OrdinalIgnoreCase);
            Sev worst = Engine.GradeFor(_st.Type, _st.Max);

            Font fn = Theme.F(10.5f, FontStyle.Bold);
            Font ft = Theme.F(8.6f, FontStyle.Regular);
            Font fk = Theme.F(8.4f, FontStyle.Regular);
            Font fv = Theme.Fm(8.8f, FontStyle.Bold);
            Font fb = Theme.F(7.6f, FontStyle.Bold);

            int y = Theme.Px(2);
            Draw.Text(g, _st.Name, fn, p.TextPri, new Rectangle(0, y, Width - Theme.Px(66), fn.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            string badge = _st.Type + (isr ? "  阈值 500µs" : "  阈值 1000µs");
            Size bs = Draw.Measure(badge, fb);
            Rectangle br = new Rectangle(Width - bs.Width - Theme.Px(14), y + Theme.Px(1), bs.Width + Theme.Px(12), Theme.Px(17));
            Draw.FillRound(g, br, Theme.Px(8), Theme.SevSoft(worst), null);
            Draw.Text(g, badge, fb, Theme.SevColor(worst), br,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            y += fn.Height + Theme.Px(2);

            Draw.Text(g, "采集 " + Fmt.Count(_st.Count) + " 次 · 当前 " + Fmt.Us(_st.Cur) + " µs · 平均 " + Fmt.Us(_st.Avg) + " µs", ft, p.TextSec,
                new Rectangle(0, y, Width, ft.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += ft.Height + Theme.Px(8);

            // 四根相对长度的条：p50/p95/p99/max，直观区分「整体慢」和「偶尔抖」
            double[] vals = { _st.P50, _st.P95, _st.P99, _st.Max };
            string[] names = { "P50 中位", "P95", "P99", "Max 峰值" };
            double top = _st.Max > 0 ? _st.Max : 1;
            int labelW = Theme.Px(60);
            int valW = Theme.Px(72);
            int barX = labelW;
            int barW = Math.Max(Theme.Px(30), Width - labelW - valW - Theme.Px(6));
            for (int i = 0; i < 4; i++)
            {
                int rh = Theme.Px(22);
                Sev sv = Engine.GradeFor(_st.Type, vals[i]);
                Draw.Text(g, names[i], fk, p.TextSec, new Rectangle(0, y, labelW, rh),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                int bh = Theme.Px(8);
                int by = y + (rh - bh) / 2;
                Draw.FillRound(g, new Rectangle(barX, by, barW, bh), bh / 2, p.SurfaceAlt, null);
                int fw = (int)Math.Round(barW * Math.Max(0.015, Math.Min(1.0, vals[i] / top)));
                Draw.FillRound(g, new Rectangle(barX, by, Math.Max(bh, fw), bh), bh / 2, Theme.SevColor(sv), null);
                Draw.Text(g, Fmt.Us(vals[i]) + " µs", fv, Theme.SevColor(sv), new Rectangle(Width - valW, y, valW, rh),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                y += rh;
            }

            y += Theme.Px(4);
            string verdict;
            if (worst == Sev.Crit) verdict = "该驱动已产生超过阈值的延迟 —— 优先排查它的驱动版本 / 电源管理设置";
            else if (worst == Sev.Warn) verdict = "接近但未超过阈值，属于「需要注意」区间";
            else verdict = "延迟表现正常，不构成掉帧来源";
            Draw.Text(g, verdict, Theme.F(8f, FontStyle.Regular), Theme.SevColor(worst),
                new Rectangle(0, y, Width, Height - y),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>多段说明文字（自动换行）。</summary>
    internal class TextBlock : SkinnedControl
    {
        public string[] Lines = new string[0];
        public string Heading = "";

        public TextBlock() { Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } }; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            int y = Theme.Px(2);
            if (!string.IsNullOrEmpty(Heading))
            {
                Font fh = Theme.F(9.2f, FontStyle.Bold);
                Draw.Text(g, Heading, fh, p.TextPri, new Rectangle(0, y, Width, fh.Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                y += fh.Height + Theme.Px(6);
            }
            Font f = Theme.F(8.5f, FontStyle.Regular);
            foreach (string line in Lines)
            {
                Size sz = TextRenderer.MeasureText(g, line, f, new Size(Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                if (y + sz.Height > Height) break;
                Draw.Text(g, line, f, p.TextSec, new Rectangle(0, y, Width, sz.Height),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                y += sz.Height + Theme.Px(5);
            }
        }
    }

    // =========================================================================================
    //  概览
    // =========================================================================================
    internal class OverviewPage : PageBase
    {
        private readonly StatCard[] _stat = new StatCard[5];
        private readonly Card _cardChart = new Card();
        private readonly Card _cardTop = new Card();
        private readonly Card _cardSpike = new Card();
        private readonly Card[] _cardBot = new Card[4];
        private readonly TimelineChart _chart = new TimelineChart();
        private readonly BarList _top = new BarList();
        private readonly SpikeFeed _feed = new SpikeFeed();
        private readonly MiniStats[] _mini = new MiniStats[4];

        public OverviewPage(Engine e)
            : base(e)
        {
            HeadTitle = "概览";
            HeadSub = "延迟、抖动与系统负载的总览。左侧曲线是判断「抖动是否频繁」的核心视图。";

            for (int i = 0; i < _stat.Length; i++)
            {
                _stat[i] = new StatCard();
                Controls.Add(_stat[i]);
            }

            _cardChart.Title = "DPC / ISR 延迟时间线";
            _cardChart.Subtitle = "每秒取该秒内的最大与平均延迟；红点 = 超过危险线";
            _cardChart.TitleIcon = Icon.Pulse;
            _cardChart.Controls.Add(_chart);
            Controls.Add(_cardChart);

            _cardSpike.Title = "最近尖峰";
            _cardSpike.Subtitle = "超过阈值的事件，最新在最上";
            _cardSpike.TitleIcon = Icon.Warn;
            _cardSpike.Controls.Add(_feed);
            Controls.Add(_cardSpike);

            _cardTop.Title = "延迟最高的驱动";
            _cardTop.Subtitle = "按峰值排序，条形长度 = 相对最高值";
            _cardTop.TitleIcon = Icon.Layers;
            _cardTop.Controls.Add(_top);
            Controls.Add(_cardTop);

            string[] titles = { "磁盘活动", "内存与页面", "网络与中断", "输入与定时器" };
            Icon[] icons = { Icon.Layers, Icon.Gauge, Icon.Pulse, Icon.Grid };
            for (int i = 0; i < 4; i++)
            {
                _cardBot[i] = new Card();
                _cardBot[i].Title = titles[i];
                _cardBot[i].TitleIcon = icons[i];
                _mini[i] = new MiniStats();
                _cardBot[i].Controls.Add(_mini[i]);
                Controls.Add(_cardBot[i]);
            }
        }

        public override string NavLabel { get { return "概览"; } }
        public override Icon NavIcon { get { return Icon.Grid; } }

        protected override void DoLayout()
        {
            int x0 = Pad, w = Width - Pad * 2;
            if (w < Theme.Px(200)) return;
            int y = ContentTop;

            int statH = Theme.Px(104);
            int n = _stat.Length;
            int cardW = (w - Gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
                _stat[i].SetBounds(x0 + i * (cardW + Gap), y, cardW, statH);
            y += statH + Gap;

            int bottomH = Theme.Px(144);
            int rightW = Math.Min(Theme.Px(372), Math.Max(Theme.Px(286), w / 3));
            int leftW = w - rightW - Gap;
            int midH = Height - y - bottomH - Gap - Theme.Px(8);
            if (midH < Theme.Px(170)) midH = Theme.Px(170);

            int spikeH = Math.Max(Theme.Px(110), midH * 38 / 100);
            _cardSpike.SetBounds(x0 + leftW + Gap, y, rightW, spikeH);
            _cardTop.SetBounds(x0 + leftW + Gap, y + spikeH + Gap, rightW, midH - spikeH - Gap);
            _cardChart.SetBounds(x0, y, leftW, midH);
            y += midH + Gap;

            int bn = 4;
            int bw = (w - Gap * (bn - 1)) / bn;
            for (int i = 0; i < bn; i++)
                _cardBot[i].SetBounds(x0 + i * (bw + Gap), y, bw, bottomH);

            LayoutCardInner();
        }

        private void LayoutCardInner()
        {
            Inset(_cardChart, _chart);
            Inset(_cardSpike, _feed);
            Inset(_cardTop, _top);
            for (int i = 0; i < 4; i++) Inset(_cardBot[i], _mini[i]);
        }

        private static void Inset(Card c, Control inner)
        {
            int top = c.HeaderHeight;
            int pad = Theme.Px(12);
            inner.SetBounds(pad, top, Math.Max(Theme.Px(20), c.Width - pad * 2), Math.Max(Theme.Px(20), c.Height - top - pad + Theme.Px(4)));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCardInner();
        }

        public override void ApplyTheme()
        {
            base.ApplyTheme();
            BackColor = Theme.Cur.Bg;
        }

        public override void OnSample(Snapshot s)
        {
            // 指标卡展示「本会话」累计最大值；曲线图才用每秒口径（两者含义不同，别混用）
            Sev dsev = Engine.GradeDpc(s.SessionMaxDpc);
            _stat[0].Set("最高 DPC", Fmt.Us(s.SessionMaxDpc), "µs",
                string.IsNullOrEmpty(s.WorstDriver) ? "尚未捕获 DPC 事件" : "本会话累计 · 来自 " + s.WorstDriver, dsev);

            _stat[1].Set("最高 ISR", Fmt.Us(s.SessionMaxIsr), "µs",
                string.IsNullOrEmpty(s.WorstIsrDriver) ? "尚未捕获 ISR 事件" : "本会话累计 · 来自 " + s.WorstIsrDriver,
                Engine.GradeIsr(s.SessionMaxIsr));

            _stat[2].Set("DPC 速率", Fmt.Count((long)s.DpcPerSec), "/s",
                "ISR " + Fmt.Count((long)s.IsrPerSec) + "/s", Sev.Info);

            _stat[3].Set("定时器精度", s.TimerMs.ToString("0.0000", CultureInfo.InvariantCulture), "ms",
                "鼠标回报率 " + (s.MouseHz > 1 ? s.MouseHz.ToString("0") + " Hz" : "未检测到移动"),
                s.TimerMs <= 1.0 ? Sev.Ok : (s.TimerMs <= 2.0 ? Sev.Info : Sev.Warn));

            _stat[4].Set("尖峰计数", s.SpikeCount.ToString("#,0"), "次",
                "阈值 DPC ≥ " + Eng.DpcThreshold.ToString("0") + "µs",
                s.SpikeCount == 0 ? Sev.Ok : Sev.Warn);

            _chart.Push(s.MaxDpc, s.AvgDpc, s.MaxIsr, s.SpikesThisSec);
            _chart.WarnUs = Eng.DpcThreshold;
            _chart.CritUs = Eng.DpcThreshold * 2;

            // UI 只取最近窗口（归档仍是完整的，导出不受影响）
            _feed.Set(Eng.RecentSpikes(Engine.UiSpikeWindow), s.Tracing ? null : "未启用内核追踪（需要管理员权限）");

            BuildTop(s);
            BuildMini(s);
        }

        private void BuildTop(Snapshot s)
        {
            List<BarList.Item> items = new List<BarList.Item>();
            List<DriverStat> all = new List<DriverStat>();
            all.AddRange(s.Dpc);
            all.AddRange(s.Isr);
            // 峰值排行，但把只跑过一两次的细节过滤掉，否则全是噪声条目
            List<DriverStat> filt = new List<DriverStat>();
            foreach (DriverStat d in all) if (d.Count >= 3 && d.Max > 0) filt.Add(d);
            filt.Sort(delegate (DriverStat a, DriverStat b) { return b.Max.CompareTo(a.Max); });

            double top = filt.Count > 0 ? filt[0].Max : 1;
            for (int i = 0; i < Math.Min(7, filt.Count); i++)
            {
                DriverStat d = filt[i];
                BarList.Item it = new BarList.Item();
                it.Label = d.Name;
                it.Value = Fmt.Us(d.Max) + " µs";
                it.Sub = d.Type + " · P99 " + Fmt.Us(d.P99) + " · " + Fmt.Count(d.Count) + " 次";
                it.Fraction = d.Max / top;
                it.Sev = Engine.GradeFor(d.Type, d.Max);
                items.Add(it);
            }
            _top.Set(items, s.Tracing ? "还没有累积到足够的 DPC/ISR 事件" : "未启用内核追踪");
        }

        private void BuildMini(Snapshot s)
        {
            List<MiniStats.KV> d = new List<MiniStats.KV>();
            int dn = Math.Min(4, s.Disks.Count);
            for (int i = 0; i < dn; i++)
            {
                DiskRow r = s.Disks[i];
                MiniStats.KV kv = new MiniStats.KV();
                kv.K = r.Name;
                kv.V = r.LatencyMs.ToString("0.0") + "ms · " + r.ActivePct.ToString("0") + "%";
                kv.Sev = r.LatencyMs > 20 ? Sev.Warn : Sev.Ok;
                d.Add(kv);
            }
            _mini[0].Set(d);

            List<MiniStats.KV> m = new List<MiniStats.KV>();
            AddKV(m, "可用内存", Fmt.Mb(s.AvailMb), s.AvailMb < 1024 ? Sev.Crit : (s.AvailMb < 2048 ? Sev.Warn : Sev.Ok));
            AddKV(m, "待机缓存", Fmt.Mb(s.CacheMb), Sev.Neutral);
            AddKV(m, "缺页次数/秒", Fmt.Count((long)s.PageFaults), s.PageFaults > 5000 ? Sev.Warn : Sev.Ok);
            AddKV(m, "处理器队列", s.ProcQueue.ToString("0.0"), s.ProcQueue > 2 ? Sev.Warn : Sev.Ok);
            _mini[1].Set(m);

            List<MiniStats.KV> nw = new List<MiniStats.KV>();
            AddKV(nw, "网络吞吐", s.NetMbs.ToString("0.00") + " MB/s", s.NetMbs > 1 ? Sev.Info : Sev.Neutral);
            AddKV(nw, "中断/秒", Fmt.Count((long)s.InterruptsTotal), SeverityOfCores(s.CoreInterrupts));
            AddKV(nw, "上下文切换/秒", Fmt.Count((long)s.ContextSw), Sev.Neutral);
            AddKV(nw, "UDP 收包错误", s.UdpErr.ToString("0"), s.UdpErr > 0 ? Sev.Warn : Sev.Ok);
            _mini[2].Set(nw);

            List<MiniStats.KV> io = new List<MiniStats.KV>();
            AddKV(io, "定时器精度", s.TimerMs.ToString("0.000") + " ms", s.TimerMs <= 1 ? Sev.Ok : Sev.Info);
            AddKV(io, "鼠标回报率", s.MouseHz > 1 ? s.MouseHz.ToString("0") + " Hz" : "—", Sev.Neutral);
            AddKV(io, "DPC 事件/秒", Fmt.Count((long)s.DpcPerSec), Sev.Neutral);
            AddKV(io, "内核追踪", s.Tracing ? "运行中" : "已停止", s.Tracing ? Sev.Ok : Sev.Warn);
            _mini[3].Set(io);
        }

        private static Sev SeverityOfCores(float[] cores)
        {
            float max = 0;
            for (int i = 0; i < cores.Length; i++) if (cores[i] > max) max = cores[i];
            if (max > 20000) return Sev.Crit;
            if (max > 15000) return Sev.Warn;
            return Sev.Ok;
        }

        private static void AddKV(List<MiniStats.KV> list, string k, string v, Sev sev)
        {
            MiniStats.KV kv = new MiniStats.KV();
            kv.K = k; kv.V = v; kv.Sev = sev;
            list.Add(kv);
        }
    }

    // =========================================================================================
    //  DPC / ISR
    // =========================================================================================
    internal class DpcPage : PageBase
    {
        private readonly SearchBox _search = new SearchBox();
        private readonly Segmented _typeSeg;
        private readonly FlatButton _btnReset = new FlatButton();
        private readonly FlatButton _btnExport = new FlatButton();
        private readonly FlatButton _btnReport = new FlatButton();
        private readonly Card _cardTable = new Card();
        private readonly Card _cardDetail = new Card();
        private readonly Card _cardSpikes = new Card();
        private readonly TableView _table = new TableView();
        private readonly DriverDetail _detail = new DriverDetail();
        private readonly SpikeFeed _feed = new SpikeFeed();
        private readonly FlatButton _btnExportSpikes = new FlatButton();

        private int _typeFilter;   // 0 全部 1 DPC 2 ISR

        public DpcPage(Engine e)
            : base(e)
        {
            HeadTitle = "DPC / ISR 延迟";
            HeadSub = "内核态中断处理耗时。P99 才是判断「抖不抖」的关键，Max 只代表最极端的一次。";

            _search.Placeholder = "按驱动名筛选…";
            _search.Changed2 += delegate { ApplyFilter(); };
            Controls.Add(_search);

            _typeSeg = new Segmented(new string[] { "全部", "DPC", "ISR" }, new Icon[] { Icon.None, Icon.None, Icon.None });
            _typeSeg.SelectedChanged += delegate
            {
                _typeFilter = _typeSeg.Selected;
                ApplyFilter();
            };
            Controls.Add(_typeSeg);

            _btnReset.Text2 = "清零统计";
            _btnReset.Ico = Icon.Refresh;
            _btnReset.Click += delegate { Eng.ResetStats(); _table.ClearRows(); _detail.Set(null); _feed.Set(null, null); };
            Controls.Add(_btnReset);

            _btnExport.Text2 = "导出驱动 CSV";
            _btnExport.Ico = Icon.Export;
            _btnExport.Click += delegate { Export(true); };
            Controls.Add(_btnExport);

            _btnReport.Text2 = "导出报告";
            _btnReport.Ico = Icon.Info;
            _btnReport.Click += delegate { ExportReportFile(); };
            Controls.Add(_btnReport);

            _cardTable.Title = "驱动延迟表";
            _cardTable.Subtitle = "点表头排序、点一行看右侧分布";
            _cardTable.TitleIcon = Icon.Layers;
            _table.RowHeight = 26;
            _table.EmptyText = "还没有捕获到 DPC / ISR 事件";
            // AutoFit：列宽按「表头 + 实际内容」量出来，DPI 或字号一变也不会把 "P99" 截成 "P9…"
            _table.AddColumn(new TableColumn("驱动 / 模块", 200) { Flex = true, MinWidth = 145 });
            _table.AddColumn(new TableColumn("类型", 46).Fit(46, 96));
            _table.AddColumn(new TableColumn("事件数", 68, true).Fit(56, 100));
            _table.AddColumn(new TableColumn("P95 µs", 64, true).Fit(52, 96));
            _table.AddColumn(new TableColumn("P99 µs", 64, true).Fit(52, 96));
            _table.AddColumn(new TableColumn("最大 µs", 74, true).Fit(58, 100));
            _table.SelectionChanged += delegate { SyncDetail(); };
            _cardTable.Controls.Add(_table);
            Controls.Add(_cardTable);

            _cardDetail.Title = "选中驱动详情";
            _cardDetail.TitleIcon = Icon.Gauge;
            _cardDetail.Controls.Add(_detail);
            Controls.Add(_cardDetail);

            _cardSpikes.Title = "尖峰日志";
            _cardSpikes.Subtitle = "超过阈值的内核回调，按时间倒序";
            _cardSpikes.TitleIcon = Icon.Warn;
            _btnExportSpikes.Text2 = "导出";
            _btnExportSpikes.Ico = Icon.Export;
            _btnExportSpikes.Click += delegate { Export(false); };
            _cardSpikes.Controls.Add(_btnExportSpikes);
            _cardSpikes.Controls.Add(_feed);
            Controls.Add(_cardSpikes);

            _table.Filter = RowVisible;
        }

        public override string NavLabel { get { return "DPC / ISR 延迟"; } }
        public override Icon NavIcon { get { return Icon.Pulse; } }

        private bool RowVisible(TableRow r)
        {
            if (_typeFilter != 0)
            {
                string t = r.Cells.Length > 1 ? r.Cells[1] : "";
                if (_typeFilter == 1 && t != "DPC") return false;
                if (_typeFilter == 2 && t != "ISR") return false;
            }
            string q = _search.Value.Trim();
            if (q.Length > 0)
            {
                string nm = r.Cells.Length > 0 ? r.Cells[0] : "";
                if (nm.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        private void ApplyFilter()
        {
            _table.RefreshFilter();
        }

        private void ExportReportFile()
        {
            try
            {
                string f = Eng.ExportReport(Engine.AppDir);
                Eng.Write("report exported: " + f, LogLevel.Ok);
                MessageBox.Show(this, "已导出分析报告（结论/系统信息/DPC与ISR统计/每核心数据/周期判定）：\n" + f,
                    "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Export(bool drivers)
        {
            try
            {
                string dir = Engine.AppDir;
                string f = drivers ? Eng.ExportDriversCsv(dir) : Eng.ExportSpikesCsv(dir);
                Eng.Write("exported " + f, LogLevel.Ok);
                MessageBox.Show(this, "已导出：\n" + f, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void DoLayout()
        {
            int x0 = Pad, w = Width - Pad * 2;
            if (w < Theme.Px(200)) return;
            int y = ContentTop;

            int tbH = Theme.Px(30);
            int sx = x0;
            _search.SetBounds(sx, y, Theme.Px(210), tbH); sx += Theme.Px(210) + Gap;
            _typeSeg.SetBounds(sx, y, Theme.Px(168), tbH); sx += Theme.Px(168) + Gap;

            int rightBtn = x0 + w;
            _btnReport.SetBounds(rightBtn - Theme.Px(110), y, Theme.Px(110), tbH);
            _btnExport.SetBounds(_btnReport.Left - Theme.Px(136) - Gap, y, Theme.Px(136), tbH);
            _btnReset.SetBounds(_btnExport.Left - Theme.Px(104) - Gap, y, Theme.Px(104), tbH);
            y += tbH + Gap;

            int rightW = Math.Min(Theme.Px(372), Math.Max(Theme.Px(262), w * 33 / 100));
            int leftW = w - rightW - Gap;
            int midH = Height - y - Theme.Px(10);
            if (midH < Theme.Px(200)) midH = Theme.Px(200);

            _cardTable.SetBounds(x0, y, leftW, midH);
            int detailH = Math.Max(Theme.Px(196), midH * 45 / 100);
            _cardDetail.SetBounds(x0 + leftW + Gap, y, rightW, detailH);
            _cardSpikes.SetBounds(x0 + leftW + Gap, y + detailH + Gap, rightW, midH - detailH - Gap);

            Inner(_cardTable, _table);
            InnerHead(_cardDetail, _detail);
            InnerHead(_cardSpikes, _feed);
            _btnExportSpikes.SetBounds(_cardSpikes.Width - Theme.Px(74) - Theme.Px(12), Theme.Px(11), Theme.Px(74), Theme.Px(24));
        }

        private static void Inner(Card c, Control inner)
        {
            int pad = Theme.Px(10);
            inner.SetBounds(pad, c.HeaderHeight, Math.Max(Theme.Px(20), c.Width - pad * 2), Math.Max(Theme.Px(20), c.Height - c.HeaderHeight - pad));
        }

        private static void InnerHead(Card c, Control inner)
        {
            int pad = Theme.Px(12);
            int top = c.HeaderHeight;
            inner.SetBounds(pad, top, Math.Max(Theme.Px(20), c.Width - pad * 2), Math.Max(Theme.Px(20), c.Height - top - pad + Theme.Px(4)));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_table.Parent != null) Inner(_cardTable, _table);
            if (_detail.Parent != null) InnerHead(_cardDetail, _detail);
            if (_feed.Parent != null) InnerHead(_cardSpikes, _feed);
            _btnExportSpikes.SetBounds(_cardSpikes.Width - Theme.Px(74) - Theme.Px(12), Theme.Px(11), Theme.Px(74), Theme.Px(24));
        }

        /// <summary>
        /// 滚轮消息只发给有焦点的控件，而这些自绘控件默认不抢焦点 ——
        /// 所以在页面这一层接住滚轮，按光标位置转发给下面的表/列表。
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Point cur = PointToClient(Cursor.Position);
            if (_feed.Bounds.Contains(cur)) _feed.Wheel(e.Delta);
            else if (_table.Bounds.Contains(_table.PointToClient(Cursor.Position))) _table.Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        private void SyncDetail()
        {
            TableRow r = _table.SelectedRow;
            _detail.Set(r != null ? (r.Tag as DriverStat) : null);
        }

        public override void OnSample(Snapshot s)
        {
            List<TableRow> rows = new List<TableRow>();
            AddRows(rows, s.Dpc);
            AddRows(rows, s.Isr);
            _table.SetRows(rows);
            // 行对象每秒重建，但 SetRows 会按 RowKey 把选中项找回原位置
            SyncDetail();

            _feed.Set(Eng.RecentSpikes(Engine.UiSpikeWindow), s.Tracing ? null : "未启用内核追踪（需要管理员权限）");

            HeadRight = "累计尖峰 " + s.SpikeCount.ToString("#,0") + " 次";
            UpdatePeriodicity(s);
            Invalidate();
        }

        private int _periodTick;

        /// <summary>
        /// 把「尖峰是不是固定间隔、间隔多少」直接算出来写在卡片上。
        /// 每 5 秒算一次即可 —— 分析是 O(候选周期 × 尖峰数)，没必要每秒做。
        /// </summary>
        private void UpdatePeriodicity(Snapshot s)
        {
            _periodTick++;
            if (_periodTick % 5 != 1 && _periodTick != 1) return;

            string sub;
            if (s.SpikeCount == 0)
            {
                sub = "超过阈值的内核回调，按时间倒序";
            }
            else
            {
                PeriodResult pr = Eng.AnalyzePeriodsNow();
                double med, mn, mx;
                Eng.IntervalStats(out med, out mn, out mx);
                // 把「本列表只是最近一段」和「全会话最长是多少」都写在副标题上，
                // 否则用户会拿列表里的最大值去对比驱动表的"最大"，看起来像自相矛盾
                string head = "共 " + s.SpikeCount.ToString("#,0") + " 次";
                List<SpikeRec> top = Eng.TopSpikes;
                if (top.Count > 0)
                    head += " · 全会话最长 " + Fmt.Us(top[0].Us) + " µs（" + top[0].Driver + "）";
                head += " · 本列表仅最近 " + Engine.SpikeBufferSize + " 条";
                if (med > 0) head += " · 间隔中位 " + (med >= 1000 ? (med / 1000.0).ToString("0.##") + " s" : med.ToString("0") + " ms");
                sub = pr == null
                    ? head + " · 未发现固定间隔"
                    : head + " · 疑似周期 " + PeriodShort(pr.PeriodSec) + "（集中度 " + (pr.Score * 100).ToString("0") + "%）";
            }
            if (_cardSpikes.Subtitle != sub)
            {
                _cardSpikes.Subtitle = sub;
                _cardSpikes.Invalidate();
            }
        }

        private static string PeriodShort(double sec)
        {
            if (sec >= 120) return (sec / 60.0).ToString("0.#") + " 分钟";
            if (sec >= 1) return sec.ToString("0.##") + " 秒";
            return (sec * 1000).ToString("0") + " 毫秒";
        }

        private static void AddRows(List<TableRow> rows, List<DriverStat> src)
        {
            double maxAll = 1;
            foreach (DriverStat d in src) if (d.Max > maxAll) maxAll = d.Max;

            foreach (DriverStat d in src)
            {
                TableRow r = new TableRow();
                r.Cells = new string[]
                {
                    d.Name, d.Type, Fmt.Count(d.Count), Fmt.Us(d.P95), Fmt.Us(d.P99), Fmt.Us(d.Max)
                };
                r.Keys = new double[] { 0, 0, d.Count, d.P95, d.P99, d.Max };
                r.Bars = new double[] { 0, 0, 0, 0, 0, d.Max / maxAll };
                Color?[] cs = new Color?[6];
                cs[1] = Theme.Cur.TextSec;
                cs[2] = Theme.Cur.TextSec;
                cs[3] = Theme.SevColor(Engine.GradeFor(d.Type, d.P95));
                cs[4] = Theme.SevColor(Engine.GradeFor(d.Type, d.P99));
                cs[5] = Theme.SevColor(Engine.GradeFor(d.Type, d.Max));
                r.Colors = cs;
                r.Tag = d;
                r.RowKey = d.Name + "|" + d.Type;
                rows.Add(r);
            }
        }
    }
}
