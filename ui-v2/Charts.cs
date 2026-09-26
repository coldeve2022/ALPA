using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ALP2
{
    /// <summary>
    /// DPC/ISR 延迟滚动时间线。
    /// v1 只有一堆滚动的数字文本，看不出「什么时候抖了一下」——
    /// 这个是整个工具最有价值的视图：能同时看到基线、均值和尖峰。
    /// </summary>
    internal class TimelineChart : SkinnedControl
    {
        public struct Sample
        {
            public DateTime T;
            public double MaxDpc;
            public double AvgDpc;
            public double MaxIsr;
            public int Spikes;
        }

        public int Capacity = 120;          // 保留最近 N 秒
        public double WarnUs = 500;         // DPC 警戒线
        public double CritUs = 1000;        // DPC 危险线
        public bool ShowIsr = true;

        private readonly List<Sample> _pts = new List<Sample>();
        private int _hoverIdx = -1;

        public TimelineChart()
        {
            MinimumSize = new Size(Theme.Px(240), Theme.Px(140));
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        public void Push(double maxDpc, double avgDpc, double maxIsr, int spikes)
        {
            lock (_pts)
            {
                _pts.Add(new Sample
                {
                    T = DateTime.Now,
                    MaxDpc = maxDpc,
                    AvgDpc = avgDpc,
                    MaxIsr = maxIsr,
                    Spikes = spikes
                });
                while (_pts.Count > Capacity) _pts.RemoveAt(0);
            }
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        public void Clear()
        {
            lock (_pts) _pts.Clear();
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        public int Count { get { lock (_pts) return _pts.Count; } }

        private string _empty = "等待内核追踪数据…（需要以管理员身份运行）";
        public string EmptyText { get { return _empty; } set { _empty = value; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Sample[] arr;
            lock (_pts) arr = _pts.ToArray();
            if (arr.Length == 0) { base.OnMouseMove(e); return; }
            Rectangle plot = PlotRect();
            if (plot.Width <= 0) { base.OnMouseMove(e); return; }
            int gap = Math.Max(1, plot.Width / Math.Max(1, arr.Length - 1));
            int idx = (int)Math.Round((e.X - plot.X) / (double)gap);
            idx = Math.Min(arr.Length - 1, Math.Max(0, idx));
            if (idx != _hoverIdx) { _hoverIdx = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIdx = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        private Rectangle PlotRect()
        {
            return new Rectangle(Theme.Px(52), Theme.Px(26), Math.Max(1, Width - Theme.Px(52) - Theme.Px(14)),
                                 Math.Max(1, Height - Theme.Px(26) - Theme.Px(26)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            Rectangle plot = PlotRect();

            Sample[] arr;
            lock (_pts) arr = _pts.ToArray();

            // ---- 纵轴刻度 ----
            double dataMax = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].MaxDpc > dataMax) dataMax = arr[i].MaxDpc;
                if (ShowIsr && arr[i].MaxIsr > dataMax) dataMax = arr[i].MaxIsr;
            }
            double top = Math.Max(CritUs * 1.25, dataMax * 1.15);
            if (top < 200) top = 200;
            double step = NiceStep(top / 4.0);
            top = Math.Ceiling(top / step) * step;

            Font fa = Theme.F(7.6f, FontStyle.Regular);
            for (double v = 0; v <= top + 1e-6; v += step)
            {
                int y = plot.Bottom - (int)Math.Round(plot.Height * (v / top));
                Draw.HLine(g, plot.X, plot.Right, y, p.GridLine);
                Draw.Text(g, v >= 1000 ? (v / 1000.0).ToString("0.#") + "ms" : v.ToString("0") + "µs", fa, p.TextMuted,
                    new Rectangle(0, y - fa.Height / 2, Theme.Px(48), fa.Height),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // ---- 阈值线 ----
            DrawThreshold(g, plot, top, WarnUs, p.Warn, "警戒 " + WarnUs.ToString("0") + "µs");
            DrawThreshold(g, plot, top, CritUs, p.Crit, "危险 " + CritUs.ToString("0") + "µs");

            // ---- 图例 ----
            int lx = plot.X;
            lx = Legend(g, lx, Theme.Px(4), p.Accent, "DPC 峰值");
            lx = Legend(g, lx, Theme.Px(4), p.Info, "DPC 均值");
            if (ShowIsr) lx = Legend(g, lx, Theme.Px(4), p.Warn, "ISR 峰值");

            if (arr.Length < 2)
            {
                Font fe = Theme.F(9f, FontStyle.Regular);
                Draw.Text(g, _empty, fe, p.TextMuted, plot,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            // ---- 数据折线 ----
            int n = arr.Length;
            float gapPx = plot.Width / (float)(Capacity - 1);
            float startX = plot.Right - gapPx * (n - 1);

            PointF[] maxPts = new PointF[n];
            PointF[] avgPts = new PointF[n];
            PointF[] isrPts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                float x = startX + gapPx * i;
                maxPts[i] = new PointF(x, YOf(arr[i].MaxDpc, plot, top));
                avgPts[i] = new PointF(x, YOf(arr[i].AvgDpc, plot, top));
                isrPts[i] = new PointF(x, YOf(arr[i].MaxIsr, plot, top));
            }

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(plot);

            // DPC 峰值线下方的淡色面积
            if (n >= 2)
            {
                PointF[] area = new PointF[n + 2];
                Array.Copy(maxPts, area, n);
                area[n] = new PointF(maxPts[n - 1].X, plot.Bottom);
                area[n + 1] = new PointF(maxPts[0].X, plot.Bottom);
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(plot.X, plot.Y, Math.Max(1, plot.Width), Math.Max(1, plot.Height)),
                    Color.FromArgb(64, p.Accent), Color.FromArgb(6, p.Accent), 90f))
                {
                    g.FillPolygon(br, area);
                }
            }

            if (ShowIsr && n >= 2)
            {
                using (Pen pen = new Pen(Color.FromArgb(170, p.Warn), Theme.Px(1) + 0.4f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawLines(pen, isrPts);
                }
            }
            if (n >= 2)
            {
                using (Pen pen = new Pen(Color.FromArgb(190, p.Info), Theme.Px(1) + 0.6f))
                    g.DrawLines(pen, avgPts);
                using (Pen pen = new Pen(p.Accent, Theme.Px(2)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, maxPts);
                }
            }

            // ---- 尖峰标记 ----
            using (SolidBrush br = new SolidBrush(p.Crit))
            {
                for (int i = 0; i < n; i++)
                {
                    if (arr[i].MaxDpc > CritUs)
                        g.FillEllipse(br, maxPts[i].X - Theme.Px(3), maxPts[i].Y - Theme.Px(3), Theme.Px(6), Theme.Px(6));
                }
            }
            g.ResetClip();

            // ---- 悬停十字线 + 提示框 ----
            if (_hoverIdx >= 0 && _hoverIdx < n)
            {
                float x = maxPts[_hoverIdx].X;
                using (Pen pen = new Pen(p.BorderStrong, 1f) { DashStyle = DashStyle.Dot })
                    g.DrawLine(pen, x, plot.Y, x, plot.Bottom);
                g.FillEllipse(new SolidBrush(p.Accent), x - Theme.Px(3), maxPts[_hoverIdx].Y - Theme.Px(3), Theme.Px(6), Theme.Px(6));

                string l1 = "DPC 峰值 " + Fmt.Us(arr[_hoverIdx].MaxDpc) + " µs";
                string l2 = "DPC 均值 " + Fmt.Us(arr[_hoverIdx].AvgDpc) + " µs";
                string l3 = "ISR 峰值 " + Fmt.Us(arr[_hoverIdx].MaxIsr) + " µs";
                string l4 = Fmt.Time(arr[_hoverIdx].T) + (arr[_hoverIdx].Spikes > 0 ? "  尖峰×" + arr[_hoverIdx].Spikes : "");
                Font ft = Theme.F(8.2f, FontStyle.Regular);
                int tw = Math.Max(Draw.Measure(l1, ft).Width, Math.Max(Draw.Measure(l2, ft).Width, Draw.Measure(l4, ft).Width)) + Theme.Px(20);
                int th = ft.Height * 4 + Theme.Px(14);
                int tx = (int)Math.Min(plot.Right - tw, Math.Max(plot.X, x + Theme.Px(10)));
                int ty = Math.Max(plot.Y + Theme.Px(2), (int)Math.Min(plot.Bottom - th, maxPts[_hoverIdx].Y - th / 2));
                Rectangle tr = new Rectangle(tx, ty, tw, th);
                Draw.FillRound(g, tr, Theme.Px(7), p.Surface, p.BorderStrong);
                int yy = tr.Y + Theme.Px(6);
                Draw.Text(g, l1, ft, p.Accent, new Rectangle(tr.X + Theme.Px(9), yy, tr.Width, ft.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); yy += ft.Height;
                Draw.Text(g, l2, ft, p.TextSec, new Rectangle(tr.X + Theme.Px(9), yy, tr.Width, ft.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); yy += ft.Height;
                if (ShowIsr) { Draw.Text(g, l3, ft, p.TextSec, new Rectangle(tr.X + Theme.Px(9), yy, tr.Width, ft.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); }
                yy += ft.Height;
                Draw.Text(g, l4, ft, p.TextMuted, new Rectangle(tr.X + Theme.Px(9), yy, tr.Width, ft.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            g.SmoothingMode = old;

            // ---- 时间轴说明 ----
            Font fx = Theme.F(7.6f, FontStyle.Regular);
            Draw.Text(g, "最近 " + (Capacity) + " 秒", fx, p.TextMuted,
                new Rectangle(plot.X, plot.Bottom + Theme.Px(4), plot.Width, fx.Height + Theme.Px(4)),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
            Draw.Text(g, "现在", fx, p.TextMuted,
                new Rectangle(plot.X, plot.Bottom + Theme.Px(4), plot.Width, fx.Height + Theme.Px(4)),
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        }

        private static float YOf(double v, Rectangle plot, double top)
        {
            double f = v / top;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            return plot.Bottom - (float)(plot.Height * f);
        }

        private void DrawThreshold(Graphics g, Rectangle plot, double top, double v, Color c, string label)
        {
            if (v <= 0 || v > top) return;
            int y = plot.Bottom - (int)Math.Round(plot.Height * (v / top));
            using (Pen pen = new Pen(Color.FromArgb(150, c), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, plot.X, y, plot.Right, y);
            Font f = Theme.F(7.4f, FontStyle.Regular);
            Size s = Draw.Measure(label, f);
            Rectangle lr = new Rectangle(plot.Right - s.Width - Theme.Px(6), y - f.Height - Theme.Px(1), s.Width + Theme.Px(4), f.Height);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, Theme.Cur.Surface))) g.FillRectangle(b, lr);
            Draw.Text(g, label, f, c, lr, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private int Legend(Graphics g, int x, int y, Color c, string text)
        {
            Font f = Theme.F(8f, FontStyle.Regular);
            int len = Theme.Px(14);
            using (Pen pen = new Pen(c, Theme.Px(2))) g.DrawLine(pen, x, y + f.Height / 2, x + len, y + f.Height / 2);
            Draw.Text(g, text, f, Theme.Cur.TextSec, new Rectangle(x + len + Theme.Px(5), y, Width, f.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return x + len + Theme.Px(5) + Draw.Measure(text, f).Width + Theme.Px(16);
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
            return nice * mag;
        }
    }

    /// <summary>横向条形排行（Top 违规驱动 / 磁盘 / 网络）。</summary>
    internal class BarList : SkinnedControl
    {
        public class Item
        {
            public string Label = "";
            public string Value = "";
            public string Sub = "";
            public double Fraction;   // 0..1
            public Sev Sev = Sev.Neutral;
        }

        private List<Item> _items = new List<Item>();
        private string _empty = "暂无数据";
        public int RowHeight = 30;

        public BarList()
        {
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        public void Set(List<Item> items, string emptyText)
        {
            _items = items != null ? items : new List<Item>();
            if (emptyText != null) _empty = emptyText;
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            if (_items.Count == 0)
            {
                Font fe = Theme.F(9f, FontStyle.Regular);
                Draw.Text(g, _empty, fe, p.TextMuted, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int rh = Theme.Px(RowHeight);
            Font fl = Theme.F(9f, FontStyle.Regular);
            Font fv = Theme.Fm(9f, FontStyle.Bold);
            Font fsub = Theme.F(7.8f, FontStyle.Regular);

            int labelW = Math.Min(Theme.Px(190), Math.Max(Theme.Px(96), Width / 3));
            int valueW = Theme.Px(86);
            int barX = labelW + Theme.Px(10);
            int barW = Math.Max(Theme.Px(40), Width - barX - valueW - Theme.Px(12));

            int y = Theme.Px(4);
            using (SolidBrush track = new SolidBrush(p.SurfaceAlt))
            {
                foreach (Item it in _items)
                {
                    if (y + rh > Height) break;
                    Color c = Theme.SevColor(it.Sev);

                    Draw.Text(g, it.Label, fl, p.TextPri, new Rectangle(0, y, labelW, rh),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    int bh = Theme.Px(9);
                    int by = y + (rh - bh) / 2 - (string.IsNullOrEmpty(it.Sub) ? 0 : Theme.Px(5));
                    Rectangle trackR = new Rectangle(barX, by, barW, bh);
                    Draw.FillRound(g, trackR, bh / 2, p.SurfaceAlt, null);
                    int fw = (int)Math.Round(barW * Math.Max(0.02, Math.Min(1.0, it.Fraction)));
                    Draw.FillRound(g, new Rectangle(barX, by, Math.Max(bh, fw), bh), bh / 2, c, null);

                    Draw.Text(g, it.Value, fv, c, new Rectangle(Width - valueW, y, valueW, rh),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    if (!string.IsNullOrEmpty(it.Sub))
                    {
                        Draw.Text(g, it.Sub, fsub, p.TextMuted, new Rectangle(barX, by + bh + Theme.Px(1), barW, fsub.Height + 2),
                            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    }
                    y += rh;
                }
            }
        }
    }

    /// <summary>中断/核心负载这类「每个核心一格」的紧凑分布条。</summary>
    internal class CoreGrid : SkinnedControl
    {
        private float[] _vals = new float[0];
        private float _warn = 15000f;
        private float _crit = 20000f;

        public CoreGrid()
        {
            Height = Theme.Px(54);
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        public void Set(float[] vals, float warn, float crit)
        {
            _vals = vals != null ? vals : new float[0];
            _warn = warn; _crit = crit;
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            if (_vals.Length == 0)
            {
                Draw.Text(g, "等待数据…", Theme.F(9f, FontStyle.Regular), p.TextMuted, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int n = _vals.Length;
            int pad = Theme.Px(2);
            Font ft = Theme.F(8f, FontStyle.Regular);
            Font fc = Theme.F(7f, FontStyle.Regular);
            int titleH = Draw.LineH(ft);
            int labelH = Draw.LineH(fc);
            int top = titleH + Theme.Px(2);

            Draw.Text(g, "每核心中断数（格底数字 = 核心编号）", ft, p.TextSec,
                new Rectangle(0, 0, Width, titleH),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // 条宽按核心数均分，保证最后一个核心不会被挤出卡片右边界
            int cw = Math.Max(Theme.Px(4), (Width - pad * (n + 1)) / Math.Max(1, n));
            int barsH = Math.Max(Theme.Px(10), Height - top - labelH - Theme.Px(1));
            int baseY = top + barsH;

            // 编号只有放得下才画：不够宽就隔 step 个画一个，绝不互相压字
            int labelW = Draw.Measure("00", fc).Width + Theme.Px(2);
            int step = Math.Max(1, (int)Math.Ceiling(labelW / (double)Math.Max(1, cw + pad)));

            float max = _crit * 1.5f;
            for (int i = 0; i < n; i++) if (_vals[i] > max) max = _vals[i];

            for (int i = 0; i < n; i++)
            {
                int x = pad + i * (cw + pad);
                if (x + cw > Width) break;
                float f = _vals[i] / max;
                if (f > 1) f = 1;
                int bh = Math.Max(Theme.Px(2), (int)(barsH * f));
                Sev sv = _vals[i] > _crit ? Sev.Crit : (_vals[i] > _warn ? Sev.Warn : Sev.Ok);
                Color c = Theme.SevColor(sv);

                Draw.FillRound(g, new Rectangle(x, baseY - barsH, cw, barsH), Theme.Px(3), p.SurfaceAlt, null);
                Draw.FillRound(g, new Rectangle(x, baseY - bh, cw, bh), Theme.Px(3), c, null);
                if (i % step == 0)
                {
                    Draw.Text(g, i.ToString(), fc, p.TextMuted, new Rectangle(x - pad, baseY + Theme.Px(1), cw + pad * 2, labelH),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
