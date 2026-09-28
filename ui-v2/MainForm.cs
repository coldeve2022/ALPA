using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

// 注意：MainForm 继承自 Form，Form.Icon 这个属性会把 ALP2.Icon 枚举遮蔽掉，
// 所以在本文件里统一用别名 Ico 指代图标枚举。
using Ico = ALP2.Icon;

namespace ALP2
{
    /// <summary>顶栏：底色 + 底部分割线 + 左侧标题。</summary>
    internal class TopArea : SkinnedControl
    {
        public string Title = "ALPA";
        public string Sub = "";
        public string Version = "";

        public TopArea()
        {
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Surface);
            Draw.HLine(g, 0, Width, Height - 1, p.Border);

            int x = Theme.Px(18);
            Font ft = Theme.F(13f, FontStyle.Bold);
            Font fs = Theme.F(8.2f, FontStyle.Regular);
            int cy = Height / 2;
            Draw.Text(g, Title, ft, p.TextPri, new Rectangle(x, cy - ft.Height - Theme.Px(1), Theme.Px(240), ft.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            Draw.Text(g, Sub, fs, p.TextSec, new Rectangle(x, cy + Theme.Px(1), Theme.Px(420), fs.Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (!string.IsNullOrEmpty(Version))
            {
                // 版本徽标按标题/副标题的实际宽度让位，别再压住文字
                int textW = Math.Max(Draw.Measure(Title, ft).Width, Draw.Measure(Sub, fs).Width);
                Font fv = Theme.F(7.8f, FontStyle.Bold);
                Size vs = Draw.Measure(Version, fv);
                Rectangle vr = new Rectangle(x + textW + Theme.Px(12), cy - Theme.Px(9), vs.Width + Theme.Px(16), Theme.Px(18));
                Draw.FillRound(g, vr, Theme.Px(9), p.AccentSoft, null);
                Draw.Text(g, Version, fv, p.Accent, vr,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal class SidePanel : SkinnedControl
    {
        public string Footer = "";

        public SidePanel()
        {
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.Sidebar);
            using (Pen pen = new Pen(p.Border, 1f)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            if (!string.IsNullOrEmpty(Footer))
            {
                Font f = Theme.F(7.8f, FontStyle.Regular);
                Draw.Text(g, Footer, f, p.TextMuted, new Rectangle(Theme.Px(18), Height - Theme.Px(40), Width - Theme.Px(24), Theme.Px(30)),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            }
        }
    }

    internal class StatusBarCtl : SkinnedControl
    {
        private string[] _left = new string[0];
        private Sev[] _sev = new Sev[0];
        private string _right = "";
        private Color _rightColor;

        public StatusBarCtl()
        {
            Theme.Changed += delegate { try { if (!IsDisposed) Invalidate(); } catch { } };
        }

        public void Set(string[] left, Sev[] sev, string right, Color rightColor)
        {
            _left = left; _sev = sev; _right = right; _rightColor = rightColor;
            if (!IsDisposed && IsHandleCreated) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Cur;
            g.Clear(p.SurfaceAlt);
            Draw.HLine(g, 0, Width, 0, p.Border);

            Font f = Theme.F(8f, FontStyle.Regular);
            int x = Theme.Px(14);
            for (int i = 0; i < _left.Length; i++)
            {
                Size s = Draw.Measure(_left[i], f);
                if (x + s.Width > Width - Theme.Px(8)) break;
                Draw.Text(g, _left[i], f, Theme.SevColor(_sev[i]), new Rectangle(x, 0, s.Width + Theme.Px(2), Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += s.Width + Theme.Px(4);
                if (i < _left.Length - 1)
                {
                    Draw.Text(g, "·", f, p.BorderStrong, new Rectangle(x, 0, Theme.Px(12), Height),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x += Theme.Px(14);
                }
            }
            if (!string.IsNullOrEmpty(_right))
            {
                Draw.Text(g, _right, f, _rightColor, new Rectangle(0, 0, Width - Theme.Px(14), Height),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal class MainForm : Form
    {
        private readonly Engine _eng = new Engine();
        private readonly TopArea _top = new TopArea();
        private readonly SidePanel _side = new SidePanel();
        private readonly StatusBarCtl _status = new StatusBarCtl();
        private readonly Panel _host = new Panel();
        private readonly Banner _banner = new Banner();
        private readonly List<NavItem> _navs = new List<NavItem>();
        private readonly List<PageBase> _pages = new List<PageBase>();
        private readonly Segmented _themeSeg;
        private readonly FlatButton _btnRun = new FlatButton();
        private readonly FlatButton _btnExport = new FlatButton();
        private readonly FlatButton _btnLogs = new FlatButton();
        private readonly FlatButton _btnReset = new FlatButton();

        private readonly Dictionary<Control, Action> _themed = new Dictionary<Control, Action>();

        private int _current;
        private readonly int _startPage;
        private System.Windows.Forms.Timer _timer;
        private readonly Stopwatch _ui = new Stopwatch();

        public MainForm(int startPage)
        {
            Text = "ALPA v2 — 延迟与 DPC 审计 / Latency & DPC Audit";
            _startPage = startPage;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Cur.Bg;
            Font = Theme.F(9f, FontStyle.Regular);
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            int w = (int)(1264 * Theme.S);
            int h = (int)(802 * Theme.S);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            w = Math.Min(w, wa.Width - Theme.Px(8));
            h = Math.Min(h, wa.Height - Theme.Px(8));
            ClientSize = new Size(w, h);
            // 留出足够余量，别让 MinimumSize 反过来把窗口顶大
            MinimumSize = new Size((int)(w * 0.78f), (int)(h * 0.74f));
            MaximizeBox = true;
            MinimizeBox = true;

            _top.Title = "ALPA";
            _top.Sub = "延迟与 DPC 审计 · Latency & DPC Audit";
            try { _top.Version = "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
            catch { _top.Version = "v2"; }
            Controls.Add(_top);
            Controls.Add(_side);
            Controls.Add(_status);

            _themeSeg = new Segmented(new string[] { "浅色", "深色" }, new Ico[] { Ico.Sun, Ico.Moon });
            _themeSeg.Selected = 0;
            _themeSeg.SelectedChanged += delegate { SwitchTheme(_themeSeg.Selected == 1); };
            Controls.Add(_themeSeg);

            _btnRun.Kind = BtnKind.Primary;
            _btnRun.Ico = Ico.Stop;
            _btnRun.Text2 = "停止追踪";
            _btnRun.Click += delegate { ToggleRun(); };
            Controls.Add(_btnRun);

            _btnExport.Kind = BtnKind.Ghost;
            _btnExport.Ico = Ico.Export;
            _btnExport.Text2 = "导出";
            _btnExport.Click += delegate { DoExport(); };
            Controls.Add(_btnExport);

            _btnLogs.Kind = BtnKind.Ghost;
            _btnLogs.Ico = Ico.Folder;
            _btnLogs.Text2 = "日志目录";
            _btnLogs.Click += delegate { OpenDir(); };
            Controls.Add(_btnLogs);

            _btnReset.Kind = BtnKind.Ghost;
            _btnReset.Ico = Ico.Refresh;
            _btnReset.Text2 = "清零";
            _btnReset.Click += delegate { _eng.ResetStats(); };
            Controls.Add(_btnReset);

            _host.BackColor = Theme.Cur.Bg;
            Controls.Add(_host);

            _banner.Visible = false;
            AddPage(new OverviewPage(_eng));
            AddPage(new DpcPage(_eng));
            AddPage(new ProcPage(_eng));
            AddPage(new HealthPage(_eng));
            AddPage(new StartupPage(_eng));
            AddPage(new ConsolePage(_eng));

            _host.Controls.Add(_banner);

            SetPage(_startPage >= 0 ? _startPage : 0);

            // 引擎事件 → UI 线程
            _eng.Log += delegate (LogEntry e)
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(delegate { ((ConsolePage)_pages[5]).Append(e); })); }
                catch { }
            };
            _eng.Sample += delegate (Snapshot s)
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(delegate { ApplySample(s); })); }
                catch { }
            };
            _eng.Spike += delegate (SpikeRec r)
            {
                // 尖峰本身已进引擎队列，这里只做一次轻量重绘触发
                if (IsDisposed) return;
                try { BeginInvoke(new Action(delegate { _pages[_current].Invalidate(); })); }
                catch { }
            };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate
            {
                _eng.FlushMouseRate();
                UpdateStatus();
                // 提示条不能只在「采样到达时」更新：引擎/追踪失败可能早于首次采样，
                // 那样用户会先盯着一片空白好几秒才看到原因。这里每秒复查一次。
                UpdateBanner(null);
            };
            _timer.Start();

            Shown += delegate
            {
                _ui.Start();
                _eng.AttachRawInput(Handle);
                _eng.Start(Engine.AppDir);
                // 界面缩放写进日志：出问题时要能一眼看出跑的是哪套 DPI/缩放组合
                _eng.Write("ui scale " + Theme.S.ToString("0.###", CultureInfo.InvariantCulture)
                    + " (dpi " + Program.DpiScale.ToString("0.##", CultureInfo.InvariantCulture)
                    + " x fit " + Program.FitScale.ToString("0.###", CultureInfo.InvariantCulture) + ")",
                    LogLevel.Muted);
                // 界面一出来就先给结论（有没有权限），别让空白先出现
                UpdateBanner(null);
                SyncRunButton();
            };

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
                {
                    SetPage(e.KeyCode - Keys.D1);
                    e.Handled = true;
                }
            };

            Theme.Changed += delegate
            {
                try { ApplyTheme(); }
                catch { }
            };
        }

        private void AddPage(PageBase p)
        {
            p.Visible = false;
            _pages.Add(p);
            _host.Controls.Add(p);

            NavItem n = new NavItem();
            n.Ico = p.NavIcon;
            n.Label = p.NavLabel;
            int idx = _navs.Count;
            n.Click += delegate { SetPage(idx); };
            _navs.Add(n);
            _side.Controls.Add(n);
        }

        private void SetPage(int idx)
        {
            if (idx < 0 || idx >= _pages.Count) return;
            _current = idx;
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = (i == idx);
                _navs[i].Selected = (i == idx);
            }
            _pages[idx].BringToFront();
            _banner.BringToFront();
            _side.Invalidate();
        }

        private void ApplySample(Snapshot s)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                if (i == _current) _pages[i].OnSample(s);
            }
            _pages[_current].Invalidate(true);
            // 概览页与 DPC 页即使在后台也要保持曲线与尖峰连续
            if (_current != 0) _pages[0].OnSample(s);
            if (_current != 1) _pages[1].OnSample(s);
            UpdateBanner(s);
        }

        private bool _bannerShown;
        private string _bannerKey = "";

        /// <summary>
        /// 提示条有且只有三种状态：① 没提权；② 提权了但内核追踪没起来（把原因直接摆给用户看）；
        /// ③ 一切正常则不显示。用 key 去重，避免每秒重建按钮。
        /// </summary>
        private void UpdateBanner(Snapshot s)
        {
            string key;
            if (!_eng.IsAdmin) key = "no-admin";
            else if (!string.IsNullOrEmpty(_eng.TraceError)) key = "trace:" + _eng.TraceError;
            else key = "";

            if (key == _bannerKey) return;
            _bannerKey = key;
            _bannerShown = key.Length > 0;

            if (!_bannerShown)
            {
                _banner.Visible = false;
                LayoutHost();
                return;
            }

            if (key == "no-admin")
            {
                _banner.Set("当前以普通权限运行，内核 DPC / ISR 追踪不可用",
                    "这一项是整个工具的核心能力，需要管理员权限才能挂载 ETW 内核会话。",
                    Sev.Warn, "以管理员身份重启", "查看日志");
                if (_banner.Action != null) _banner.Action.Click += delegate { Elevate(); };
            }
            else
            {
                _banner.Set("内核 DPC / ISR 追踪没能启动 —— 这就是延迟数据空白的原因",
                    "原因：" + _eng.TraceError + "。日志里有完整堆栈；若另一个延迟监控工具正占着内核会话，关掉它再点「重试追踪」。",
                    Sev.Crit, "重试追踪", "查看日志");
                if (_banner.Action != null) _banner.Action.Click += delegate { RetryTracing(); };
            }
            if (_banner.Action2 != null) _banner.Action2.Click += delegate { SetPage(5); };

            _banner.Visible = true;
            LayoutHost();
        }

        private void RetryTracing()
        {
            _eng.RetryTracing();
            _bannerKey = "";          // 强制下一帧重算提示条状态
            Invalidate();
        }

        private int BannerH { get { return _bannerShown ? Theme.Px(58) : 0; } }

        /// <summary>
        /// 显式摆放页面与提示条。
        /// 一开始用 Dock（提示条 Top + 页面 Fill）实测失效：Fill 的页面会先占满整个区域，
        /// 提示条只是盖在它上面，页面顶部内容被压在提示条底下。改成手算边界最稳。
        /// </summary>
        private void LayoutHost()
        {
            int bh = BannerH;
            int cw = _host.ClientSize.Width;
            int ch = _host.ClientSize.Height;
            if (cw <= 0 || ch <= 0) return;
            _banner.SetBounds(0, 0, cw, bh);
            for (int i = 0; i < _pages.Count; i++)
                _pages[i].SetBounds(0, bh, cw, Math.Max(Theme.Px(80), ch - bh));
        }

        private void Elevate()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "--no-elevate";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch { }
        }

        private void ToggleRun()
        {
            if (_eng.IsTracing || _runRequested)
            {
                _runRequested = false;
                _eng.Stop();
                _eng.Write("tracing stopped by user", LogLevel.Warn);
            }
            else
            {
                _runRequested = true;
                _eng.Start(Engine.AppDir);
            }
            SyncRunButton();
        }

        private bool _runRequested = true;

        private void SyncRunButton()
        {
            bool running = _eng.IsTracing || (_runRequested && !_eng.IsAdmin);
            _btnRun.Text2 = _runRequested ? "停止追踪" : "开始追踪";
            _btnRun.Ico = _runRequested ? Ico.Stop : Ico.Play;
            _btnRun.Kind = _runRequested ? BtnKind.Primary : BtnKind.Primary;
            _btnRun.Invalidate();
            if (!running) { }
        }

        private void DoExport()
        {
            try
            {
                string a = _eng.ExportDriversCsv(Engine.AppDir);
                string b = _eng.ExportSpikesCsv(Engine.AppDir);
                MessageBox.Show(this, "已导出两个文件：\n\n" + a + "\n" + b, "导出完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDir()
        {
            try { Process.Start("explorer.exe", "\"" + Engine.AppDir + "\""); }
            catch { }
        }

        private void SwitchTheme(bool dark)
        {
            Theme.Use(dark ? Palette.MakeDark() : Palette.MakeLight());
        }

        private void ApplyTheme()
        {
            BackColor = Theme.Cur.Bg;
            _host.BackColor = Theme.Cur.Bg;
            _top.Invalidate();
            _side.Invalidate();
            _status.Invalidate();
            _banner.Invalidate();
            foreach (PageBase p in _pages) { p.BackColor = Theme.Cur.Bg; p.ApplyTheme(); p.Invalidate(true); }
            foreach (NavItem n in _navs) n.Invalidate();
            _themeSeg.Invalidate();
            _btnRun.Invalidate();
            _btnExport.Invalidate();
            _btnLogs.Invalidate();
            _btnReset.Invalidate();
        }

        private void UpdateStatus()
        {
            string[] left;
            Sev[] sev;
            left = new string[]
            {
                _eng.IsAdmin ? "管理员权限" : "普通权限",
                _eng.IsTracing ? "内核追踪运行中" : "内核追踪未运行",
                "DPC " + Fmt.Count((long)_lastDpcPerSec) + "/s",
                "尖峰 " + _lastSpikes.ToString("#,0"),
                "运行 " + Fmt.Span(_ui.Elapsed)
            };
            sev = new Sev[]
            {
                _eng.IsAdmin ? Sev.Ok : Sev.Warn,
                _eng.IsTracing ? Sev.Ok : Sev.Warn,
                Sev.Info,
                _lastSpikes > 0 ? Sev.Warn : Sev.Ok,
                Sev.Neutral
            };
            _status.Set(left, sev, string.IsNullOrEmpty(_eng.LogFile) ? "" : "日志 " + System.IO.Path.GetFileName(_eng.LogFile), Theme.Cur.TextMuted);
        }

        private double _lastDpcPerSec;
        private int _lastSpikes;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Cur.Bg);
            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated) DoLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DoLayout();
        }

        private void DoLayout()
        {
            int topH = Theme.Px(58);
            int statusH = Theme.Px(26);
            int sideW = Theme.Px(206);

            _top.SetBounds(0, 0, ClientSize.Width, topH);
            _side.SetBounds(0, topH, sideW, ClientSize.Height - topH - statusH);
            _status.SetBounds(0, ClientSize.Height - statusH, ClientSize.Width, statusH);
            _host.SetBounds(sideW, topH, ClientSize.Width - sideW, ClientSize.Height - topH - statusH);
            LayoutHost();

            int pad = Theme.Px(14);
            int bh = Theme.Px(30);
            int x = ClientSize.Width - pad;
            _btnRun.SetBounds(x - Theme.Px(112), (topH - bh) / 2, Theme.Px(112), bh); x -= Theme.Px(112) + Theme.Px(8);
            _btnExport.SetBounds(x - Theme.Px(84), (topH - bh) / 2, Theme.Px(84), bh); x -= Theme.Px(84) + Theme.Px(8);
            _btnLogs.SetBounds(x - Theme.Px(96), (topH - bh) / 2, Theme.Px(96), bh); x -= Theme.Px(96) + Theme.Px(8);
            _btnReset.SetBounds(x - Theme.Px(72), (topH - bh) / 2, Theme.Px(72), bh); x -= Theme.Px(72) + Theme.Px(14);
            _themeSeg.SetBounds(x - Theme.Px(150), (topH - bh) / 2, Theme.Px(150), bh);

            int y = Theme.Px(14);
            int nh = Theme.Px(42);
            for (int i = 0; i < _navs.Count; i++)
            {
                _navs[i].SetBounds(Theme.Px(10), y + i * (nh + Theme.Px(2)), sideW - Theme.Px(20), nh);
            }
            _side.Footer = "快捷键 Ctrl+1…6 切换页面\n统计数据随程序退出写回程序目录";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
                _eng.Stop();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        /// <summary>进程 DPI 缩放（96dpi = 1.0）。启动时测一次。</summary>
        public static float DpiScale = 1f;
        /// <summary>屏幕适配系数：设计尺寸装不下时整体等比缩小。</summary>
        public static float FitScale = 1f;

        /// <summary>
        /// 入口。只做一件事：先把嵌入依赖的加载器挂上，再进真正的 Main。
        /// 这个拆分是必须的 —— 本方法体内一旦出现任何依赖 TraceEvent 的类型，
        /// JIT 会在 AssemblyResolve 注册之前就去加载它，单文件模式直接崩。
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Boot.Attach();
            Run(args);
        }

        [STAThread]
        private static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 提权后工作目录会变成 system32，v1 用的都是相对路径，会导致日志和 CSV 落错地方
            try { System.IO.Directory.SetCurrentDirectory(Engine.AppDir); } catch { }

            float dpiScale = 1f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    dpiScale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { dpiScale = 1f; }

            // 设计尺寸（逻辑单位）。如果屏幕装不下，就整体等比缩小 UI 缩放系数，
            // 而不是让窗口被裁掉、版面被挤扁。
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            float wantW = 1264f * dpiScale;
            float wantH = 802f * dpiScale;
            float fit = Math.Min(1f, Math.Min((wa.Width - 12f * dpiScale) / wantW, (wa.Height - 12f * dpiScale) / wantH));
            DpiScale = dpiScale;
            FitScale = fit;
            Theme.S = Math.Max(0.6f, dpiScale * fit);

            bool noElevate = false;
            bool selfTest = false;
            int startPage = -1;
            float forcedScale = 0f;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--no-elevate") noElevate = true;
                else if (args[i] == "--selftest") selfTest = true;
                else if (args[i].StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(args[i].Substring(7), out startPage);
                    startPage = Math.Max(0, Math.Min(5, startPage - 1));   // 命令行用 1..6
                }
                else if (args[i].StartsWith("--scale=", StringComparison.OrdinalIgnoreCase))
                {
                    // 手动指定界面缩放：字太小/太大时用，例如 --scale=1.25
                    float.TryParse(args[i].Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out forcedScale);
                }
            }
            if (forcedScale > 0f)
            {
                Theme.S = Math.Max(0.5f, Math.Min(2.5f, forcedScale));
                FitScale = Theme.S;
            }

            if (selfTest)
            {
                // 造 5 阵 × 32 个尖峰、阵间距 900 秒，验证导出格式与周期判定。
                // 走这一步不需要管理员权限，所以内核追踪那条链路不可用时也能验证这两段逻辑。
                try
                {
                    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alpa_selftest");
                    System.IO.Directory.CreateDirectory(dir);
                    Engine eng = new Engine();
                    eng.InjectSyntheticSpikes(5, 900.0, 32);
                    string csv = eng.ExportSpikesCsv(dir);
                    string rep = eng.ExportReport(dir);
                    PeriodResult pr = Engine.AnalyzePeriods(eng.Spikes);

                    // 回归测试：用户滚到中间后，下一次采样刷新不应把位置拽回顶部
                    SpikeFeed feed = new SpikeFeed();
                    feed.Set(eng.Spikes, null);
                    feed.ScrollTo(40);
                    // 注意：Set 会把传入列表反转成「新的在上」，所以这里每次都从
                    // eng.Spikes 重新取原始顺序，不能复用已被反转过的列表
                    List<SpikeRec> next = new List<SpikeRec>(eng.Spikes);
                    string beforeKey = feed.TopItem == null ? "?" : feed.TopItem.AtSec.ToString("0.####");
                    for (int i = 0; i < 3; i++)
                    {
                        // 模拟来了 3 条更新的尖峰（时间更晚，刷新后排在最前面）
                        SpikeRec n = new SpikeRec();
                        n.T = DateTime.Now.AddSeconds(i); n.Driver = "test.sys"; n.Where = "test.sys+0x1";
                        n.Type = "DPC"; n.Us = 600; n.AtSec = 99999 + i; n.SincePrevMs = 1000;
                        next.Add(n);
                    }
                    feed.Set(next, null);
                    string afterKey = feed.TopItem == null ? "?" : feed.TopItem.AtSec.ToString("0.####");
                    string scrollOk = (beforeKey == afterKey && beforeKey != "?") ? "通过" : "失败";
                    string scrollReport = "滚动前视口顶部 AtSec=" + beforeKey + " → 刷新后视口顶部 AtSec="
                        + afterKey + " → " + scrollOk;

                    // 回归：导出必须是完整归档，不能被 UI 的显示窗口截断
                    int csvLines = 0;
                    using (System.IO.StreamReader sr = new System.IO.StreamReader(csv))
                    {
                        while (sr.ReadLine() != null) csvLines++;
                    }
                    long archive = eng.SpikeTotal;
                    string exportReport = "导出 " + (csvLines - 1) + " 条 / 归档 " + archive + " 条（UI 窗口 "
                        + Engine.UiSpikeWindow + "）→ " + ((csvLines - 1 == archive) ? "通过" : "失败");

                    string report = "csv      = " + csv + Environment.NewLine
                        + "report   = " + rep + Environment.NewLine
                        + "spikes   = " + eng.Spikes.Count + Environment.NewLine
                        + "analysis = " + (pr == null ? "<null 未发现周期>" : pr.Describe()) + Environment.NewLine
                        + "scroll   = " + scrollReport + Environment.NewLine
                        + "export   = " + exportReport + Environment.NewLine;
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "selftest.txt"), report, System.Text.Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alpa_selftest_error.txt"),
                        ex.ToString(), System.Text.Encoding.UTF8);
                }
                return;
            }

            if (!Engine.IsElevated() && !noElevate)
            {
                // v1 是「非管理员就直接拒绝启动并偷偷建计划任务」，
                // v2 改成：请求一次 UAC，用户拒绝就降级运行（UI 里会挂提示条）。
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Application.ExecutablePath;
                    psi.Arguments = "--no-elevate";
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process p = Process.Start(psi);
                    if (p != null) return;
                }
                catch { }
            }

            Application.Run(new MainForm(startPage));
        }
    }
}
