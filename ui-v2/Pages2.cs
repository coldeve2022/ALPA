using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ALP2
{
    // =========================================================================================
    //  进程
    // =========================================================================================
    internal class ProcPage : PageBase
    {
        private readonly SearchBox _search = new SearchBox();
        private readonly Segmented _sortSeg;
        private readonly Card _cardTable = new Card();
        private readonly TableView _table = new TableView();
        private int _sortMode;

        public ProcPage(Engine e)
            : base(e)
        {
            HeadTitle = "进程";
            HeadSub = "按资源占用排序，Score = 线程数 + 内存/50 + 显存 + I/O(MB)。点表头可改成按单列排序。";

            _search.Placeholder = "按进程名筛选…";
            _search.Changed2 += delegate { _table.RefreshFilter(); };
            Controls.Add(_search);

            _sortSeg = new Segmented(new string[] { "综合分", "内存", "显存", "I/O" },
                new Icon[] { Icon.None, Icon.None, Icon.None, Icon.None });
            _sortSeg.SelectedChanged += delegate { _sortMode = _sortSeg.Selected; };
            Controls.Add(_sortSeg);

            _cardTable.Title = "进程资源表";
            _cardTable.Subtitle = "高优先级进程以蓝色标出；CPU 为累计时间，内存/显存取当前值；枚举在后台线程完成";
            _cardTable.TitleIcon = Icon.Layers;
            _table.RowHeight = 26;
            _table.EmptyText = "等待第一次进程采样…";
            _table.AddColumn(new TableColumn("进程名", 190) { Flex = true, MinWidth = 150 });
            _table.AddColumn(new TableColumn("PID", 56, true).Fit(48, 94));
            _table.AddColumn(new TableColumn("线程", 50, true).Fit(44, 80));
            _table.AddColumn(new TableColumn("优先", 62).Fit(58, 96));
            _table.AddColumn(new TableColumn("CPU", 62, true).Fit(56, 100));
            _table.AddColumn(new TableColumn("显存", 70, true).Fit(58, 108));
            _table.AddColumn(new TableColumn("内存", 72, true).Fit(58, 108));
            _table.AddColumn(new TableColumn("I/O", 74, true).Fit(58, 116));
            _table.AddColumn(new TableColumn("Score", 64, true).Fit(56, 100));
            _cardTable.Controls.Add(_table);
            Controls.Add(_cardTable);

            _table.Filter = delegate (TableRow r)
            {
                string q = _search.Value.Trim();
                if (q.Length == 0) return true;
                return r.Cells[0].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            };
        }

        public override string NavLabel { get { return "进程"; } }
        public override Icon NavIcon { get { return Icon.Layers; } }

        protected override void DoLayout()
        {
            int x0 = Pad, w = Width - Pad * 2;
            if (w < Theme.Px(200)) return;
            int y = ContentTop;
            int tbH = Theme.Px(30);
            _search.SetBounds(x0, y, Theme.Px(230), tbH);
            _sortSeg.SetBounds(x0 + Theme.Px(230) + Gap, y, Theme.Px(232), tbH);
            y += tbH + Gap;

            _cardTable.SetBounds(x0, y, w, Height - y - Theme.Px(10));
            int pad = Theme.Px(10);
            _table.SetBounds(pad, _cardTable.HeaderHeight, Math.Max(Theme.Px(20), _cardTable.Width - pad * 2),
                Math.Max(Theme.Px(20), _cardTable.Height - _cardTable.HeaderHeight - pad));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_table.Parent != null)
            {
                int pad = Theme.Px(10);
                _table.SetBounds(pad, _cardTable.HeaderHeight, Math.Max(Theme.Px(20), _cardTable.Width - pad * 2),
                    Math.Max(Theme.Px(20), _cardTable.Height - _cardTable.HeaderHeight - pad));
            }
        }

        /// <summary>滚轮只发给有焦点的控件；表不抢焦点，所以在页面这一层转发。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_table.Bounds.Contains(_table.PointToClient(Cursor.Position))) _table.Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        private List<ProcRow> _last = new List<ProcRow>();

        public override void OnSample(Snapshot s)
        {
            if (s.Procs == null || s.Procs.Count == 0) return;
            _last = s.Procs;

            List<ProcRow> sorted = new List<ProcRow>(_last);
            switch (_sortMode)
            {
                case 1: sorted.Sort(delegate (ProcRow a, ProcRow b) { return b.RamMb.CompareTo(a.RamMb); }); break;
                case 2: sorted.Sort(delegate (ProcRow a, ProcRow b) { return b.VramMb.CompareTo(a.VramMb); }); break;
                case 3: sorted.Sort(delegate (ProcRow a, ProcRow b) { return b.IoBytes.CompareTo(a.IoBytes); }); break;
                default: sorted.Sort(delegate (ProcRow a, ProcRow b) { return b.Score.CompareTo(a.Score); }); break;
            }

            List<TableRow> rows = new List<TableRow>(sorted.Count);
            foreach (ProcRow p in sorted)
            {
                TableRow r = new TableRow();
                r.Cells = new string[]
                {
                    p.Name,
                    p.Pid.ToString(),
                    p.Threads.ToString(),
                    p.Prio,
                    Fmt.Span(p.Cpu),
                    p.VramMb > 0 ? Fmt.Mb(p.VramMb) : "—",
                    Fmt.Mb(p.RamMb),
                    Fmt.Bytes(p.IoBytes),
                    p.Score.ToString("0.0")
                };
                r.Keys = new double[] { 0, p.Pid, p.Threads, p.PrioVal, p.Cpu.TotalSeconds, p.VramMb, p.RamMb, p.IoBytes / 1024.0, p.Score };
                double maxRam = 1; if (p.RamMb > maxRam) maxRam = p.RamMb;
                r.Bars = new double[] { 0, 0, 0, 0, 0, 0, Math.Min(1.0, p.RamMb / 2048.0), Math.Min(1.0, p.IoBytes / 1024.0 / 1024.0 / 8192.0), 0 };
                Color?[] cs = new Color?[9];
                cs[3] = p.PrioVal >= 5 ? Theme.Cur.Accent : Theme.Cur.TextSec;
                cs[4] = Theme.Cur.TextSec;
                cs[5] = p.VramMb > 1024 ? Theme.Cur.Info : Theme.Cur.TextSec;
                cs[8] = p.Score > 400 ? Theme.Cur.Warn : Theme.Cur.TextSec;
                r.Colors = cs;
                r.Tag = p;
                r.RowKey = p.Pid + "|" + p.Name;
                rows.Add(r);
            }
            _table.SetRows(rows);
            HeadRight = sorted.Count + " 个进程";
            Invalidate();
        }
    }

    // =========================================================================================
    //  系统体检
    // =========================================================================================
    internal class HealthPage : PageBase
    {
        private readonly Card _cardList = new Card();
        private readonly Card _cardNotes = new Card();
        private readonly Card _cardCore = new Card();
        private readonly Panel _list = new Panel();
        private readonly TextBlock _notes = new TextBlock();
        private readonly CoreGrid _cores = new CoreGrid();
        private readonly FlatButton _btnRescan = new FlatButton();
        private readonly List<HealthRow> _rows = new List<HealthRow>();

        public HealthPage(Engine e)
            : base(e)
        {
            HeadTitle = "系统体检";
            HeadSub = "影响输入延迟的关键开关。红点 = 明确有害，黄点 = 需要注意，绿点 = 正常。";

            _cardList.Title = "体检结果";
            _cardList.Subtitle = "首次扫描在后台完成，不会阻塞启动";
            _cardList.TitleIcon = Icon.ShieldCheck;
            _btnRescan.Text2 = "重新扫描";
            _btnRescan.Ico = Icon.Refresh;
            _btnRescan.Click += delegate { Eng.RescanChecks(); Eng.RescanStartup(); LoadRows(); };
            _cardList.Controls.Add(_btnRescan);

            _list.BackColor = Theme.Cur.Surface;
            _list.AutoScroll = true;
            _cardList.Controls.Add(_list);
            Controls.Add(_cardList);

            _cardNotes.Title = "怎么读这些指标";
            _cardNotes.TitleIcon = Icon.Info;
            _notes.Heading = "DPC / ISR 与输入延迟的关系";
            _notes.Lines = new string[]
            {
                "DPC（延迟过程调用）和 ISR（中断服务例程）都是内核态回调。它们运行时，系统会短暂占住当前 CPU 核心，优先级高于普通线程 —— 所以一次 1000µs 的 DPC 就足以让一帧画面迟到。",
                "看单个驱动的延迟要同时看三个数：P50 代表常态水平，P99 代表「经常性抖动」，Max 只代表最极端的一次。三者接近说明整体偏高；P50 很低但 Max 很高，说明是偶发尖峰。",
                "常见的高延迟来源：网卡驱动（ndis.sys / 厂商驱动）、USB 控制器驱动（usbxhci.sys）、显卡驱动、以及各类带网络过滤的安全软件内核驱动。",
                "判断顺序建议：先看「概览」页的曲线有没有周期性尖峰 → 再到「DPC / ISR 延迟」页按 Max 排序找驱动 → 用「进程」页确认后台是否有程序在跑。",
                "注意：跑测试时把电源计划切到高性能、关闭后台下载与同步，否则数据会混入无关噪声。"
            };
            _cardNotes.Controls.Add(_notes);
            Controls.Add(_cardNotes);

            _cardCore.Title = "每核心中断负载";
            _cardCore.Subtitle = "单核心被中断打满会明显卡顿；超 20000/s 标红";
            _cardCore.TitleIcon = Icon.Gauge;
            _cardCore.Controls.Add(_cores);
            Controls.Add(_cardCore);
        }

        public override string NavLabel { get { return "系统体检"; } }
        public override Icon NavIcon { get { return Icon.ShieldCheck; } }

        private void LoadRows()
        {
            _list.SuspendLayout();
            foreach (HealthRow r in _rows) { _list.Controls.Remove(r); r.Dispose(); }
            _rows.Clear();

            foreach (CheckItem c in Eng.Checks)
            {
                HealthRow r = new HealthRow();
                r.TitleText = c.Title;
                r.ValueText = c.Value;
                r.HintText = string.IsNullOrEmpty(c.Hint) ? "" : (c.Group + " · " + c.Hint);
                r.Sev = c.Sev;
                _rows.Add(r);
                _list.Controls.Add(r);
            }
            _list.ResumeLayout();
            LayoutRows();
        }

        private void LayoutRows()
        {
            int rh = Theme.Px(46);
            int total = _rows.Count * rh;
            _list.AutoScrollMinSize = new Size(1, Math.Max(0, total));
            // 两遍布局：第一遍行宽会让纵向滚动条出现，客户区随之变窄；
            // 第二遍用变窄后的宽度重排，否则行比客户区宽会凭空多出一条横向滚动条
            // （上一版就是这样，右侧数值被挤到看不见）。
            for (int pass = 0; pass < 2; pass++)
            {
                int w = Math.Max(Theme.Px(40), _list.ClientSize.Width);
                for (int i = 0; i < _rows.Count; i++)
                    _rows[i].SetBounds(0, i * rh, w, rh);
            }
        }

        protected override void DoLayout()
        {
            int x0 = Pad, w = Width - Pad * 2;
            if (w < Theme.Px(200)) return;
            int y = ContentTop;

            int rightW = Math.Min(Theme.Px(392), Math.Max(Theme.Px(300), w * 34 / 100));
            int leftW = w - rightW - Gap;
            int h = Height - y - Theme.Px(10);
            if (h < Theme.Px(200)) h = Theme.Px(200);

            int coreH = Math.Max(Theme.Px(112), h * 26 / 100);

            _cardList.SetBounds(x0, y, leftW, h);
            _cardCore.SetBounds(x0 + leftW + Gap, y, rightW, coreH);
            _cardNotes.SetBounds(x0 + leftW + Gap, y + coreH + Gap, rightW, h - coreH - Gap);

            int pad = Theme.Px(12);
            _btnRescan.SetBounds(_cardList.Width - Theme.Px(100) - pad, Theme.Px(11), Theme.Px(100), Theme.Px(24));
            _list.SetBounds(pad, _cardList.HeaderHeight, Math.Max(Theme.Px(20), _cardList.Width - pad * 2),
                Math.Max(Theme.Px(20), _cardList.Height - _cardList.HeaderHeight - pad));

            _cores.SetBounds(pad, _cardCore.HeaderHeight, Math.Max(Theme.Px(20), _cardCore.Width - pad * 2),
                Math.Max(Theme.Px(20), _cardCore.Height - _cardCore.HeaderHeight - pad));
            _notes.SetBounds(pad, _cardNotes.HeaderHeight, Math.Max(Theme.Px(20), _cardNotes.Width - pad * 2),
                Math.Max(Theme.Px(20), _cardNotes.Height - _cardNotes.HeaderHeight - pad));

            LayoutRows();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRows();
        }

        public override void ApplyTheme()
        {
            base.ApplyTheme();
            _list.BackColor = Theme.Cur.Surface;
        }

        private bool _loaded;

        public override void OnSample(Snapshot s)
        {
            if (!_loaded && Eng.BootScanDone)
            {
                _loaded = true;
                LoadRows();
                HeadRight = "扫描完成";
            }
            int n = 0, warn = 0, crit = 0;
            foreach (CheckItem c in Eng.Checks)
            {
                n++;
                if (c.Sev == Sev.Warn) warn++;
                if (c.Sev == Sev.Crit) crit++;
            }
            _cores.Set(s.CoreInterrupts, 15000f, 20000f);
            if (_loaded) HeadRight = crit > 0 ? (crit + " 项需要处理") : (warn > 0 ? warn + " 项建议关注" : "未发现明显问题");
        }
    }

    // =========================================================================================
    //  启动项
    // =========================================================================================
    internal class StartupPage : PageBase
    {
        private readonly SearchBox _search = new SearchBox();
        private readonly Segmented _catSeg;
        private readonly FlatButton _btnRescan = new FlatButton();
        private readonly Card _cardTable = new Card();
        private readonly TableView _table = new TableView();
        private int _cat;

        public StartupPage(Engine e)
            : base(e)
        {
            HeadTitle = "启动项";
            HeadSub = "启动文件夹、注册表 Run、计划任务与第三方服务 —— 计划任务是最常被忽略的藏身点。";

            _search.Placeholder = "按名称筛选…";
            _search.Changed2 += delegate { _table.RefreshFilter(); };
            Controls.Add(_search);

            _catSeg = new Segmented(new string[] { "全部", "启动文件夹", "注册表", "计划任务", "服务" },
                new Icon[] { Icon.None, Icon.None, Icon.None, Icon.None, Icon.None });
            _catSeg.Width = Theme.Px(400);
            _catSeg.SelectedChanged += delegate { _cat = _catSeg.Selected; _table.RefreshFilter(); };
            Controls.Add(_catSeg);

            _btnRescan.Text2 = "重新扫描";
            _btnRescan.Ico = Icon.Refresh;
            _btnRescan.Click += delegate { Eng.RescanStartup(); Reload(); };
            Controls.Add(_btnRescan);

            _cardTable.Title = "启动项清单";
            _cardTable.Subtitle = "只做展示与定位，不会替你改动系统（禁用请用 msconfig / services.msc / 任务计划程序）";
            _cardTable.TitleIcon = Icon.Rocket;
            _table.RowHeight = 26;
            _table.EmptyText = "扫描中…";
            _table.AddColumn(new TableColumn("类型", 96).Fit(72, 150));
            _table.AddColumn(new TableColumn("名称", 220) { Flex = true, MinWidth = 150 });
            _table.AddColumn(new TableColumn("来源 / 详情", 420) { Flex = true, MinWidth = 190 });
            _cardTable.Controls.Add(_table);
            Controls.Add(_cardTable);

            _table.Filter = delegate (TableRow r)
            {
                if (_cat != 0)
                {
                    string[] cats = { "", "启动文件夹", "注册表 Run", "计划任务", "服务" };
                    if (r.Cells[0] != cats[_cat]) return false;
                }
                string q = _search.Value.Trim();
                if (q.Length == 0) return true;
                return r.Cells[1].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            };
        }

        public override string NavLabel { get { return "启动项"; } }
        public override Icon NavIcon { get { return Icon.Rocket; } }

        protected override void DoLayout()
        {
            int x0 = Pad, w = Width - Pad * 2;
            if (w < Theme.Px(200)) return;
            int y = ContentTop;
            int tbH = Theme.Px(30);
            _search.SetBounds(x0, y, Theme.Px(220), tbH);
            _catSeg.SetBounds(x0 + Theme.Px(220) + Gap, y, Theme.Px(392), tbH);
            _btnRescan.SetBounds(x0 + w - Theme.Px(108), y, Theme.Px(108), tbH);
            y += tbH + Gap;

            _cardTable.SetBounds(x0, y, w, Height - y - Theme.Px(10));
            LayoutInner();
        }

        private void LayoutInner()
        {
            int pad = Theme.Px(10);
            _table.SetBounds(pad, _cardTable.HeaderHeight, Math.Max(Theme.Px(20), _cardTable.Width - pad * 2),
                Math.Max(Theme.Px(20), _cardTable.Height - _cardTable.HeaderHeight - pad));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private bool _loaded;

        /// <summary>滚轮只发给有焦点的控件；表不抢焦点，所以在页面这一层转发。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_table.Bounds.Contains(_table.PointToClient(Cursor.Position))) _table.Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        public override void OnSample(Snapshot s)
        {
            if (!_loaded && Eng.BootScanDone)
            {
                _loaded = true;
                Reload();
            }
        }

        private void Reload()
        {
            List<StartupItem> items = Eng.Startup;
            List<TableRow> rows = new List<TableRow>(items.Count);
            foreach (StartupItem it in items)
            {
                TableRow r = new TableRow();
                r.Cells = new string[] { it.Category, it.Name, it.Detail };
                Color?[] cs = new Color?[3];
                cs[0] = it.Sev == Sev.Warn ? Theme.Cur.Warn : Theme.Cur.TextSec;
                cs[2] = Theme.Cur.TextMuted;
                r.Colors = cs;
                r.Tint = null;
                r.Tag = it;
                rows.Add(r);
            }
            _table.SetRows(rows);
            HeadRight = items.Count + " 项";
            Invalidate();
        }
    }

    // =========================================================================================
    //  控制台
    // =========================================================================================
    internal class ConsolePage : PageBase
    {
        private readonly Card _cardLog = new Card();
        private readonly RichTextBox _rtb = new RichTextBox();
        private readonly Segmented _lvlSeg;
        private readonly FlatButton _btnClear = new FlatButton();
        private readonly FlatButton _btnOpenLog = new FlatButton();
        private readonly List<LogEntry> _all = new List<LogEntry>();
        private int _lvl;

        public ConsolePage(Engine e)
            : base(e)
        {
            HeadTitle = "控制台";
            HeadSub = "引擎的完整运行日志。每次运行都会在程序目录生成独立的日志文件，不会无限追加。";

            _lvlSeg = new Segmented(new string[] { "全部", "警告以上", "仅错误" },
                new Icon[] { Icon.None, Icon.None, Icon.None });
            _lvlSeg.Width = Theme.Px(276);
            _lvlSeg.SelectedChanged += delegate { _lvl = _lvlSeg.Selected; Rebuild(); };
            Controls.Add(_lvlSeg);

            _btnClear.Text2 = "清空显示";
            _btnClear.Click += delegate { _rtb.Clear(); };
            Controls.Add(_btnClear);

            _btnOpenLog.Text2 = "打开日志文件";
            _btnOpenLog.Ico = Icon.Folder;
            _btnOpenLog.Click += delegate
            {
                try
                {
                    if (!string.IsNullOrEmpty(Eng.LogFile) && System.IO.File.Exists(Eng.LogFile))
                        System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + Eng.LogFile + "\"");
                    else
                        System.Diagnostics.Process.Start("explorer.exe", "\"" + Engine.AppDir + "\"");
                }
                catch { }
            };
            Controls.Add(_btnOpenLog);

            _cardLog.Title = "引擎日志";
            _cardLog.TitleIcon = Icon.Terminal;
            _rtb.BorderStyle = BorderStyle.None;
            _rtb.Font = Theme.Fm(8.6f, FontStyle.Regular);
            _rtb.ReadOnly = true;
            _rtb.WordWrap = false;
            _rtb.ScrollBars = RichTextBoxScrollBars.Both;
            _rtb.BackColor = Theme.Cur.Surface;
            _rtb.ForeColor = Theme.Cur.TextPri;
            _rtb.DetectUrls = false;
            _cardLog.Controls.Add(_rtb);
            Controls.Add(_cardLog);
        }

        public override string NavLabel { get { return "控制台"; } }
        public override Icon NavIcon { get { return Icon.Terminal; } }

        private bool Pass(LogEntry e)
        {
            if (_lvl == 1) return e.Lv == LogLevel.Warn || e.Lv == LogLevel.Crit;
            if (_lvl == 2) return e.Lv == LogLevel.Crit;
            return true;
        }

        private Color ColOf(LogLevel lv)
        {
            switch (lv)
            {
                case LogLevel.Ok: return Theme.Cur.Ok;
                case LogLevel.Warn: return Theme.Cur.Warn;
                case LogLevel.Crit: return Theme.Cur.Crit;
                case LogLevel.Muted: return Theme.Cur.TextMuted;
                default: return Theme.Cur.TextSec;
            }
        }

        public void Append(LogEntry e)
        {
            _all.Add(e);
            if (_all.Count > 5000) _all.RemoveRange(0, 1000);
            if (!Pass(e)) return;
            if (_rtb.TextLength > 600000) { _rtb.Clear(); Rebuild(); return; }
            AppendRich(e);
        }

        private void AppendRich(LogEntry e)
        {
            _rtb.SelectionStart = _rtb.TextLength;
            _rtb.SelectionLength = 0;
            _rtb.SelectionColor = Theme.Cur.TextMuted;
            _rtb.AppendText("[" + e.T.ToString("HH:mm:ss") + "] ");
            _rtb.SelectionColor = ColOf(e.Lv);
            _rtb.AppendText(e.Text + Environment.NewLine);
            _rtb.SelectionStart = _rtb.TextLength;
            _rtb.ScrollToCaret();
        }

        private void Rebuild()
        {
            _rtb.SuspendLayout();
            _rtb.Clear();
            int from = Math.Max(0, _all.Count - 1200);
            for (int i = from; i < _all.Count; i++) if (Pass(_all[i])) AppendRich(_all[i]);
            _rtb.ResumeLayout();
        }

        public override void ApplyTheme()
        {
            base.ApplyTheme();
            _rtb.BackColor = Theme.Cur.Surface;
            _rtb.ForeColor = Theme.Cur.TextPri;
            Rebuild();
        }

        protected override void DoLayout()
        {
            int x0 = Pad, w = Width - Pad * 2;
            if (w < Theme.Px(200)) return;
            int y = ContentTop;
            int tbH = Theme.Px(30);
            _lvlSeg.SetBounds(x0, y, Theme.Px(270), tbH);
            _btnOpenLog.SetBounds(x0 + w - Theme.Px(126), y, Theme.Px(126), tbH);
            _btnClear.SetBounds(_btnOpenLog.Left - Theme.Px(96) - Gap, y, Theme.Px(96), tbH);
            y += tbH + Gap;

            _cardLog.SetBounds(x0, y, w, Height - y - Theme.Px(10));
            int pad = Theme.Px(12);
            _rtb.SetBounds(pad, _cardLog.HeaderHeight, Math.Max(Theme.Px(20), _cardLog.Width - pad * 2),
                Math.Max(Theme.Px(20), _cardLog.Height - _cardLog.HeaderHeight - pad));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_rtb.Parent != null)
            {
                int pad = Theme.Px(12);
                _rtb.SetBounds(pad, _cardLog.HeaderHeight, Math.Max(Theme.Px(20), _cardLog.Width - pad * 2),
                    Math.Max(Theme.Px(20), _cardLog.Height - _cardLog.HeaderHeight - pad));
            }
        }
    }
}
