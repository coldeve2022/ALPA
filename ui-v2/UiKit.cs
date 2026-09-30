using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ALP2
{
    internal enum Icon
    {
        None,
        Grid,
        Pulse,
        Layers,
        ShieldCheck,
        Rocket,
        Terminal,
        Sun,
        Moon,
        Play,
        Stop,
        Export,
        Folder,
        Refresh,
        SortAsc,
        SortDesc,
        Warn,
        Check,
        Cross,
        Info,
        Gauge
    }

    internal static class IconArt
    {
        /// <summary>全部图标用矢量画，避免依赖 Segoe MDL2 的私有码位（版本间会漂）。</summary>
        public static void Paint(Graphics g, Icon icon, Rectangle r, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(2f, r.Width / 9f);
            using (Pen p = new Pen(c, w))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                int x = r.X, y = r.Y, s = r.Width;
                int cx = r.X + s / 2, cy = r.Y + s / 2;
                switch (icon)
                {
                    case Icon.Grid:
                        g.DrawRectangle(p, x + s * 0.12f, y + s * 0.12f, s * 0.32f, s * 0.32f);
                        g.DrawRectangle(p, x + s * 0.56f, y + s * 0.12f, s * 0.32f, s * 0.32f);
                        g.DrawRectangle(p, x + s * 0.12f, y + s * 0.56f, s * 0.32f, s * 0.32f);
                        g.DrawRectangle(p, x + s * 0.56f, y + s * 0.56f, s * 0.32f, s * 0.32f);
                        break;
                    case Icon.Pulse:
                        {
                            float[] xs = { 0.05f, 0.30f, 0.42f, 0.58f, 0.72f, 0.95f };
                            float[] ys = { 0.55f, 0.55f, 0.18f, 0.86f, 0.50f, 0.50f };
                            PointF[] pts = new PointF[xs.Length];
                            for (int i = 0; i < xs.Length; i++) pts[i] = new PointF(x + s * xs[i], y + s * ys[i]);
                            g.DrawLines(p, pts);
                        }
                        break;
                    case Icon.Layers:
                        g.DrawLine(p, x + s * 0.5f, y + s * 0.10f, x + s * 0.88f, y + s * 0.32f);
                        g.DrawLine(p, x + s * 0.88f, y + s * 0.32f, x + s * 0.5f, y + s * 0.54f);
                        g.DrawLine(p, x + s * 0.5f, y + s * 0.54f, x + s * 0.12f, y + s * 0.32f);
                        g.DrawLine(p, x + s * 0.12f, y + s * 0.32f, x + s * 0.5f, y + s * 0.10f);
                        g.DrawLine(p, x + s * 0.12f, y + s * 0.50f, x + s * 0.5f, y + s * 0.72f);
                        g.DrawLine(p, x + s * 0.5f, y + s * 0.72f, x + s * 0.88f, y + s * 0.50f);
                        break;
                    case Icon.ShieldCheck:
                        {
                            PointF[] pts = new PointF[]
                            {
                                new PointF(cx, y + s * 0.08f),
                                new PointF(x + s * 0.86f, y + s * 0.24f),
                                new PointF(x + s * 0.86f, y + s * 0.52f),
                                new PointF(cx, y + s * 0.92f),
                                new PointF(x + s * 0.14f, y + s * 0.52f),
                                new PointF(x + s * 0.14f, y + s * 0.24f)
                            };
                            g.DrawPolygon(p, pts);
                            g.DrawLine(p, x + s * 0.33f, y + s * 0.52f, x + s * 0.46f, y + s * 0.66f);
                            g.DrawLine(p, x + s * 0.46f, y + s * 0.66f, x + s * 0.68f, y + s * 0.38f);
                        }
                        break;
                    case Icon.Rocket:
                        g.DrawLine(p, x + s * 0.20f, y + s * 0.80f, x + s * 0.62f, y + s * 0.38f);
                        g.DrawLine(p, x + s * 0.38f, y + s * 0.20f, x + s * 0.80f, y + s * 0.62f);
                        g.DrawLine(p, x + s * 0.62f, y + s * 0.38f, x + s * 0.80f, y + s * 0.62f);
                        g.DrawLine(p, x + s * 0.20f, y + s * 0.80f, x + s * 0.38f, y + s * 0.20f);
                        break;
                    case Icon.Terminal:
                        g.DrawRectangle(p, x + s * 0.10f, y + s * 0.16f, s * 0.80f, s * 0.68f);
                        {
                            PointF[] pts = new PointF[]
                            {
                                new PointF(x + s * 0.26f, y + s * 0.38f),
                                new PointF(x + s * 0.38f, y + s * 0.50f),
                                new PointF(x + s * 0.26f, y + s * 0.62f)
                            };
                            g.DrawLines(p, pts);
                        }
                        g.DrawLine(p, x + s * 0.50f, y + s * 0.64f, x + s * 0.74f, y + s * 0.64f);
                        break;
                    case Icon.Sun:
                        {
                            g.DrawEllipse(p, cx - s * 0.20f, cy - s * 0.20f, s * 0.40f, s * 0.40f);
                            for (int i = 0; i < 8; i++)
                            {
                                double a = i * Math.PI / 4.0;
                                float x1 = cx + (float)(Math.Cos(a) * s * 0.30f);
                                float y1 = cy + (float)(Math.Sin(a) * s * 0.30f);
                                float x2 = cx + (float)(Math.Cos(a) * s * 0.44f);
                                float y2 = cy + (float)(Math.Sin(a) * s * 0.44f);
                                g.DrawLine(p, x1, y1, x2, y2);
                            }
                        }
                        break;
                    case Icon.Moon:
                        {
                            g.DrawArc(p, x + s * 0.16f, y + s * 0.10f, s * 0.68f, s * 0.80f, 60f, 240f);
                            g.DrawArc(p, x + s * 0.30f, y + s * 0.02f, s * 0.66f, s * 0.94f, 110f, 150f);
                        }
                        break;
                    case Icon.Play:
                        g.DrawPolygon(p, new PointF[] {
                            new PointF(x + s * 0.28f, y + s * 0.18f),
                            new PointF(x + s * 0.82f, y + s * 0.50f),
                            new PointF(x + s * 0.28f, y + s * 0.82f) });
                        break;
                    case Icon.Stop:
                        g.DrawRectangle(p, x + s * 0.26f, y + s * 0.26f, s * 0.48f, s * 0.48f);
                        break;
                    case Icon.Export:
                        g.DrawLine(p, cx, y + s * 0.66f, cx, y + s * 0.14f);
                        g.DrawLine(p, x + s * 0.30f, y + s * 0.34f, cx, y + s * 0.14f);
                        g.DrawLine(p, x + s * 0.70f, y + s * 0.34f, cx, y + s * 0.14f);
                        g.DrawLine(p, x + s * 0.14f, y + s * 0.84f, x + s * 0.86f, y + s * 0.84f);
                        break;
                    case Icon.Folder:
                        g.DrawLines(p, new PointF[] {
                            new PointF(x + s * 0.10f, y + s * 0.80f),
                            new PointF(x + s * 0.10f, y + s * 0.24f),
                            new PointF(x + s * 0.44f, y + s * 0.24f),
                            new PointF(x + s * 0.52f, y + s * 0.34f),
                            new PointF(x + s * 0.90f, y + s * 0.34f),
                            new PointF(x + s * 0.90f, y + s * 0.80f),
                            new PointF(x + s * 0.10f, y + s * 0.80f) });
                        break;
                    case Icon.Refresh:
                        g.DrawArc(p, x + s * 0.14f, y + s * 0.14f, s * 0.72f, s * 0.72f, 40f, 280f);
                        g.DrawLines(p, new PointF[] {
                            new PointF(x + s * 0.62f, y + s * 0.06f),
                            new PointF(x + s * 0.90f, y + s * 0.26f),
                            new PointF(x + s * 0.60f, y + s * 0.38f) });
                        break;
                    case Icon.SortAsc:
                        g.DrawLine(p, cx, y + s * 0.72f, cx, y + s * 0.28f);
                        g.DrawLine(p, x + s * 0.30f, y + s * 0.48f, cx, y + s * 0.24f);
                        g.DrawLine(p, x + s * 0.70f, y + s * 0.48f, cx, y + s * 0.24f);
                        break;
                    case Icon.SortDesc:
                        g.DrawLine(p, cx, y + s * 0.28f, cx, y + s * 0.72f);
                        g.DrawLine(p, x + s * 0.30f, y + s * 0.52f, cx, y + s * 0.76f);
                        g.DrawLine(p, x + s * 0.70f, y + s * 0.52f, cx, y + s * 0.76f);
                        break;
                    case Icon.Check:
                        g.DrawLine(p, x + s * 0.20f, y + s * 0.54f, x + s * 0.42f, y + s * 0.76f);
                        g.DrawLine(p, x + s * 0.42f, y + s * 0.76f, x + s * 0.80f, y + s * 0.24f);
                        break;
                    case Icon.Cross:
                        g.DrawLine(p, x + s * 0.24f, y + s * 0.24f, x + s * 0.76f, y + s * 0.76f);
                        g.DrawLine(p, x + s * 0.76f, y + s * 0.24f, x + s * 0.24f, y + s * 0.76f);
                        break;
                    case Icon.Warn:
                        g.DrawLines(p, new PointF[] {
                            new PointF(cx, y + s * 0.12f),
                            new PointF(x + s * 0.92f, y + s * 0.84f),
                            new PointF(x + s * 0.08f, y + s * 0.84f),
                            new PointF(cx, y + s * 0.12f) });
                        g.DrawLine(p, cx, y + s * 0.40f, cx, y + s * 0.60f);
                        g.DrawLine(p, cx, y + s * 0.70f, cx, y + s * 0.74f);
                        break;
                    case Icon.Info:
                        g.DrawEllipse(p, x + s * 0.10f, y + s * 0.10f, s * 0.80f, s * 0.80f);
                        g.DrawLine(p, cx, y + s * 0.46f, cx, y + s * 0.70f);
                        g.DrawLine(p, cx, y + s * 0.30f, cx, y + s * 0.34f);
                        break;
                    case Icon.Gauge:
                        g.DrawArc(p, x + s * 0.10f, y + s * 0.20f, s * 0.80f, s * 0.80f, 180f, 180f);
                        g.DrawLine(p, cx, y + s * 0.60f, x + s * 0.70f, y + s * 0.32f);
                        break;
                }
            }
            g.SmoothingMode = old;
        }
    }

    /// <summary>所有自绘控件的基类：统一开启双缓冲，统一处理主题变更重绘。</summary>
    internal abstract class SkinnedControl : Control
    {
        protected SkinnedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Theme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(object s, EventArgs e)
        {
            try
            {
                if (!IsDisposed)
                {
                    if (InvokeRequired) BeginInvoke(new Action(Invalidate));
                    else Invalidate();
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }

    /// <summary>圆角卡片容器。子控件用 Padding 定位，卡片自己负责圆角描边。</summary>
    internal class Card : Panel
    {
        public int Radius = 10;
        public string Title = "";
        public string Subtitle = "";
        public bool DrawBorder = true;
        public Color? FillOverride = null;
        public Icon TitleIcon = Icon.None;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Theme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(object s, EventArgs e)
        {
            try { if (!IsDisposed) Invalidate(); }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        public int HeaderHeight
        {
            get
            {
                if (string.IsNullOrEmpty(Title)) return 0;
                int h = Theme.Px(12) + Draw.LineH(Theme.F(10.5f, FontStyle.Bold));
                if (!string.IsNullOrEmpty(Subtitle)) h += Draw.LineH(Theme.F(8.5f, FontStyle.Regular)) + Theme.Px(2);
                return h + Theme.Px(9);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Cur.Bg);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Draw.FillRound(g, r, Theme.Px(Radius), FillOverride.HasValue ? FillOverride.Value : Theme.Cur.Surface,
                DrawBorder ? (Color?)Theme.Cur.Border : null);

            int pad = Theme.Px(14);
            int y = pad - Theme.Px(2);
            if (!string.IsNullOrEmpty(Title))
            {
                Font ft = Theme.F(10.5f, FontStyle.Bold);
                int ftH = Draw.LineH(ft);
                int tx = pad;
                if (TitleIcon != Icon.None)
                {
                    int isz = Theme.Px(14);
                    IconArt.Paint(g, TitleIcon, new Rectangle(pad, y + (ftH - isz) / 2, isz, isz), Theme.Cur.Accent);
                    tx += isz + Theme.Px(7);
                }
                Draw.Text(g, Title, ft, Theme.Cur.TextPri, new Rectangle(tx, y, Width - tx - pad, ftH),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                y += ftH;
                if (!string.IsNullOrEmpty(Subtitle))
                {
                    Font fs = Theme.F(8.5f, FontStyle.Regular);
                    int fsH = Draw.LineH(fs);
                    Draw.Text(g, Subtitle, fs, Theme.Cur.TextSec, new Rectangle(pad, y, Width - pad * 2, fsH),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += fsH + Theme.Px(2);
                }
                y += Theme.Px(8);
            }
            // 标题下的一条极细分割线，比 v1 用大字号标题硬隔开更安静
            if (!string.IsNullOrEmpty(Title) && DrawBorder)
            {
                Draw.HLine(g, pad, Width - pad, y - Theme.Px(5), Theme.Cur.GridLine);
            }
        }
    }

    internal enum BtnKind
    {
        Primary,
        Ghost,
        Subtle,
        Danger
    }

    internal class FlatButton : SkinnedControl
    {
        private bool _hover;
        private bool _down;
        public BtnKind Kind = BtnKind.Ghost;
        public Icon Ico = Icon.None;
        public string Text2 = "";
        public bool Enabled2 = true;

        public FlatButton()
        {
            Cursor = Cursors.Hand;
            Height = Theme.Px(32);
            Font = Theme.F(9f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 父控件若是"透明"（本程序里就是 Card），用 Transparent 去 Clear 会画出黑底，
            // 所以退回到卡片底色 —— 卡片子控件上放按钮时必须走这一支。
            g.Clear(Parent != null && Parent.BackColor != Color.Transparent ? Parent.BackColor : Theme.Cur.Surface);
            Palette p = Theme.Cur;
            Color fill, text, border;

            switch (Kind)
            {
                case BtnKind.Primary:
                    fill = _hover ? p.AccentHover : p.Accent;
                    if (_down) fill = ControlPaint.Dark(fill, 0.06f);
                    text = p.TextOnAccent; border = fill;
                    break;
                case BtnKind.Danger:
                    fill = _hover ? Color.FromArgb(Math.Min(255, p.Crit.R + 18), p.Crit.G, p.Crit.B) : p.Crit;
                    text = Color.White; border = fill;
                    break;
                case BtnKind.Subtle:
                    fill = _hover ? p.SurfaceHover : p.SurfaceAlt;
                    text = p.TextPri; border = p.Border;
                    break;
                default: // Ghost
                    fill = _hover ? p.SurfaceHover : p.Surface;
                    text = p.TextPri; border = p.Border;
                    break;
            }
            if (!Enabled2) { fill = p.SurfaceAlt; text = p.TextMuted; border = p.Border; }

            Rectangle r = new Rectangle(0, 0, Width, Height);
            Draw.FillRound(g, r, Theme.Px(8), fill, border);

            // 按钮宽度是调用方按版面给的，窄屏下很容易放不下文字。
            // 两级自适应：先缩字号（下限 7.5pt），还不行就丢掉图标，最后才轮到省略号。
            int avail0 = Math.Max(Theme.Px(12), Width - Theme.Px(12));
            float pt = 9f;
            Font f = Theme.F(pt, FontStyle.Regular);
            int isz = Ico == Icon.None ? 0 : Theme.Px(13);
            Size ts = Draw.Measure(Text2, f);
            while (pt > 7.5f && ts.Width + (isz > 0 ? isz + Theme.Px(5) : 0) > avail0)
            {
                pt -= 0.5f;
                f = Theme.F(pt, FontStyle.Regular);
                ts = Draw.Measure(Text2, f);
            }
            if (isz > 0 && ts.Width + isz + Theme.Px(5) > avail0) isz = 0;

            int total = ts.Width + (isz > 0 ? isz + Theme.Px(5) : 0);
            int x = Math.Max(Theme.Px(5), (Width - total) / 2);
            if (isz > 0)
            {
                IconArt.Paint(g, Ico, new Rectangle(x, (Height - isz) / 2, isz, isz), text);
                x += isz + Theme.Px(5);
            }
            Draw.Text(g, Text2, f, text, new Rectangle(x, 0, Math.Max(1, Width - x - Theme.Px(5)), Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal class NavItem : SkinnedControl
    {
        private bool _hover;
        private bool _sel;
        public Icon Ico = Icon.None;
        public string Label = "";
        public string Badge = "";

        public bool Selected
        {
            get { return _sel; }
            set { if (_sel != value) { _sel = value; Invalidate(); } }
        }

        public NavItem()
        {
            Cursor = Cursors.Hand;
            Height = Theme.Px(42);
            Font = Theme.F(9.5f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(Parent != null ? Parent.BackColor : p.Sidebar);

            Rectangle r = new Rectangle(0, Theme.Px(2), Width - 1, Height - Theme.Px(4));
            Color fill = _sel ? p.AccentSoft : (_hover ? p.SurfaceHover : p.Sidebar);
            Draw.FillRound(g, r, Theme.Px(8), fill, null);

            if (_sel)
            {
                // 左侧 3px 选中指示条，比整块反白更克制
                Rectangle ind = new Rectangle(r.X, r.Y + Theme.Px(8), Theme.Px(3), r.Height - Theme.Px(16));
                Draw.FillRound(g, ind, Theme.Px(2), p.Accent, null);
            }

            Color fg = _sel ? p.Accent : p.TextSec;
            int isz = Theme.Px(17);
            int ix = r.X + Theme.Px(12);
            IconArt.Paint(g, Ico, new Rectangle(ix, r.Y + (r.Height - isz) / 2, isz, isz), fg);

            Font f = Theme.F(9.5f, _sel ? FontStyle.Bold : FontStyle.Regular);
            int lx = ix + isz + Theme.Px(10);
            int rw = r.Right - lx - Theme.Px(8);
            if (!string.IsNullOrEmpty(Badge))
            {
                Font fb = Theme.F(8f, FontStyle.Bold);
                Size bs = Draw.Measure(Badge, fb);
                int bw = bs.Width + Theme.Px(12);
                Rectangle br = new Rectangle(r.Right - Theme.Px(10) - bw, r.Y + (r.Height - Theme.Px(17)) / 2, bw, Theme.Px(17));
                Draw.FillRound(g, br, Theme.Px(8), _sel ? p.Accent : p.CritSoft, null);
                Draw.Text(g, Badge, fb, _sel ? p.TextOnAccent : p.Crit, br,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                rw = br.X - lx - Theme.Px(6);
            }
            Draw.Text(g, Label, f, _sel ? p.TextPri : p.TextSec, new Rectangle(lx, r.Y, Math.Max(1, rw), r.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (!Enabled) { }
        }
    }

    /// <summary>概览页的大数字指标卡。</summary>
    internal class StatCard : Panel
    {
        private string _value = "-";
        private string _unit = "";
        private string _caption = "";
        private string _hint = "";
        private Sev _sev = Sev.Neutral;

        public StatCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        public void Set(string caption, string value, string unit, string hint, Sev sev)
        {
            _caption = caption; _value = value; _unit = unit; _hint = hint; _sev = sev;
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Bg);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Draw.FillRound(g, r, Theme.Px(10), p.Surface, p.Border);

            // 左侧色条直接表达严重度，不用读数字也能一眼扫出问题
            Color accent = Theme.SevColor(_sev);
            Rectangle bar = new Rectangle(r.X, r.Y + Theme.Px(10), Theme.Px(4), r.Height - Theme.Px(20));
            Draw.FillRound(g, bar, Theme.Px(2), accent, null);

            int pad = Theme.Px(14);
            Font fc = Theme.F(8.5f, FontStyle.Regular);
            Font fv = Theme.Fm(18f, FontStyle.Bold);
            Font fu = Theme.F(9f, FontStyle.Regular);
            Font fh = Theme.F(8f, FontStyle.Regular);

            int y = pad - Theme.Px(4);
            int fcH = Draw.LineH(fc);
            Draw.Text(g, _caption, fc, p.TextSec, new Rectangle(pad + Theme.Px(6), y, Math.Max(Theme.Px(10), Width - pad - Theme.Px(8)), fcH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += fcH + Theme.Px(3);

            // 窄卡片下把大字号缩一点，别让单位和数值互相挤出去
            int avail = Math.Max(Theme.Px(20), Width - pad - Theme.Px(8));
            Size vs = Draw.Measure(_value, fv);
            float track = vs.Width + (string.IsNullOrEmpty(_unit) ? 0 : Draw.Measure(_unit, fu).Width + Theme.Px(8));
            if (track > avail)
            {
                float shrink = Math.Max(0.5f, avail / track);
                fv = Theme.Fm(18f * shrink, FontStyle.Bold);
                fu = Theme.F(9f * shrink, FontStyle.Regular);
                vs = Draw.Measure(_value, fv);
            }
            int fvH = Draw.LineH(fv);
            Draw.Text(g, _value, fv, accent, new Rectangle(pad + Theme.Px(6), y, Math.Max(Theme.Px(10), avail), fvH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(_unit))
            {
                int fuH = Draw.LineH(fu);
                int ux = pad + Theme.Px(8) + vs.Width + Theme.Px(3);
                int uw = Math.Max(Theme.Px(8), avail - (ux - pad - Theme.Px(6)));
                Draw.Text(g, _unit, fu, p.TextSec,
                    new Rectangle(ux, y + (fvH - fuH), uw, fuH),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            y += fvH + Theme.Px(2);
            // 卡片太矮时宁可不画提示行，也不要露出一截被切掉的文字
            int fhH = Draw.LineH(fh);
            if (y + fhH <= Height - Theme.Px(1))
            {
                Draw.Text(g, _hint, fh, p.TextMuted, new Rectangle(pad + Theme.Px(6), y, Math.Max(Theme.Px(10), Width - pad * 2), fhH),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>体检页/Banner 用的圆点状态行。</summary>
    internal class HealthRow : SkinnedControl
    {
        public string TitleText = "";
        public string ValueText = "";
        public string HintText = "";
        public Sev Sev = Sev.Neutral;
        private bool _hover;

        public HealthRow()
        {
            Height = Theme.Px(46);
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            if (_hover) { using (SolidBrush b = new SolidBrush(p.SurfaceHover)) g.FillRectangle(b, ClientRectangle); }
            Draw.HLine(g, 0, Width, Height - 1, p.GridLine);

            int dot = Theme.Px(9);
            int dy = (Height - dot) / 2;
            Color c = Theme.SevColor(Sev);
            using (SolidBrush b = new SolidBrush(c))
            {
                float old = 0;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, Theme.Px(12), dy, dot, dot);
                g.SmoothingMode = SmoothingMode.HighSpeed;
                if (old > 0) { }
            }

            Font ft = Theme.F(9.5f, FontStyle.Regular);
            Font fv = Theme.F(9.5f, FontStyle.Bold);
            Font fh = Theme.F(8f, FontStyle.Regular);

            int x = Theme.Px(12) + dot + Theme.Px(10);
            // 数值列宽按实测文字宽度给，别用一个固定的「宽度/3」把「未启用…」截掉；
            // 标题至少留 70，剩下的都给数值
            int valW = Math.Max(Theme.Px(56), Draw.Measure(ValueText, fv).Width + Theme.Px(6));
            int rightW = Math.Min(valW, Math.Max(Theme.Px(56), Width - x - Theme.Px(12) - Theme.Px(70)));
            int leftW = Math.Max(Theme.Px(40), Width - x - rightW - Theme.Px(12));

            if (string.IsNullOrEmpty(HintText))
            {
                Draw.Text(g, TitleText, ft, p.TextPri, new Rectangle(x, 0, leftW, Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                // 标题在上、提示在下，整体垂直居中。用实测行高自己排，别让两行互相压
                int titleH = Draw.LineH(ft);
                int hintH = Draw.LineH(fh);
                int top = Math.Max(0, (Height - titleH - hintH) / 2);
                Draw.Text(g, TitleText, ft, p.TextPri, new Rectangle(x, top, leftW, titleH),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                Draw.Text(g, HintText, fh, p.TextMuted, new Rectangle(x, top + titleH, leftW, hintH),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            Draw.Text(g, ValueText, fv, c, new Rectangle(Width - rightW - Theme.Px(12), 0, rightW, Height),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>浅色/深色切换用的分段控件。</summary>
    internal class Segmented : SkinnedControl
    {
        private int _sel = 0;
        private readonly string[] _items;
        private readonly Icon[] _icons;
        private int _hover = -1;

        public event EventHandler SelectedChanged;

        public Segmented(string[] items, Icon[] icons)
        {
            _items = items;
            _icons = icons;
            Height = Theme.Px(30);
            Cursor = Cursors.Hand;
            Width = Theme.Px(24) * items.Length + Theme.Px(40);
        }

        public int Selected
        {
            get { return _sel; }
            set
            {
                if (_sel != value && value >= 0 && value < _items.Length)
                {
                    _sel = value;
                    Invalidate();
                    if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int seg = Width / _items.Length;
            int h = Math.Min(_items.Length - 1, Math.Max(0, e.X / Math.Max(1, seg)));
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int seg = Width / _items.Length;
            Selected = Math.Min(_items.Length - 1, Math.Max(0, e.X / Math.Max(1, seg)));
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(Parent != null ? Parent.BackColor : p.Bg);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Draw.FillRound(g, r, Theme.Px(8), p.SurfaceAlt, p.Border);

            int seg = Width / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                Rectangle ir = new Rectangle(i * seg + Theme.Px(2), Theme.Px(2), seg - Theme.Px(4), Height - Theme.Px(4));
                if (i == _sel) Draw.FillRound(g, ir, Theme.Px(6), p.Surface, p.Border);
                else if (i == _hover) Draw.FillRound(g, ir, Theme.Px(6), p.SurfaceHover, null);

                Color fg = i == _sel ? p.Accent : p.TextSec;
                Font f = Theme.F(8.5f, i == _sel ? FontStyle.Bold : FontStyle.Regular);
                int isz = _icons != null && _icons[i] != Icon.None ? Theme.Px(12) : 0;
                Size ts = Draw.Measure(_items[i], f);
                int total = ts.Width + (isz > 0 ? isz + Theme.Px(5) : 0);
                int x = ir.X + (ir.Width - total) / 2;
                if (isz > 0)
                {
                    IconArt.Paint(g, _icons[i], new Rectangle(x, ir.Y + (ir.Height - isz) / 2, isz, isz), fg);
                    x += isz + Theme.Px(5);
                }
                Draw.Text(g, _items[i], f, fg, new Rectangle(x, ir.Y, ir.Width - (x - ir.X), ir.Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>圆角筛选输入框（TextBox 无边框内嵌，自己画外框和放大镜）。</summary>
    internal class SearchBox : SkinnedControl
    {
        private readonly TextBox _tb = new TextBox();
        public string Placeholder = "筛选…";
        public event EventHandler Changed2;

        public SearchBox()
        {
            Height = Theme.Px(30);
            _tb.BorderStyle = BorderStyle.None;
            _tb.Font = Theme.F(9f, FontStyle.Regular);
            _tb.BackColor = Theme.Cur.Surface;
            _tb.ForeColor = Theme.Cur.TextPri;
            _tb.TextChanged += delegate
            {
                Invalidate();
                if (Changed2 != null) Changed2(this, EventArgs.Empty);
            };
            Controls.Add(_tb);
        }

        public string Value
        {
            get { return _tb.Text; }
            set { _tb.Text = value; }
        }

        public void ApplyTheme()
        {
            _tb.BackColor = Theme.Cur.Surface;
            _tb.ForeColor = Theme.Cur.TextPri;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            // 内嵌 TextBox 的高度也要按 GDI 行高给，否则字号一大文字就被上下裁掉
            int h = Math.Max(Theme.Px(14), Draw.LineH(Theme.F(9f, FontStyle.Regular)));
            _tb.SetBounds(Theme.Px(29), Math.Max(0, (Height - h) / 2 - Theme.Px(1)),
                Math.Max(Theme.Px(20), Width - Theme.Px(38)), h);
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(Parent != null ? Parent.BackColor : p.Surface);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Draw.FillRound(g, r, Theme.Px(8), p.Surface, p.Border);

            int isz = Theme.Px(12);
            using (Pen pen = new Pen(p.TextMuted, 1.4f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(pen, Theme.Px(9), (Height - isz) / 2, isz - Theme.Px(2), isz - Theme.Px(2));
                g.DrawLine(pen, Theme.Px(9) + isz - Theme.Px(3), (Height + isz) / 2 - Theme.Px(3),
                    Theme.Px(9) + isz, (Height + isz) / 2);
                g.SmoothingMode = SmoothingMode.HighSpeed;
            }
            if (_tb.Text.Length == 0 && !_tb.Focused)
            {
                Draw.Text(g, Placeholder, Theme.F(9f, FontStyle.Regular), p.TextMuted,
                    new Rectangle(Theme.Px(27), 0, Width - Theme.Px(36), Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>顶部提示条（例如「未提权，内核追踪不可用」）。最多带两个操作按钮。</summary>
    internal class Banner : SkinnedControl
    {
        public string TitleText = "";
        public string BodyText = "";
        public Sev Sev = Sev.Warn;
        public FlatButton Action;
        public FlatButton Action2;

        public Banner()
        {
            Height = Theme.Px(58);
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        public void Set(string title, string body, Sev sev, string actionText)
        {
            Set(title, body, sev, actionText, null);
        }

        public void Set(string title, string body, Sev sev, string actionText, string action2Text)
        {
            TitleText = title; BodyText = body; Sev = sev;
            if (Action != null) { Controls.Remove(Action); Action.Dispose(); Action = null; }
            if (Action2 != null) { Controls.Remove(Action2); Action2.Dispose(); Action2 = null; }
            if (!string.IsNullOrEmpty(actionText))
            {
                Action = new FlatButton();
                Action.Kind = BtnKind.Primary;
                Action.Text2 = actionText;
                Action.Height = Theme.Px(28);
                Action.Width = Theme.Px(142);
                Controls.Add(Action);
            }
            if (!string.IsNullOrEmpty(action2Text))
            {
                Action2 = new FlatButton();
                Action2.Kind = BtnKind.Ghost;
                Action2.Text2 = action2Text;
                Action2.Height = Theme.Px(28);
                Action2.Width = Theme.Px(110);
                Controls.Add(Action2);
            }
            LayoutActions();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            LayoutActions();
            base.OnResize(e);
        }

        private void LayoutActions()
        {
            int x = Width - Theme.Px(12);
            if (Action != null)
            {
                Action.SetBounds(x - Action.Width, (Height - Action.Height) / 2, Action.Width, Action.Height);
                x -= Action.Width + Theme.Px(8);
            }
            if (Action2 != null)
            {
                Action2.SetBounds(x - Action2.Width, (Height - Action2.Height) / 2, Action2.Width, Action2.Height);
                x -= Action2.Width + Theme.Px(8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Bg);
            Color c = Theme.SevColor(Sev);
            Rectangle r = new Rectangle(Theme.Px(1), Theme.Px(4), Width - Theme.Px(2), Height - Theme.Px(8));
            Draw.FillRound(g, r, Theme.Px(8), Theme.SevSoft(Sev), c);

            int isz = Theme.Px(15);
            IconArt.Paint(g, Sev == Sev.Crit ? Icon.Warn : Icon.Info, new Rectangle(Theme.Px(14), (Height - isz) / 2, isz, isz), c);

            Font ft = Theme.F(9f, FontStyle.Bold);
            Font fb = Theme.F(8.4f, FontStyle.Regular);
            int x = Theme.Px(14) + isz + Theme.Px(9);
            int right = Theme.Px(14);
            if (Action != null) right += Action.Width + Theme.Px(8);
            if (Action2 != null) right += Action2.Width + Theme.Px(8);
            int w = Math.Max(Theme.Px(40), Width - x - right);

            // 标题 + 正文整体垂直居中，避免底部被圆角边框切掉半行字
            int ftH = Draw.LineH(ft);
            int fbH = Draw.LineH(fb);
            int textH = ftH + fbH + Theme.Px(1);
            int ty = Math.Max(Theme.Px(2), (Height - textH) / 2);
            Draw.Text(g, TitleText, ft, c, new Rectangle(x, ty, w, ftH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            Draw.Text(g, BodyText, fb, p.TextSec, new Rectangle(x, ty + ftH + Theme.Px(1), w, fbH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>页面标题区。高度由字体实测行高算出来，保证 Paint 与 Height 永远一致。</summary>
    internal static class PageHead
    {
        private static Font TitleFont { get { return Theme.F(15f, FontStyle.Bold); } }
        private static Font SubFont { get { return Theme.F(8.8f, FontStyle.Regular); } }

        public static void Paint(Graphics g, string title, string sub, int x, int y, int w, string right)
        {
            Palette p = Theme.Cur;
            Font ft = TitleFont;
            Font fs = SubFont;
            int ftH = Draw.LineH(ft);
            int fsH = Draw.LineH(fs);

            int ty = y + Theme.Px(4);
            Draw.Text(g, title, ft, p.TextPri, new Rectangle(x, ty, w, ftH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            int sy = ty + ftH + Theme.Px(1);
            Draw.Text(g, sub, fs, p.TextSec, new Rectangle(x, sy, w, fsH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (!string.IsNullOrEmpty(right))
            {
                Draw.Text(g, right, fs, p.TextMuted, new Rectangle(x, ty, w, ftH),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        public static int Height
        {
            get { return Theme.Px(4) + Draw.LineH(TitleFont) + Theme.Px(1) + Draw.LineH(SubFont) + Theme.Px(7); }
        }
    }
}
