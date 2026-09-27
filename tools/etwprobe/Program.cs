using System;
using System.IO;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace EtwProbe
{
    /// <summary>
    /// ETW 内核会话诊断探针。
    ///
    /// 用途：定位「启动 NT Kernel Logger 会话失败」的确切原因。
    /// 关键在于打印**完整异常堆栈**——没有堆栈只能看到一句中文报错，
    /// 无法判断是哪个重载、哪一行抛的。
    ///
    /// 用法（必须以管理员身份运行）：
    ///   etwprobe.exe &gt; 报告.txt 2&gt;&amp;1
    /// </summary>
    internal static class Program
    {
        private const string KernelName = "NT Kernel Logger";
        private const KernelTraceEventParser.Keywords Kw =
            (KernelTraceEventParser.Keywords)0x04 | (KernelTraceEventParser.Keywords)0x20 | (KernelTraceEventParser.Keywords)0x40;

        private static string _exeDir;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("================ ETW 内核会话诊断报告 ================");
            Console.WriteLine("时间        = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("管理员      = " + IsAdmin());
            Console.WriteLine("cwd         = " + Safe(delegate { return Directory.GetCurrentDirectory(); }));
            Console.WriteLine("temp        = " + Safe(delegate { return Path.GetTempPath(); }));
            Console.WriteLine("exeDir      = " + Safe(delegate { _exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location); return _exeDir; }));
            Console.WriteLine("windir      = " + (Environment.GetEnvironmentVariable("windir") ?? "<null>"));
            Console.WriteLine("ProcessPath = " + Safe(delegate { return Environment.GetEnvironmentVariable("Path").Length.ToString() + " 字符"; }));
            Console.WriteLine("TraceEvent  = " + typeof(TraceEventSession).Assembly.GetName().Version);
            Console.WriteLine("OS          = " + Environment.OSVersion + "  64bit=" + Environment.Is64BitProcess);
            Console.WriteLine();

            // ---- 1. 当前有哪些 ETW 会话在跑（含内核会话）----
            Console.WriteLine("---- 1. 活跃 ETW 会话（TraceEventSession.GetActiveSessionNames）");
            try
            {
                System.Collections.Generic.List<string> names = TraceEventSession.GetActiveSessionNames();
                if (names == null || names.Count == 0) Console.WriteLine("     （空）");
                else foreach (string n in names) Console.WriteLine("     " + n);
            }
            catch (Exception ex) { Console.WriteLine("     枚举失败: " + Describe(ex)); }
            Console.WriteLine();

            // ---- 2. 注册表里的 AutoLogger（有管理员权限才读得到）----
            Console.WriteLine("---- 2. WMI AutoLogger 配置（预占会话的常见来源）");
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\WMI\Autologger"))
                {
                    if (k == null) Console.WriteLine("     键不存在");
                    else
                    {
                        foreach (string sub in k.GetSubKeyNames())
                        {
                            using (Microsoft.Win32.RegistryKey s = k.OpenSubKey(sub))
                            {
                                object start = s == null ? null : s.GetValue("Start");
                                object file = s == null ? null : s.GetValue("FileName");
                                Console.WriteLine(string.Format("     {0,-32} Start={1}  FileName={2}", sub, start, file));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("     读取失败: " + Describe(ex)); }
            Console.WriteLine();

            // ---- 3. 各种构造方式逐个试，全部打完整堆栈 ----
            Console.WriteLine("---- 3. 各种启动方式对照（重点看堆栈）");
            Console.WriteLine();

            // 3.1 应用当前用的写法：单参数
            Variant("3.1  new TraceEventSession(\"NT Kernel Logger\")   ← 应用当前写法",
                delegate { return new TraceEventSession(KernelName); });

            // 3.2 显式走「实时会话」重载
            Variant("3.2  new TraceEventSession(name, TraceEventSessionOptions.Create)   ← 显式实时",
                delegate { return new TraceEventSession(KernelName, TraceEventSessionOptions.Create); });

            // 3.3 三参数显式 null 文件名
            Variant("3.3  new TraceEventSession(name, null, TraceEventSessionOptions.Create)",
                delegate { return new TraceEventSession(KernelName, null, TraceEventSessionOptions.Create); });

            // 3.4 实时 + 环形缓冲
            Variant("3.4  实时 + CircularBufferMB=64",
                delegate
                {
                    TraceEventSession s = new TraceEventSession(KernelName, TraceEventSessionOptions.Create);
                    s.CircularBufferMB = 64;
                    return s;
                });

            // 3.5 文件模式，写到自己 exe 目录
            Variant("3.5  文件模式（exe 目录下的 etl）",
                delegate
                {
                    string etl = Path.Combine(_exeDir ?? ".", "alpa_probe_kernel.etl");
                    return new TraceEventSession(KernelName, etl);
                });

            // 3.6 文件模式，写到 %TEMP%
            Variant("3.6  文件模式（%TEMP% 下的 etl）",
                delegate
                {
                    string etl = Path.Combine(Path.GetTempPath(), "alpa_probe_kernel.etl");
                    return new TraceEventSession(KernelName, etl);
                });

            // ---- 4. 先清掉残留会话，再试一次实时 ----
            Console.WriteLine("---- 4. 先强制停止残留的 kernel 会话，再试实时");
            try
            {
                using (TraceEventSession killer = new TraceEventSession(KernelName))
                {
                    killer.Stop(true);
                }
                Console.WriteLine("     Stop(true) 完成");
            }
            catch (Exception ex) { Console.WriteLine("     Stop(true) 失败: " + Describe(ex)); }
            System.Threading.Thread.Sleep(400);
            Variant("4.1  清理后重试实时会话",
                delegate { return new TraceEventSession(KernelName, TraceEventSessionOptions.Create); });

            Console.WriteLine();
            Console.WriteLine("================ 报告结束 ================");
            return 0;
        }

        private static void Variant(string label, Func<TraceEventSession> factory)
        {
            Console.WriteLine("### " + label);
            TraceEventSession session = null;
            try
            {
                session = factory();

                // 先看一下这个对象被设成了什么模式（不启动会话，纯 introspection）
                Console.WriteLine("     构造成功: IsRealTime=" + session.IsRealTime
                    + "  IsInMemoryCircular=" + session.IsInMemoryCircular
                    + "  FileName=" + (string.IsNullOrEmpty(session.FileName) ? "<null>" : session.FileName));

                session.EnableKernelProvider(Kw);
                Console.WriteLine("     >>> 成功：内核 provider 已启用");
            }
            catch (Exception ex)
            {
                Console.WriteLine("     >>> 失败：" + Describe(ex));
            }
            finally
            {
                if (session != null)
                {
                    try { session.Dispose(); } catch { }
                }
            }
            Console.WriteLine();
        }

        private static string Describe(Exception ex)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Exception e = ex;
            int depth = 0;
            while (e != null && depth < 4)
            {
                sb.AppendLine();
                sb.AppendLine("         [" + (depth == 0 ? "异常" : "inner " + depth) + "] " + e.GetType().FullName);
                sb.AppendLine("         消息: " + e.Message);
                if (e is System.ComponentModel.Win32Exception)
                    sb.AppendLine("         Win32 错误码: " + ((System.ComponentModel.Win32Exception)e).NativeErrorCode);
                if (!string.IsNullOrEmpty(e.StackTrace))
                {
                    sb.AppendLine("         堆栈:");
                    string[] lines = e.StackTrace.Split('\n');
                    for (int i = 0; i < lines.Length && i < 14; i++) sb.AppendLine("           " + lines[i].TrimEnd());
                }
                e = e.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch (Exception ex) { return "<异常 " + ex.Message + ">"; }
        }

        private static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
