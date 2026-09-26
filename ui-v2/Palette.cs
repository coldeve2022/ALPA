using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ALP2
{
    /// <summary>
    /// 一套完整配色。v1 的问题是所有颜色都硬编码在窗体里，且只有一种黑底方案，
    /// 这里把颜色集中成可整体替换的调色板，浅色/深色各一份。
    /// </summary>
    internal class Palette
    {
        public string Name = "";

        // 层级背景
        public Color Bg;           // 窗口底色
        public Color Sidebar;      // 左侧导航底色
        public Color Surface;      // 卡片/表格底色
        public Color SurfaceAlt;   // 表头、斑马纹
        public Color SurfaceHover; // 悬停行

        // 描边
        public Color Border;
        public Color BorderStrong;
        public Color GridLine;

        // 文字
        public Color TextPri;
        public Color TextSec;
        public Color TextMuted;
        public Color TextOnAccent;

        // 主色
        public Color Accent;
        public Color AccentSoft;
        public Color AccentHover;

        // 语义色（含浅底版本，用于徽章底色）
        public Color Ok, OkSoft;
        public Color Warn, WarnSoft;
        public Color Crit, CritSoft;
        public Color Info, InfoSoft;
        public Color Neutral, NeutralSoft;

        public Color Selection;
        public Color SelectionText;

        public static Palette MakeLight()
        {
            Palette p = new Palette();
            p.Name = "浅色";
            p.Bg = Color.FromArgb(0xF2, 0xF5, 0xFA);
            p.Sidebar = Color.FromArgb(0xFF, 0xFF, 0xFF);
            p.Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
            p.SurfaceAlt = Color.FromArgb(0xF8, 0xFA, 0xFC);
            p.SurfaceHover = Color.FromArgb(0xEF, 0xF4, 0xFB);

            p.Border = Color.FromArgb(0xE2, 0xE8, 0xF0);
            p.BorderStrong = Color.FromArgb(0xC7, 0xD2, 0xE0);
            p.GridLine = Color.FromArgb(0xEC, 0xF0, 0xF6);

            p.TextPri = Color.FromArgb(0x0F, 0x17, 0x2A);
            p.TextSec = Color.FromArgb(0x5B, 0x6B, 0x84);
            p.TextMuted = Color.FromArgb(0x94, 0xA3, 0xB8);
            p.TextOnAccent = Color.White;

            p.Accent = Color.FromArgb(0x1D, 0x4E, 0xD8);
            p.AccentSoft = Color.FromArgb(0xE0, 0xEA, 0xFF);
            p.AccentHover = Color.FromArgb(0x1A, 0x45, 0xC0);

            p.Ok = Color.FromArgb(0x15, 0x80, 0x3D);
            p.OkSoft = Color.FromArgb(0xDC, 0xFC, 0xE7);
            p.Warn = Color.FromArgb(0xB4, 0x53, 0x09);
            p.WarnSoft = Color.FromArgb(0xFE, 0xF3, 0xC7);
            p.Crit = Color.FromArgb(0xB9, 0x1C, 0x1C);
            p.CritSoft = Color.FromArgb(0xFE, 0xE2, 0xE2);
            p.Info = Color.FromArgb(0x03, 0x69, 0xA1);
            p.InfoSoft = Color.FromArgb(0xE0, 0xF2, 0xFE);
            p.Neutral = Color.FromArgb(0x47, 0x55, 0x69);
            p.NeutralSoft = Color.FromArgb(0xEE, 0xF2, 0xF7);

            p.Selection = Color.FromArgb(0xDB, 0xE7, 0xFF);
            p.SelectionText = Color.FromArgb(0x0F, 0x17, 0x2A);
            return p;
        }

        public static Palette MakeDark()
        {
            Palette p = new Palette();
            p.Name = "深色";
            p.Bg = Color.FromArgb(0x0B, 0x12, 0x20);
            p.Sidebar = Color.FromArgb(0x11, 0x18, 0x27);
            p.Surface = Color.FromArgb(0x16, 0x1F, 0x2F);
            p.SurfaceAlt = Color.FromArgb(0x1B, 0x25, 0x38);
            p.SurfaceHover = Color.FromArgb(0x22, 0x2E, 0x45);

            p.Border = Color.FromArgb(0x26, 0x32, 0x4A);
            p.BorderStrong = Color.FromArgb(0x35, 0x44, 0x5E);
            p.GridLine = Color.FromArgb(0x20, 0x2B, 0x40);

            p.TextPri = Color.FromArgb(0xE8, 0xEE, 0xF7);
            p.TextSec = Color.FromArgb(0x9A, 0xAA, 0xC0);
            p.TextMuted = Color.FromArgb(0x6B, 0x7C, 0x94);
            p.TextOnAccent = Color.FromArgb(0x06, 0x11, 0x22);

            p.Accent = Color.FromArgb(0x60, 0xA5, 0xFA);
            p.AccentSoft = Color.FromArgb(0x1E, 0x33, 0x5E);
            p.AccentHover = Color.FromArgb(0x7D, 0xB6, 0xFB);

            p.Ok = Color.FromArgb(0x4A, 0xDE, 0x80);
            p.OkSoft = Color.FromArgb(0x12, 0x33, 0x24);
            p.Warn = Color.FromArgb(0xFB, 0xBF, 0x24);
            p.WarnSoft = Color.FromArgb(0x3B, 0x2C, 0x0C);
            p.Crit = Color.FromArgb(0xF8, 0x71, 0x71);
            p.CritSoft = Color.FromArgb(0x3D, 0x17, 0x17);
            p.Info = Color.FromArgb(0x38, 0xBD, 0xF8);
            p.InfoSoft = Color.FromArgb(0x0C, 0x2C, 0x42);
            p.Neutral = Color.FromArgb(0xA8, 0xB6, 0xC8);
            p.NeutralSoft = Color.FromArgb(0x1F, 0x2A, 0x3E);

            p.Selection = Color.FromArgb(0x1E, 0x3A, 0x6B);
            p.SelectionText = Color.FromArgb(0xE8, 0xEE, 0xF7);
            return p;
        }
    }

    /// <summary>严重程度，UI 里到处都在用它决定配色。</summary>
    internal enum Sev
    {
        Ok,
        Info,
        Warn,
        Crit,
        Neutral
    }

    internal static class Theme
    {
        public static Palette Cur = Palette.MakeLight();
        public static event EventHandler Changed;

        /// <summary>
        /// 全局 UI 缩放系数 = DPI 缩放 × 屏幕适配系数。
        /// 像素常量（Px/Pf）和字号（F/Fm）都乘它，保证两者永远同步 ——
        /// 否则在 DPI 125% 且只有 1024x768 的屏幕上，字号按 DPI 放大而卡片不放大，
        /// 版面就会被文字挤爆。
        /// </summary>
        public static float S = 1f;

        public static int Px(float v)
        {
            return (int)Math.Round(v * S);
        }

        public static float Pf(float v)
        {
            return v * S;
        }

        public static void Use(Palette p)
        {
            Cur = p;
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        private static readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();
        private static readonly object _cacheLock = new object();

        private static string _uiFamily = null;
        private static string _monoFamily = null;

        public static string UiFamily
        {
            get
            {
                if (_uiFamily == null) _uiFamily = Resolve(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma" }, "Microsoft YaHei UI");
                return _uiFamily;
            }
        }

        public static string MonoFamily
        {
            get
            {
                if (_monoFamily == null) _monoFamily = Resolve(new string[] { "Cascadia Mono", "Consolas", "Courier New" }, "Consolas");
                return _monoFamily;
            }
        }

        private static string Resolve(string[] candidates, string fallback)
        {
            try
            {
                List<string> have = new List<string>();
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    foreach (FontFamily f in col.Families) have.Add(f.Name);
                }
                foreach (string c in candidates)
                {
                    if (have.Contains(c)) return c;
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>带缓存的字体工厂 —— 绝不要在 OnPaint 里 new Font（GDI 句柄会爆）。</summary>
        public static Font F(float pt, FontStyle st)
        {
            return Get(UiFamily, pt * S, st);
        }

        public static Font Fm(float pt, FontStyle st)
        {
            return Get(MonoFamily, pt * S, st);
        }

        private static Font Get(string family, float pt, FontStyle st)
        {
            string key = family + "|" + pt.ToString("0.###") + "|" + (int)st;
            lock (_cacheLock)
            {
                Font f;
                if (_cache.TryGetValue(key, out f)) return f;
                try { f = new Font(family, pt, st, GraphicsUnit.Point); }
                catch { f = new Font(FontFamily.GenericSansSerif, pt, st, GraphicsUnit.Point); }
                _cache[key] = f;
                return f;
            }
        }

        public static Color SevColor(Sev s)
        {
            switch (s)
            {
                case Sev.Ok: return Cur.Ok;
                case Sev.Warn: return Cur.Warn;
                case Sev.Crit: return Cur.Crit;
                case Sev.Info: return Cur.Info;
                default: return Cur.Neutral;
            }
        }

        public static Color SevSoft(Sev s)
        {
            switch (s)
            {
                case Sev.Ok: return Cur.OkSoft;
                case Sev.Warn: return Cur.WarnSoft;
                case Sev.Crit: return Cur.CritSoft;
                case Sev.Info: return Cur.InfoSoft;
                default: return Cur.NeutralSoft;
            }
        }
    }

    internal static class Draw
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || d > r.Width || d > r.Height)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>填充圆角矩形（可选描边），不修改调用方的 SmoothingMode。</summary>
        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color? border)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue)
                {
                    using (Pen p = new Pen(border.Value, 1f)) g.DrawPath(p, path);
                }
            }
            g.SmoothingMode = old;
        }

        public static void FillRoundGradient(Graphics g, Rectangle r, int radius, Color a, Color b)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(r, radius))
            using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)), a, b, 90f))
                g.FillPath(br, path);
            g.SmoothingMode = old;
        }

        private static readonly Dictionary<string, Size> _txtCache = new Dictionary<string, Size>();
        private static readonly object _txtLock = new object();

        private static string FontKey(Font f)
        {
            return f.Name + "|" + f.SizeInPoints.ToString("0.###") + "|" + (int)f.Style;
        }

        /// <summary>
        /// GDI 口径的一行文字尺寸（TextRenderer 实际使用的那套度量）。
        /// 这里踩过一个很隐蔽的坑：Font.Height 是 GDI+ 口径，比 GDI 口径小 3~5px，
        /// 而所有控件的矩形高度之前都拿 Font.Height 当行高，于是每行字上下各被切一刀；
        /// 更糟的是没有 SingleLine 时 TextRenderer 会自动折行，再叠上 VerticalCenter，
        /// 两行文字被塞进一行高的框里，看上去就像整段被压扁。
        /// </summary>
        public static Size GdiSize(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return new Size(0, 0);
            string key = "G|" + FontKey(f) + "|" + s;
            lock (_txtLock)
            {
                Size v;
                if (_txtCache.TryGetValue(key, out v)) return v;
            }
            Size r = TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Store(key, r);
            return r;
        }

        /// <summary>
        /// 不依赖 Graphics 的 GDI 尺寸测量（静态重载走屏幕 DC）。
        /// 表格列宽自适应要在 SetRows 阶段算，那时拿不到 PaintEventArgs，用它。
        /// </summary>
        public static Size GdiSize(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return new Size(0, 0);
            string key = "F|" + FontKey(f) + "|" + s;
            lock (_txtLock)
            {
                Size v;
                if (_txtCache.TryGetValue(key, out v)) return v;
            }
            Size r = TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Store(key, r);
            return r;
        }

        /// <summary>一行文字真正需要的行高（GDI 口径）。布局计算统一用它，不要用 Font.Height。</summary>
        public static int LineH(Font f)
        {
            string key = "L|" + FontKey(f);
            lock (_txtLock)
            {
                Size v;
                if (_txtCache.TryGetValue(key, out v)) return v.Height;
            }
            Size r = TextRenderer.MeasureText("国Ag", f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Store(key, r);
            return r.Height;
        }

        private static void Store(string key, Size v)
        {
            lock (_txtLock)
            {
                if (_txtCache.Count > 6000) _txtCache.Clear();
                _txtCache[key] = v;
            }
        }

        /// <summary>
        /// 统一文本出口。单行意图（没写 WordBreak、字符串里也没有换行）时：
        /// ① 强制 SingleLine，杜绝「悄悄折行」；
        /// ② 矩形太矮就把高度补到 GDI 行高，并按原来的垂直对齐方向扩，保证文字完整可见。
        /// </summary>
        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(s)) return;
            bool multi = (flags & TextFormatFlags.WordBreak) != 0
                         || (flags & TextFormatFlags.SingleLine) != 0
                         || s.IndexOf('\n') >= 0;
            if (!multi)
            {
                flags |= TextFormatFlags.SingleLine;
                Size need = GdiSize(g, s, f);
                if (need.Height > r.Height)
                {
                    int extra = need.Height - r.Height;
                    if ((flags & TextFormatFlags.Bottom) != 0) r.Y -= extra;
                    else if ((flags & TextFormatFlags.VerticalCenter) != 0) r.Y -= extra / 2;
                    r.Height = need.Height;
                }
            }
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public static void TextLeft(Graphics g, string s, Font f, Color c, int x, int y, int w, int h)
        {
            Text(g, s, f, c, new Rectangle(x, y, w, h),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        public static Size Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return new Size(0, 0);
            string key = "M|" + FontKey(f) + "|" + s;
            lock (_txtLock)
            {
                Size v;
                if (_txtCache.TryGetValue(key, out v)) return v;
            }
            Size r = TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            Store(key, r);
            return r;
        }

        /// <summary>文本实际占到的像素高（含 GDI 内边距），算最小行高时用。</summary>
        public static int TextH(Graphics g, string s, Font f)
        {
            return GdiSize(g, s, f).Height;
        }

        public static void HLine(Graphics g, int x1, int x2, int y, Color c)
        {
            using (Pen p = new Pen(c, 1f))
            {
                g.DrawLine(p, x1, y, x2, y);
            }
        }

        /// <summary>给矩形做 1px 内描边，避免 Pen 跨越半像素导致的虚边。</summary>
        public static void InsetBorder(Graphics g, Rectangle r, Color c)
        {
            using (Pen p = new Pen(c, 1f))
            {
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        }
    }

    internal static class Fmt
    {
        public static string Us(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
            if (v >= 10000) return v.ToString("#,0");
            if (v >= 100) return v.ToString("0");
            return v.ToString("0.0");
        }

        /// <summary>大数字压缩成 1.2M / 34.5k，避免表格里挤成一坨。</summary>
        public static string Count(long v)
        {
            if (v >= 1000000000L) return (v / 1000000000.0).ToString("0.0") + "B";
            if (v >= 1000000L) return (v / 1000000.0).ToString("0.0") + "M";
            if (v >= 10000L) return (v / 1000.0).ToString("0.0") + "k";
            return v.ToString("#,0");
        }

        public static string Mb(double v)
        {
            if (v >= 1024 * 1024) return (v / 1024.0 / 1024.0).ToString("0.0") + " TB";
            if (v >= 1024) return (v / 1024.0).ToString("0.0") + " GB";
            return v.ToString("0") + " MB";
        }

        public static string Bytes(ulong v)
        {
            if (v >= 1024UL * 1024 * 1024 * 1024) return (v / 1024.0 / 1024 / 1024 / 1024).ToString("0.00") + " TB";
            if (v >= 1024UL * 1024 * 1024) return (v / 1024.0 / 1024 / 1024).ToString("0.0") + " GB";
            if (v >= 1024UL * 1024) return (v / 1024.0 / 1024).ToString("0") + " MB";
            return (v / 1024.0).ToString("0") + " KB";
        }

        public static string Time(DateTime t)
        {
            return t.ToString("HH:mm:ss");
        }

        public static string Span(TimeSpan t)
        {
            if (t.TotalHours >= 1) return ((int)t.TotalHours) + "h " + t.Minutes.ToString("00") + "m";
            return t.Minutes + "m " + t.Seconds.ToString("00") + "s";
        }
    }
}
