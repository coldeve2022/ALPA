# -*- coding: utf-8 -*-
"""第四部分（剩余）：DPC 页报告按钮 + 完整列表 + 滚轮转发；其它页的滚轮转发。"""
import io

P = 'ui-v2/Pages.cs'
s = io.open(P, encoding='utf-8').read()

def rep(a, b, what):
    global s
    if a not in s:
        raise SystemExit('NOT FOUND [%s]:\n%s' % (what, a[:300]))
    s = s.replace(a, b, 1)
    print('ok:', what)

rep('''            _btnExport.Text2 = "导出驱动 CSV";
            _btnExport.Ico = Icon.Export;
            _btnExport.Click += delegate { Export(true); };
            Controls.Add(_btnExport);''',
    '''            _btnExport.Text2 = "导出驱动 CSV";
            _btnExport.Ico = Icon.Export;
            _btnExport.Click += delegate { Export(true); };
            Controls.Add(_btnExport);

            _btnReport.Text2 = "导出报告";
            _btnReport.Ico = Icon.Info;
            _btnReport.Click += delegate { ExportReportFile(); };
            Controls.Add(_btnReport);''', 'report button init')

rep('''        private readonly FlatButton _btnExport = new FlatButton();
        private readonly Card _cardTable = new Card();''',
    '''        private readonly FlatButton _btnExport = new FlatButton();
        private readonly FlatButton _btnReport = new FlatButton();
        private readonly Card _cardTable = new Card();''', 'report field')

rep('''            _btnExport.SetBounds(rightBtn - Theme.Px(136), y, Theme.Px(136), tbH);
            _btnReset.SetBounds(_btnExport.Left - Theme.Px(104) - Gap, y, Theme.Px(104), tbH);''',
    '''            _btnReport.SetBounds(rightBtn - Theme.Px(110), y, Theme.Px(110), tbH);
            _btnExport.SetBounds(_btnReport.Left - Theme.Px(136) - Gap, y, Theme.Px(136), tbH);
            _btnReset.SetBounds(_btnExport.Left - Theme.Px(104) - Gap, y, Theme.Px(104), tbH);''', 'toolbar layout')

rep('''        private void Export(bool drivers)
        {''',
    '''        private void ExportReportFile()
        {
            try
            {
                string f = Eng.ExportReport(Engine.AppDir);
                Eng.Write("report exported: " + f, LogLevel.Ok);
                MessageBox.Show(this, "已导出分析报告（结论/系统信息/DPC与ISR统计/每核心数据/周期判定）：\\n" + f,
                    "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Export(bool drivers)
        {''', 'export report action')

rep('''            int take = Math.Min(60, sp.Count);
            List<SpikeRec> tail = new List<SpikeRec>();
            for (int i = sp.Count - take; i < sp.Count; i++) tail.Add(sp[i]);
            _feed.Set(tail, s.Tracing ? null : "未启用内核追踪（需要管理员权限）");''',
    '''            _feed.Set(sp, s.Tracing ? null : "未启用内核追踪（需要管理员权限）");''', 'dpc full list')

rep('''        private void SyncDetail()
        {
            TableRow r = _table.SelectedRow;''',
    '''        /// <summary>
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
            TableRow r = _table.SelectedRow;''', 'dpc wheel')

io.open(P, 'w', encoding='utf-8').write(s)
print('Pages.cs done, len=%d' % len(s))
