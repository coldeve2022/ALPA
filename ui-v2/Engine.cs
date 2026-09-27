using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;
using System.Management;

namespace ALP2
{
    internal enum LogLevel
    {
        Muted,
        Info,
        Ok,
        Warn,
        Crit
    }

    internal class LogEntry
    {
        public DateTime T;
        public string Text = "";
        public LogLevel Lv;
    }

    internal class SpikeRec
    {
        public DateTime T;
        public string Driver = "";
        public string Type = "";
        public double Us;
        /// <summary>模块 + 模块内偏移，例如 ntoskrnl.exe+0x1a2b40。</summary>
        public string Where = "";
        /// <summary>距上一个尖峰的毫秒数（第一个为 0）。判断"固定间隔"靠它。</summary>
        public double SincePrevMs;
        /// <summary>自本次统计清零以来的秒数，导出时不受 Excel 改格式影响。</summary>
        public double AtSec;
    }

    /// <summary>
    /// 对数分桶直方图：用来算 P50/P95/P99。
    /// v1 只有 Min/Max/Avg —— 但 Max 只反映一次抖动，Avg 会被海量小值稀释，
    /// 真正能说明「抖得频不频繁」的是 P99。这是本次改动里最有诊断价值的一项。
    /// </summary>
    internal class Histogram
    {
        private static readonly double[] Edges;
        private readonly int[] _b;
        private long _n;
        private double _sum;
        private double _max;

        static Histogram()
        {
            List<double> e = new List<double>();
            e.Add(0.0);
            double v = 0.25;
            while (v < 2000000.0) { e.Add(v); v *= 1.25; }
            e.Add(double.MaxValue);
            Edges = e.ToArray();
        }

        public Histogram()
        {
            _b = new int[Edges.Length];
        }

        public long N { get { return _n; } }
        public double Max { get { return _max; } }
        public double Avg { get { return _n > 0 ? _sum / _n : 0.0; } }
        public double Sum { get { return _sum; } }

        public void Add(double us)
        {
            if (us < 0) us = 0;
            _n++;
            _sum += us;
            if (us > _max) _max = us;
            int i = Index(us);
            _b[i]++;
        }

        private static int Index(double us)
        {
            int lo = 0, hi = Edges.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Edges[mid] <= us) lo = mid + 1;
                else hi = mid;
            }
            return Math.Min(Edges.Length - 1, lo);
        }

        public double Percentile(double q)
        {
            if (_n == 0) return 0;
            long target = (long)Math.Ceiling(q * _n);
            if (target < 1) target = 1;
            long acc = 0;
            for (int i = 0; i < _b.Length; i++)
            {
                acc += _b[i];
                if (acc >= target)
                {
                    // 分桶返回的是桶的上界，可能大于实际观测到的最大值 ——
                    // 不夹一下就会出现「P99 315.5 > Max 264.4」这种自相矛盾的数字
                    double e = Edges[Math.Min(i, Edges.Length - 1)];
                    return e > _max ? _max : e;
                }
            }
            return _max;
        }

        public void Reset()
        {
            Array.Clear(_b, 0, _b.Length);
            _n = 0; _sum = 0; _max = 0;
        }
    }

    internal class DriverStat
    {
        public string Name = "";
        public string Type = "";
        public double Cur;
        public long Count;
        public readonly Histogram Hist = new Histogram();

        public double Max { get { return Hist.Max; } }
        public double Avg { get { return Hist.Avg; } }
        public double P50 { get { return Hist.Percentile(0.50); } }
        public double P95 { get { return Hist.Percentile(0.95); } }
        public double P99 { get { return Hist.Percentile(0.99); } }
    }

    /// <summary>单核心的中断统计，报告的「每核心数据」节用。</summary>
    internal class CpuAgg
    {
        public long Count;
        public double TotalUs;
        public double MaxUs;
    }

    internal class ProcRow
    {
        public int Pid;
        public string Name = "";
        public int Threads;
        public string Prio = "Normal";
        public int PrioVal = 3;
        public TimeSpan Cpu;
        public double RamMb;
        public double VramMb;
        public ulong IoBytes;
        public double Score;
    }

    internal class DiskRow
    {
        public string Name = "";
        public double Queue;
        public double LatencyMs;
        public double ActivePct;
    }

    internal class CheckItem
    {
        public string Group = "";
        public string Title = "";
        public string Value = "";
        public string Hint = "";
        public Sev Sev = Sev.Neutral;
    }

    internal class StartupItem
    {
        public string Category = "";
        public string Name = "";
        public string Detail = "";
        public Sev Sev = Sev.Neutral;
    }

    internal class Snapshot
    {
        public DateTime T = DateTime.Now;
        public List<DriverStat> Dpc = new List<DriverStat>();
        public List<DriverStat> Isr = new List<DriverStat>();
        public List<ProcRow> Procs = new List<ProcRow>();
        public List<DiskRow> Disks = new List<DiskRow>();

        public double TimerMs;
        public double MouseHz;
        public float ProcQueue;
        public float ContextSw;
        public float InterruptsTotal;
        public float Parked;
        public float PageFaults;
        public float AvailMb;
        public float CacheMb;
        public float UdpErr;
        public float[] CoreInterrupts = new float[0];
        public double NetMbs;
        public double DiskPct;

        public double DpcPerSec;
        public double IsrPerSec;
        public double MaxDpc;
        public double MaxIsr;
        public double AvgDpc;
        public string WorstDriver = "";
        public int SpikeCount;
        public bool Tracing;
    }

    /// <summary>
    /// 引擎：只做数据采集，完全不碰 UI。
    /// 与 v1 的差别：① 事件统计改用直方图 → 能出百分位；② 驱动名解析加缓存
    /// （v1 每来一个 DPC 事件都要在几百个驱动里线性扫一遍，高频时纯烧 CPU）；
    /// ③ 进程枚举移到独立后台线程（v1 跑在 UI 线程上，每秒卡一下界面）。
    /// </summary>
    internal class Engine
    {
        // ---------------- Win32 ----------------
        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint min, out uint max, out uint cur);
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PROCESS_POWER_THROTTLING_STATE st, int len, out int ret);
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("psapi.dll")]
        private static extern bool EnumDeviceDrivers(IntPtr[] addrs, uint sizeBytes, out uint needed);
        [DllImport("psapi.dll")]
        private static extern int GetDeviceDriverBaseName(IntPtr addr, StringBuilder name, int size);
        [DllImport("kernel32.dll")]
        private static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS c);
        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint num, uint size);

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version, ControlMask, StateMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage, usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        // ---------------- 状态 ----------------
        public event Action<LogEntry> Log;
        public event Action<SpikeRec> Spike;
        public event Action<Snapshot> Sample;

        public bool IsAdmin { get; private set; }
        public bool IsTracing { get { return _session != null; } }

        public double DpcThreshold = 500.0;   // 请求的 DPC 尖峰阈值（µs）
        public double IsrThreshold = 250.0;

        private readonly object _statLock = new object();
        private readonly Dictionary<string, DriverStat> _dpc = new Dictionary<string, DriverStat>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DriverStat> _isr = new Dictionary<string, DriverStat>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, string> _modMap = new Dictionary<ulong, string>();
        private readonly List<SpikeRec> _spikes = new List<SpikeRec>();
        // 600 条会被一阵密集尖峰冲满（实测：中位间隔 1.4ms 时，600 条只覆盖一两秒），
        // 那样长周期分析就没数据可用了。放宽到 4000，并在报告里写上实际覆盖的时间跨度。
        private const int MaxSpikes = 4000;

        private TraceEventSession _session;
        private Thread _traceThread;
        private Thread _sampleThread;
        private volatile bool _running;
        private volatile bool _stopping;

        private readonly long[] _dpcCountAtLast = new long[2];
        private readonly long[] _isrCountAtLast = new long[2];
        private long _spikeCount;
        private long _lastSpikeTs;   // Stopwatch.GetTimestamp()，用于算尖峰间隔

        // ---- 报告用的聚合口径（每核心 / 分档 / 总量）----
        private CpuAgg[] _dpcCpu = new CpuAgg[Math.Max(1, Environment.ProcessorCount)];
        private CpuAgg[] _isrCpu = new CpuAgg[Math.Max(1, Environment.ProcessorCount)];
        private readonly long[] _dpcBuckets = new long[6];
        private readonly long[] _isrBuckets = new long[6];
        private double _dpcTotalUs;
        private double _isrTotalUs;

        /// <summary>LatencyMon 式的分档区间（微秒）。</summary>
        public static readonly string[] BucketNames = { "<250", "250-500", "500-999", "1000-1999", "2000-3999", ">=4000" };

        private static int BucketIndex(double us)
        {
            if (us < 250) return 0;
            if (us < 500) return 1;
            if (us < 1000) return 2;
            if (us < 2000) return 3;
            if (us < 4000) return 4;
            return 5;
        }

        // 性能计数器
        private PerformanceCounter _pcProcQueue, _pcCtxSw, _pcIntTotal, _pcParked;
        private PerformanceCounter _pcPageFault, _pcAvail, _pcCache, _pcUdpErr;
        private readonly List<PerformanceCounter> _pcCoreInt = new List<PerformanceCounter>();
        private readonly Dictionary<string, PerformanceCounter[]> _disk = new Dictionary<string, PerformanceCounter[]>();
        private readonly List<PerformanceCounter> _net = new List<PerformanceCounter>();

        private readonly object _procLock = new object();
        private List<ProcRow> _procs = new List<ProcRow>();
        private readonly List<CheckItem> _checks = new List<CheckItem>();
        private readonly List<StartupItem> _startup = new List<StartupItem>();

        private string _logFile = "";
        private readonly object _fileLock = new object();

        // 鼠标 Raw Input
        private long _lastInputTicks;
        private readonly List<double> _inputGaps = new List<double>();
        private double _mouseHz;

        private readonly Stopwatch _uptime = new Stopwatch();

        public Engine()
        {
            IsAdmin = IsElevated();
        }

        private static string _appDir;

        /// <summary>
        /// 可执行文件所在目录。v1 全程用相对路径（"ALPA_Log.txt" 等），
        /// 一旦通过 UAC 提权启动，工作目录会变成 system32，日志和 CSV 就会落到系统目录。
        /// </summary>
        public static string AppDir
        {
            get
            {
                if (_appDir == null)
                {
                    try { _appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
                    catch { _appDir = ""; }
                    if (string.IsNullOrEmpty(_appDir)) _appDir = Environment.CurrentDirectory;
                }
                return _appDir;
            }
        }

        public static bool IsElevated()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public TimeSpan Uptime { get { return _uptime.Elapsed; } }
        public string LogFile { get { return _logFile; } }

        public void Write(string msg, LogLevel lv)
        {
            LogEntry e = new LogEntry();
            e.T = DateTime.Now;
            e.Text = msg;
            e.Lv = lv;
            Action<LogEntry> h = Log;
            if (h != null) h(e);
            if (!string.IsNullOrEmpty(_logFile))
            {
                lock (_fileLock)
                {
                    try { File.AppendAllText(_logFile, "[" + e.T.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + Environment.NewLine); }
                    catch { }
                }
            }
        }

        // =====================================================================
        //  启动 / 停止
        // =====================================================================
        public void Start(string workDir)
        {
            _running = true;
            _stopping = false;
            _uptime.Start();

            try
            {
                if (string.IsNullOrEmpty(_logFile))
                    _logFile = Path.Combine(workDir, "ALPA_v2_Log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            }
            catch { _logFile = ""; }

            Write("ALPA v2 engine starting (admin=" + (IsAdmin ? "yes" : "no") + ")", LogLevel.Info);

            // 1) 环境自检
            uint mi, ma, cu;
            if (NtQueryTimerResolution(out mi, out ma, out cu) == 0)
                Write("ntdll timer resolution API reachable (min " + (mi / 10000.0).ToString("0.0000") + " ms)", LogLevel.Ok);
            else
                Write("ntdll timer API failed", LogLevel.Warn);

            // 真正去解析一次追踪库，而不是 File.Exists。
            //   · 单文件版把 DLL 打包在 exe 资源里，磁盘上就没有这个文件，
            //     用 File.Exists 判断会误报「缺失」，进而把内核追踪整个关掉；
            //   · 顺带还能拿到版本号，排查问题时比「found」有用得多。
            string traceErr;
            string traceId = ProbeTraceLib(out traceErr);
            if (traceId != null)
                Write("trace library ready: " + traceId, LogLevel.Ok);
            else
                Write("trace library unavailable - DPC/ISR tracing disabled"
                    + (string.IsNullOrEmpty(traceErr) ? "" : " (" + traceErr + ")"), LogLevel.Crit);

            // 原生依赖（KernelTraceControl.dll 等）能不能落盘并可加载。
            // 这一步不需要管理员权限，所以「为什么会没数据」在提权之前就能看出线索。
            string pre = Boot.Preflight();
            if (pre.StartsWith("ok"))
                Write("native preflight " + pre, LogLevel.Ok);
            else
                Write("native preflight " + pre, LogLevel.Crit);

            // 2) 计数器
            InitCounters();

            // 3) 静态体检 + 启动项扫描（一次性，不占 UI 线程）
            Thread boot = new Thread(delegate ()
            {
                try { BuildChecks(); } catch (Exception ex) { Write("check scan failed: " + ex.Message, LogLevel.Warn); }
                try { ScanStartup(); } catch (Exception ex) { Write("startup scan failed: " + ex.Message, LogLevel.Warn); }
                _bootDone = true;
            });
            boot.IsBackground = true;
            boot.Name = "ALPA-Checks";
            boot.Start();

            // 4) 进程采样线程
            _sampleThread = new Thread(SampleLoop);
            _sampleThread.IsBackground = true;
            _sampleThread.Name = "ALPA-Sampler";
            _sampleThread.Start();

            // 5) ETW
            if (IsAdmin) StartTracing();
            else Write("not elevated - DPC/ISR kernel tracing disabled", LogLevel.Warn);
        }

        private volatile bool _bootDone;

        public bool BootScanDone { get { return _bootDone; } }

        /// <summary>
        /// 只重启内核追踪线程，不碰引擎其它部分。UI 上「重试追踪」按钮用。
        /// 典型场景：启动时那次会话创建和别人抢了（比如同时开着别的延迟监控工具），
        /// 对方退出后重试就能成功，不用重启整个程序。
        /// </summary>
        public void RetryTracing()
        {
            try
            {
                TraceEventSession s = _session;
                _session = null;
                if (s != null) { try { s.Dispose(); } catch { } }
            }
            catch { }
            _traceError = null;
            if (IsAdmin) StartTracing();
            else Write("retry tracing skipped - not elevated", LogLevel.Warn);
        }

        /// <summary>手动停掉内核会话（不改动 _running，仅追踪层）。</summary>
        private void StopTracingSession()
        {
            TraceEventSession s = _session;
            _session = null;
            if (s != null) { try { s.Dispose(); } catch { } }
            try
            {
                foreach (string name in TraceEventSession.GetActiveSessionNames())
                {
                    if (name == "NT Kernel Logger")
                    {
                        using (TraceEventSession k = new TraceEventSession(name)) k.Stop(true);
                    }
                }
            }
            catch { }
        }

        public void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            _running = false;
            _uptime.Stop();

            TraceEventSession s = _session;
            _session = null;
            if (s != null)
            {
                try { s.Dispose(); }
                catch { }
            }
            // 上游 v1 在退出时去找 "ALPKernelSession"，但真正开的会话叫 "NT Kernel Logger"，
            // 结果内核会话根本没被关掉（下次运行 ETW 会报会话已存在）。
            // 这里按真实名字兜底停一次。
            try
            {
                foreach (string name in TraceEventSession.GetActiveSessionNames())
                {
                    if (name == "NT Kernel Logger")
                    {
                        using (TraceEventSession k = new TraceEventSession(name)) k.Stop(true);
                    }
                }
            }
            catch { }

            DisposeCounters();
            Write("engine stopped", LogLevel.Info);
        }

        private void DisposeCounters()
        {
            PerformanceCounter[] all = new PerformanceCounter[]
            {
                _pcProcQueue, _pcCtxSw, _pcIntTotal, _pcParked,
                _pcPageFault, _pcAvail, _pcCache, _pcUdpErr
            };
            foreach (PerformanceCounter c in all) { try { if (c != null) c.Dispose(); } catch { } }
            foreach (PerformanceCounter c in _pcCoreInt) { try { c.Dispose(); } catch { } }
            foreach (KeyValuePair<string, PerformanceCounter[]> kv in _disk)
                foreach (PerformanceCounter c in kv.Value) { try { c.Dispose(); } catch { } }
            foreach (PerformanceCounter c in _net) { try { c.Dispose(); } catch { } }
        }

        // =====================================================================
        //  性能计数器
        // =====================================================================
        private PerformanceCounter Init(string cat, string counter, string inst)
        {
            try
            {
                if (!PerformanceCounterCategory.Exists(cat)) return null;
                PerformanceCounter pc = inst == null
                    ? new PerformanceCounter(cat, counter)
                    : new PerformanceCounter(cat, counter, inst);
                pc.NextValue();
                return pc;
            }
            catch { return null; }
        }

        private void InitCounters()
        {
            _pcProcQueue = Init("System", "Processor Queue Length", null);
            _pcCtxSw = Init("System", "Context Switches/sec", null);
            _pcIntTotal = Init("Processor", "Interrupts/sec", "_Total");
            _pcParked = Init("Processor Information", "Parking Status", "_Total");
            _pcPageFault = Init("Memory", "Page Faults/sec", null);
            _pcAvail = Init("Memory", "Available MBytes", null);
            _pcCache = Init("Memory", "Cache Bytes", null);
            _pcUdpErr = Init("UDPv4", "Datagrams Received Errors", null);

            _pcCoreInt.Clear();
            int ncpu = Environment.ProcessorCount;
            for (int i = 0; i < ncpu; i++)
                _pcCoreInt.Add(Init("Processor", "Interrupts/sec", i.ToString()));

            try
            {
                if (PerformanceCounterCategory.Exists("PhysicalDisk"))
                {
                    foreach (string inst in new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames())
                    {
                        if (inst == "_Total") continue;
                        PerformanceCounter[] arr = new PerformanceCounter[3];
                        arr[0] = new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", inst);
                        arr[1] = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", inst);
                        arr[2] = new PerformanceCounter("PhysicalDisk", "% Disk Time", inst);
                        _disk[inst] = arr;
                    }
                }
            }
            catch { }

            try
            {
                if (PerformanceCounterCategory.Exists("Network Interface"))
                {
                    foreach (string inst in new PerformanceCounterCategory("Network Interface").GetInstanceNames())
                    {
                        // v1 每秒都重建这些计数器（还有 Dispose 泄漏风险），这里只建一次
                        _net.Add(new PerformanceCounter("Network Interface", "Bytes Total/sec", inst));
                    }
                }
            }
            catch { }

            Write("performance counters initialised (" + _pcCoreInt.Count + " cores, " + _disk.Count + " disks)", LogLevel.Info);
        }

        // =====================================================================
        //  ETW 内核追踪
        // =====================================================================
        private void StartTracing()
        {
            _traceThread = new Thread(delegate ()
            {
                try
                {
                    // 清掉可能残留的同名会话，否则 EnableKernelProvider 会失败
                    try
                    {
                        using (TraceEventSession killer = new TraceEventSession("NT Kernel Logger")) killer.Stop(true);
                        Thread.Sleep(300);
                    }
                    catch { }

                    LoadDriverList();

                    TraceEventSession session = new TraceEventSession("NT Kernel Logger");
                    _session = session;

                    // 0x4 = ImageLoad, 0x20 = DPC, 0x40 = Interrupt（与上游一致）
                    session.EnableKernelProvider((KernelTraceEventParser.Keywords)0x04 | (KernelTraceEventParser.Keywords)0x20 | (KernelTraceEventParser.Keywords)0x40);

                    try
                    {
                        System.Reflection.PropertyInfo prop = session.GetType().GetProperty("StopOnDispose");
                        if (prop != null) prop.SetValue(session, true, null);
                    }
                    catch { }

                    session.Source.Kernel.ImageLoad += delegate (ImageLoadTraceData d)
                    {
                        try
                        {
                            ulong b = (ulong)d.ImageBase;
                            string n = Path.GetFileName(d.FileName);
                            lock (_modMap)
                            {
                                _modMap[b] = n;
                            }
                        }
                        catch { }
                    };

                    session.Source.Kernel.PerfInfoDPC += delegate (DPCTraceData d)
                    {
                        try { OnDpcOrIsr(d, _dpc, "DPC"); }
                        catch { }
                    };

                    session.Source.Kernel.PerfInfoISR += delegate (ISRTraceData d)
                    {
                        try { OnDpcOrIsr(d, _isr, "ISR"); }
                        catch { }
                    };

                    Write("ALPA v2 engine LIVE - kernel DPC/ISR tracing active", LogLevel.Ok);
                    WriteNativeState();
                    _traceError = null;
                    session.Source.Process();
                }
                catch (Exception ex)
                {
                    // 只记 ex.Message 是不够的（一句「路径中具有非法字符」根本定位不到原因），
                    // 把类型、内部异常和堆栈前几帧一起写进日志。
                    _traceError = Short(ex);
                    Write("kernel tracing error: " + Short(ex), LogLevel.Crit);
                    Write("kernel tracing detail: " + Verbose(ex), LogLevel.Muted);
                    WriteNativeState();
                    _session = null;
                }
            });
            _traceThread.IsBackground = true;
            _traceThread.Name = "ALPA-ETW";
            _traceThread.Start();
        }

        /// <summary>
        /// 内核追踪失败的原因（null = 正常）。UI 直接拿它挂提示条，
        /// 别再让用户对着空白的曲线猜「为什么没数据」。
        /// </summary>
        private volatile string _traceError;

        public string TraceError { get { return _traceError; } }

        /// <summary>一句话版本的异常描述（给 UI 用）。</summary>
        private static string Short(Exception ex)
        {
            string m = ex.Message;
            if (m != null && m.EndsWith("。")) m = m.Substring(0, m.Length - 1);
            Exception inner = ex.InnerException;
            if (inner != null) m += " / " + inner.Message;
            return ex.GetType().Name + ": " + m;
        }

        /// <summary>带堆栈的详细描述（给日志用）。</summary>
        private static string Verbose(Exception ex)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
            System.ComponentModel.Win32Exception w = ex as System.ComponentModel.Win32Exception;
            if (w != null) sb.Append(" [Win32=").Append(w.NativeErrorCode).Append(']');
            if (ex.InnerException != null) sb.Append(" <<inner: ").Append(ex.InnerException.Message).Append(">>");
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                string[] lines = ex.StackTrace.Split('\n');
                for (int i = 0; i < lines.Length && i < 6; i++)
                    sb.Append(" | ").Append(lines[i].Trim());
            }
            return sb.ToString();
        }

        /// <summary>
        /// 记录原生依赖（KernelTraceControl.dll 等）的解包状态。
        /// 单文件版里它必须真的落盘，否则内核会话一定起不来 —— 这是排查空数据的第一个看点。
        /// </summary>
        private void WriteNativeState()
        {
            if (Boot.StageError != null)
                Write("native payload stage FAILED: " + Boot.StageError, LogLevel.Crit);
            else if (Boot.Staged)
                Write("native payload staged: " + Boot.StageDir, LogLevel.Muted);
            else
                Write("native payload never requested (OSExtensions not loaded)", LogLevel.Warn);
        }

        private void LoadDriverList()
        {            try
            {
                uint needed;
                IntPtr[] addr = new IntPtr[2048];
                if (!EnumDeviceDrivers(addr, (uint)(addr.Length * IntPtr.Size), out needed)) return;
                int count = (int)(needed / IntPtr.Size);
                if (count > addr.Length)
                {
                    addr = new IntPtr[count];
                    if (!EnumDeviceDrivers(addr, (uint)(addr.Length * IntPtr.Size), out needed)) return;
                    count = (int)(needed / IntPtr.Size);
                }
                StringBuilder sb = new StringBuilder(260);
                int ok = 0;
                lock (_modMap)
                {
                    _modMap.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        sb.Length = 0;
                        if (GetDeviceDriverBaseName(addr[i], sb, sb.Capacity) > 0)
                        {
                            _modMap[(ulong)addr[i]] = sb.ToString();
                            ok++;
                        }
                    }
                }
                Write("driver map loaded: " + ok + " modules", LogLevel.Info);
            }
            catch (Exception ex) { Write("driver map failed: " + ex.Message, LogLevel.Warn); }
        }

        /// <summary>把所有模块基址排序一次，之后按地址二分 + 结果缓存。v1 是每事件 O(n) 线性扫。</summary>
        private ulong[] _sortedBases = new ulong[0];

        private string ResolveName(ulong addr)
        {
            long off;
            return ResolveWhere(addr, out off);
        }

        /// <summary>
        /// 把内核地址解析成「模块 + 模块内偏移」。
        ///
        /// 为什么要偏移：DPC 经常落在 ntoskrnl.exe 里，但"ntoskrnl.exe"等于没说 ——
        /// 内核里有几万个例程。加上偏移（ntoskrnl.exe+0x1a2b40）才是可复现的指纹：
        /// 同一 Windows 版本上偏移是稳定的，能拿去对符号表定位到具体例程，
        /// 也能立刻看出是不是同一个源头在反复触发。
        /// </summary>
        private string ResolveWhere(ulong addr, out long offset)
        {
            offset = 0;
            if (addr == 0) return "unknown";
            lock (_modMap)
            {
                if (_sortedBases.Length != _modMap.Count)
                {
                    _sortedBases = new ulong[_modMap.Count];
                    _modMap.Keys.CopyTo(_sortedBases, 0);
                    Array.Sort(_sortedBases);
                }
                int lo = 0, hi = _sortedBases.Length - 1, best = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (_sortedBases[mid] <= addr) { best = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                if (best >= 0)
                {
                    string nm;
                    if (_modMap.TryGetValue(_sortedBases[best], out nm))
                    {
                        offset = (long)(addr - _sortedBases[best]);
                        return nm;
                    }
                }
            }
            return "addr_" + addr.ToString("X");
        }

        /// <summary>
        /// 把一次 DPC/ISR 样本并入统计。真实事件与 --selftest 的合成数据共用这一条路径，
        /// 保证「报告里的每个口径」都能在无管理员权限时被验证。
        /// </summary>
        private void RecordSample(Dictionary<string, DriverStat> target, string driver,
            string where, string type, double us, int cpu, double atSec)
        {
            lock (_statLock)
            {
                DriverStat st;
                if (!target.TryGetValue(driver, out st))
                {
                    st = new DriverStat();
                    st.Name = driver;
                    st.Type = type;
                    target[driver] = st;
                }
                st.Cur = us;
                st.Count++;
                st.Hist.Add(us);

                // 每核心 / 分档 / 总量：报告要用，不能等导出时再回溯（直方图分桶回不出精确分档）
                bool isDpc = type == "DPC";
                CpuAgg[] arr = isDpc ? _dpcCpu : _isrCpu;
                if (cpu >= 0)
                {
                    if (cpu >= arr.Length)
                    {
                        CpuAgg[] bigger = new CpuAgg[cpu + 8];
                        if (arr != null) arr.CopyTo(bigger, 0);
                        arr = bigger;
                        if (isDpc) _dpcCpu = arr; else _isrCpu = arr;
                    }
                    CpuAgg ca = arr[cpu];
                    if (ca == null) { ca = new CpuAgg(); arr[cpu] = ca; }
                    ca.Count++;
                    ca.TotalUs += us;
                    if (us > ca.MaxUs) ca.MaxUs = us;
                }
                (isDpc ? _dpcBuckets : _isrBuckets)[BucketIndex(us)]++;
                if (isDpc) _dpcTotalUs += us; else _isrTotalUs += us;

                if (us >= (isDpc ? DpcThreshold : IsrThreshold))
                {
                    _spikeCount++;
                    SpikeRec r = new SpikeRec();
                    r.T = DateTime.Now;
                    r.Driver = driver;
                    r.Where = where;
                    r.Type = type;
                    r.Us = us;
                    long nowTs = Stopwatch.GetTimestamp();
                    long prevTs = Interlocked.Exchange(ref _lastSpikeTs, nowTs);
                    r.SincePrevMs = prevTs == 0 ? 0 : (nowTs - prevTs) * 1000.0 / Stopwatch.Frequency;
                    // atSec >= 0 表示调用方自带时间轴（自检注入用），否则取真实时钟
                    r.AtSec = atSec >= 0 ? atSec : _uptime.Elapsed.TotalSeconds;
                    _spikes.Add(r);
                    if (_spikes.Count > MaxSpikes) _spikes.RemoveRange(0, _spikes.Count - MaxSpikes);
                    Action<SpikeRec> h = Spike;
                    if (h != null) h(r);
                }
            }
        }

        private void OnDpcOrIsr(object data, Dictionary<string, DriverStat> target, string type)
        {
            double us = 0;
            bool have = false;
            ulong routine = 0;
            int cpu = -1;

            DPCTraceData d = data as DPCTraceData;
            if (d != null)
            {
                object v = d.PayloadByName("ElapsedTimeMSec");
                if (v != null) { us = Convert.ToDouble(v) * 1000.0; have = true; }
                routine = (ulong)d.Routine;
                try { cpu = d.ProcessorNumber; } catch { }
            }
            else
            {
                ISRTraceData i = data as ISRTraceData;
                if (i != null)
                {
                    object v = i.PayloadByName("ElapsedTimeMSec");
                    if (v != null) { us = Convert.ToDouble(v) * 1000.0; have = true; }
                    routine = (ulong)i.Routine;
                    try { cpu = i.ProcessorNumber; } catch { }
                }
            }
            if (!have) return;

            long off;
            string driver = ResolveWhere(routine, out off);
            string where = off > 0 ? driver + "+0x" + off.ToString("x") : driver;
            RecordSample(target, driver, where, type, us, cpu, -1);

        }

        // =====================================================================
        //  每秒采样
        // =====================================================================
        private void SampleLoop()
        {
            int tick = 0;
            while (_running)
            {
                try
                {
                    Snapshot snap = BuildSnapshot();
                    Action<Snapshot> h = Sample;
                    if (h != null) h(snap);

                    // 进程枚举很贵（每个进程都要开句柄 + 查 GPU 计数器），
                    // 每 2 秒做一次且在后台线程，UI 永远不会被它卡住。
                    if ((tick & 1) == 0) SampleProcesses();
                    tick++;
                }
                catch (Exception ex) { Write("sample error: " + ex.Message, LogLevel.Warn); }
                Thread.Sleep(1000);
            }
        }

        private double Next(PerformanceCounter c)
        {
            try { return c != null ? c.NextValue() : 0; }
            catch { return 0; }
        }

        private Snapshot BuildSnapshot()
        {
            Snapshot s = new Snapshot();
            s.T = DateTime.Now;
            s.Tracing = IsTracing;

            uint mi, ma, cu;
            if (NtQueryTimerResolution(out mi, out ma, out cu) == 0) s.TimerMs = cu / 10000.0;

            lock (_statLock)
            {
                s.SpikeCount = (int)Math.Min(int.MaxValue, _spikeCount);
                foreach (KeyValuePair<string, DriverStat> kv in _dpc)
                {
                    s.Dpc.Add(kv.Value);
                    if (kv.Value.Max > s.MaxDpc) { s.MaxDpc = kv.Value.Max; s.WorstDriver = kv.Value.Name; }
                }
                foreach (KeyValuePair<string, DriverStat> kv in _isr)
                {
                    s.Isr.Add(kv.Value);
                    if (kv.Value.Max > s.MaxIsr) s.MaxIsr = kv.Value.Max;
                }
                long allCount = 0;
                double allSum = 0;
                foreach (KeyValuePair<string, DriverStat> kv in _dpc) { allCount += kv.Value.Count; allSum += kv.Value.Hist.Sum; }
                s.DpcPerSec = allCount - _dpcCountAtLast[0];
                _dpcCountAtLast[0] = allCount;
                s.AvgDpc = s.DpcPerSec > 0 ? (allSum / Math.Max(1, allCount)) : 0;

                long isrCount = 0;
                foreach (KeyValuePair<string, DriverStat> kv in _isr) isrCount += kv.Value.Count;
                s.IsrPerSec = isrCount - _isrCountAtLast[0];
                _isrCountAtLast[0] = isrCount;
            }

            s.MouseHz = _mouseHz;
            s.ProcQueue = (float)Next(_pcProcQueue);
            s.ContextSw = (float)Next(_pcCtxSw);
            s.InterruptsTotal = (float)Next(_pcIntTotal);
            s.Parked = (float)Next(_pcParked);
            s.PageFaults = (float)Next(_pcPageFault);
            s.AvailMb = (float)Next(_pcAvail);
            s.CacheMb = (float)(Next(_pcCache) / 1024.0 / 1024.0);
            s.UdpErr = (float)Next(_pcUdpErr);

            float[] cores = new float[_pcCoreInt.Count];
            for (int i = 0; i < _pcCoreInt.Count; i++) cores[i] = (float)Next(_pcCoreInt[i]);
            s.CoreInterrupts = cores;

            double totalDiskPct = 0;
            foreach (KeyValuePair<string, PerformanceCounter[]> kv in _disk)
            {
                DiskRow dr = new DiskRow();
                dr.Name = kv.Key;
                dr.Queue = Next(kv.Value[0]);
                dr.LatencyMs = Next(kv.Value[1]) * 1000.0;
                dr.ActivePct = Next(kv.Value[2]);
                totalDiskPct += dr.ActivePct;
                s.Disks.Add(dr);
            }
            s.Disks.Sort(delegate (DiskRow a, DiskRow b) { return b.ActivePct.CompareTo(a.ActivePct); });
            s.DiskPct = totalDiskPct;

            double net = 0;
            foreach (PerformanceCounter c in _net) net += Next(c);
            s.NetMbs = net / 1024.0 / 1024.0;

            lock (_procLock) s.Procs = new List<ProcRow>(_procs);
            return s;
        }

        /// <summary>
        /// 尝试真正加载 TraceEvent，成功返回「程序集名 版本 (来源)」。
        /// Assembly.Load 失败时会走 AppDomain.AssemblyResolve —— 单文件版正是靠
        /// 那条链路从 exe 内嵌资源里解出来的，所以这个探针同时覆盖两种发布形态。
        /// </summary>
        private static string ProbeTraceLib(out string err)
        {
            err = null;
            const string name = "Microsoft.Diagnostics.Tracing.TraceEvent";
            try
            {
                System.Reflection.Assembly a = System.Reflection.Assembly.Load(name);
                if (a == null) { err = "resolve returned null"; return null; }
                string where = "embedded";
                try { if (!string.IsNullOrEmpty(a.Location)) where = "file"; }
                catch { }
                return name + " " + a.GetName().Version.ToString() + " (" + where + ")";
            }
            catch (Exception ex)
            {
                err = ex.GetType().Name;
                return null;
            }
        }

        private void SampleProcesses()
        {
            List<ProcRow> outp = new List<ProcRow>();
            string[] gpuInst = new string[0];
            try
            {
                if (PerformanceCounterCategory.Exists("GPU Process Memory"))
                    gpuInst = new PerformanceCounterCategory("GPU Process Memory").GetInstanceNames();
            }
            catch { }

            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return; }

            foreach (Process p in procs)
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4) continue;
                    ProcRow r = new ProcRow();
                    r.Pid = p.Id;
                    r.Name = p.ProcessName;
                    try { r.Threads = p.Threads.Count; } catch { r.Threads = 0; }
                    try
                    {
                        if (p.PriorityClass == ProcessPriorityClass.High) { r.Prio = "High"; r.PrioVal = 5; }
                        else if (p.PriorityClass == ProcessPriorityClass.RealTime) { r.Prio = "Real"; r.PrioVal = 6; }
                        else if (p.PriorityClass == ProcessPriorityClass.Idle) { r.Prio = "Low"; r.PrioVal = 1; }
                    }
                    catch { }
                    try { r.Cpu = p.TotalProcessorTime; } catch { }
                    try { r.RamMb = p.WorkingSet64 / 1024.0 / 1024.0; } catch { }

                    if (gpuInst.Length > 0)
                    {
                        string prefix = "pid_" + p.Id + "_";
                        for (int i = 0; i < gpuInst.Length; i++)
                        {
                            if (gpuInst[i].StartsWith(prefix, StringComparison.Ordinal))
                            {
                                r.VramMb = ReadGpuMb(gpuInst[i]);
                                break;
                            }
                        }
                    }

                    try
                    {
                        IO_COUNTERS io;
                        if (GetProcessIoCounters(p.Handle, out io))
                            r.IoBytes = io.ReadTransferCount + io.WriteTransferCount;
                    }
                    catch { }

                    // Score 沿用上游口径，只是把 IO 归一到 MB 再加权
                    r.Score = r.Threads + (r.RamMb / 50.0) + r.VramMb + (r.IoBytes / 1024.0 / 1024.0);
                    outp.Add(r);
                }
                catch { }
            }

            lock (_procLock) _procs = outp;
        }

        /// <summary>
        /// 读一个进程的显存占用（MB）。
        /// 优先用 Local Usage：本机实测 Dedicated Usage 会返回离谱值
        /// （dandanplay 报 4,616,381,632,512 字节 ≈ 4.2 TB，而 Local Usage 报 1.07 GB），
        /// 这是 WDDM 计数器自身的怪癖。再做一次量级兜底，异常值不计入 Score，
        /// 免得一个假数据把整张进程表按「显存」排序时全带歪。
        /// </summary>
        private static double ReadGpuMb(string inst)
        {
            double mb = CounterMb("GPU Process Memory", "Local Usage", inst);
            if (mb <= 0) mb = CounterMb("GPU Process Memory", "Dedicated Usage", inst);
            if (mb > 256 * 1024) mb = 0;   // > 256 GB 视为计数器异常
            return mb;
        }

        private static double CounterMb(string cat, string counter, string inst)
        {
            try
            {
                using (PerformanceCounter pc = new PerformanceCounter(cat, counter, inst, true))
                {
                    float v = pc.NextValue();
                    return v > 0 ? v / 1024.0 / 1024.0 : 0;
                }
            }
            catch { return 0; }
        }

        // =====================================================================
        //  鼠标回报率（Raw Input）
        // =====================================================================
        public void AttachRawInput(IntPtr hwnd)
        {
            try
            {
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = 0x01;
                rid[0].usUsage = 0x02;
                // RIDEV_INPUTSINK = 0x100：窗口不在前台也能收到鼠标移动
                rid[0].dwFlags = 0x00000100;
                rid[0].hwndTarget = hwnd;
                if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                    Write("raw input registration failed", LogLevel.Warn);
            }
            catch (Exception ex) { Write("raw input error: " + ex.Message, LogLevel.Warn); }
        }

        public void OnRawInput()
        {
            long cur = DateTime.Now.Ticks;
            if (_lastInputTicks != 0)
            {
                double diff = cur - _lastInputTicks;
                if (diff > 0)
                {
                    lock (_inputGaps) _inputGaps.Add(diff);
                }
            }
            _lastInputTicks = cur;
        }

        /// <summary>由 UI 每秒调用一次，结算上一秒的鼠标轮询率。</summary>
        public void FlushMouseRate()
        {
            lock (_inputGaps)
            {
                if (_inputGaps.Count == 0) return;
                double sum = 0;
                for (int i = 0; i < _inputGaps.Count; i++) sum += _inputGaps[i];
                double avgTicks = sum / _inputGaps.Count;   // DateTime.Ticks = 100 ns
                _mouseHz = avgTicks > 0 ? 10000000.0 / avgTicks : 0;
                _inputGaps.Clear();
            }
        }

        // =====================================================================
        //  静态系统体检
        // =====================================================================
        private void Add(List<CheckItem> list, string group, string title, string value, Sev sev, string hint)
        {
            CheckItem c = new CheckItem();
            c.Group = group; c.Title = title; c.Value = value; c.Sev = sev; c.Hint = hint;
            list.Add(c);
        }

        private void BuildChecks()
        {
            List<CheckItem> list = new List<CheckItem>();

            Add(list, "内核与调度", "内核追踪 (ETW)", IsAdmin ? "已启用" : "未启用",
                IsAdmin ? Sev.Ok : Sev.Warn,
                IsAdmin ? "DPC / ISR 实时统计正常工作" : "以管理员身份重启即可开启内核 DPC 追踪");

            // 定时器精度
            uint mi, ma, cu;
            if (NtQueryTimerResolution(out mi, out ma, out cu) == 0)
            {
                double ms = cu / 10000.0;
                Add(list, "内核与调度", "系统定时器精度", ms.ToString("0.0000") + " ms",
                    ms <= 1.0 ? Sev.Ok : (ms <= 2.0 ? Sev.Info : Sev.Warn),
                    "当前进程请求的定时器分辨率；0.5 ms 表示有程序把定时器提频了");
            }

            // HAGS
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    object v = k != null ? k.GetValue("HwSchMode") : null;
                    bool on = v != null && Convert.ToInt32(v) == 2;
                    Add(list, "图形与显示", "GPU 硬件加速调度 (HAGS)", on ? "已开启" : "已关闭",
                        on ? Sev.Ok : Sev.Info,
                        "HAGS 关闭时 DWM 的呈现走软件路径，部分场景下会放大 DPC 抖动");
                }
            }
            catch { }

            // MPO
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm"))
                {
                    object v = k != null ? k.GetValue("OverlayTestMode") : null;
                    bool disabled = v != null && Convert.ToInt32(v) == 5;
                    Add(list, "图形与显示", "多平面叠加 (MPO)", disabled ? "已关闭" : "已开启",
                        disabled ? Sev.Ok : Sev.Info,
                        "MPO 开启时部分驱动会额外产生 DWM 相关 DPC；出现闪烁/花屏才建议关闭");
                }
            }
            catch { }

            // HPET
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel"))
                {
                    object v = k != null ? k.GetValue("UsePlatformClock") : null;
                    bool forced = v != null && Convert.ToInt32(v) == 1;
                    Add(list, "内核与调度", "强制 HPET (useplatformclock)", forced ? "已强制" : "未强制",
                        forced ? Sev.Crit : Sev.Ok,
                        forced ? "强烈建议删除该键：强制 HPET 会显著拉高整体 DPC 延迟" : "系统自动选择时钟源，状态正常");
                }
            }
            catch { }

            // TSC
            try
            {
                string cpu = "";
                ManagementObjectSearcher se = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementBaseObject o in se.Get()) { cpu = Convert.ToString(o["Name"]); break; }
                Add(list, "内核与调度", "CPU 计时器 (Invariant TSC)",
                    string.IsNullOrEmpty(cpu) ? "未知" : "支持",
                    string.IsNullOrEmpty(cpu) ? Sev.Neutral : Sev.Ok,
                    string.IsNullOrEmpty(cpu) ? "未能读取 CPU 型号" : cpu);
            }
            catch { }

            // 电源计划
            try
            {
                IntPtr g;
                if (PowerGetActiveScheme(IntPtr.Zero, out g) == 0)
                {
                    Guid guid = (Guid)Marshal.PtrToStructure(g, typeof(Guid));
                    // 直接读方案的友好名：只认内置 GUID 的话，用户/工具复制出来的
                    // 「卓越性能」副本（GUID 不同）会被误报成「其他/自定义」并给出错误建议
                    string name = FriendlyPowerPlan(guid);
                    string low = name.ToLowerInvariant();
                    Sev sv;
                    string hint;
                    if (low.Contains("卓越") || low.Contains("ultimate") || low.Contains("高性能")
                        || low.Contains("high performance") || name.IndexOf("高性能") >= 0)
                    {
                        sv = Sev.Ok; hint = "适合做延迟测试";
                    }
                    else if (low.Contains("节能") || low.Contains("power saver") || low.Contains("省电"))
                    {
                        sv = Sev.Warn; hint = "节能方案会主动降频/降中断响应，测延迟与游戏前建议切到「高性能」或「卓越性能」";
                    }
                    else if (low.Contains("平衡") || low.Contains("balanced"))
                    {
                        sv = Sev.Info; hint = "平衡方案在部分平台会激进降频；测延迟时建议切到「高性能」或「卓越性能」";
                    }
                    else
                    {
                        sv = Sev.Info; hint = "自定义电源方案，无法自动判定；请确认它是基于「高性能」还是「平衡」";
                    }
                    Add(list, "电源与性能", "当前电源计划", name, sv,
                        hint + "（方案 GUID " + guid.ToString() + "）");
                }
            }
            catch { }

            // 自身电源节流
            try
            {
                PROCESS_POWER_THROTTLING_STATE st = new PROCESS_POWER_THROTTLING_STATE();
                st.Version = 1;
                int ret;
                int status = NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 61, ref st, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)), out ret);
                if (status == 0)
                {
                    bool on = (st.StateMask & 1) != 0;
                    Add(list, "电源与性能", "本进程被电源节流",
                        on ? "是" : "否", on ? Sev.Warn : Sev.Ok,
                        on ? "系统正在限制本进程的调度，采样结果会偏低" : "本进程未被节流，采样可信");
                }
            }
            catch { }

            // 页面文件
            try
            {
                bool found = false;
                ManagementObjectSearcher se = new ManagementObjectSearcher("SELECT Name, CurrentUsage, AllocatedBaseSize FROM Win32_PageFileUsage");
                foreach (ManagementBaseObject o in se.Get())
                {
                    found = true;
                    string nm = Convert.ToString(o["Name"]);
                    uint size = Convert.ToUInt32(o["AllocatedBaseSize"]);
                    uint use = Convert.ToUInt32(o["CurrentUsage"]);
                    double pct = size > 0 ? use * 100.0 / size : 0;
                    // 数值串要短：右对齐列在窄卡片里放不下就会变成 "13623 / 33648 Mi…"
                    Add(list, "内存与存储", "页面文件 " + nm, Fmt.Mb(use) + " / " + Fmt.Mb(size),
                        pct > 80 ? Sev.Warn : Sev.Ok,
                        pct > 80 ? "页面文件使用率偏高，容易触发换页造成的卡顿" : "使用率正常");
                }
                if (!found)
                    Add(list, "内存与存储", "页面文件", "系统托管或已禁用", Sev.Info,
                        "未检测到固定大小的页面文件；完全禁用会影响大内存占用程序的稳定性");
            }
            catch { }

            // 内存
            try
            {
                ulong total = 0, avail = 0;
                ManagementObjectSearcher se = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementBaseObject o in se.Get()) { total = Convert.ToUInt64(o["TotalPhysicalMemory"]); break; }
                ManagementObjectSearcher se2 = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementBaseObject o in se2.Get()) { avail = Convert.ToUInt64(o["FreePhysicalMemory"]) * 1024; break; }
                if (total > 0)
                {
                    double freePct = avail * 100.0 / total;
                    Add(list, "内存与存储", "物理内存", Fmt.Bytes(avail) + " / " + Fmt.Bytes(total) + " 可用",
                        freePct < 10 ? Sev.Warn : (freePct < 20 ? Sev.Info : Sev.Ok),
                        freePct < 10 ? "可用内存过低，换页会直接表现为掉帧" : "余量充足");
                }
            }
            catch { }

            // 多套安全软件（用户机器上的常见卡顿成因）
            try
            {
                List<string> av = new List<string>();
                string[] names = { "MsMpEng", "HipsDaemon", "HipsTray", "QQPCTray", "QQPCRTP", "SmartEngine", "LenovoPcManagerService", "360tray", "ZhuDongFangYu" };
                foreach (Process p in Process.GetProcesses())
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (string.Equals(p.ProcessName, names[i], StringComparison.OrdinalIgnoreCase) && !av.Contains(names[i]))
                            av.Add(names[i]);
                    }
                }
                if (av.Count > 0)
                    Add(list, "驻留软件", "实时防护进程", av.Count + " 个",
                        av.Count >= 2 ? Sev.Warn : Sev.Info,
                        string.Join(" / ", av.ToArray()) + (av.Count >= 2 ? " —— 多套防护同时挂钩文件与网络过滤会叠加 DPC" : ""));
            }
            catch { }

            // 进程数 / 线程总数
            try
            {
                int pc = Process.GetProcesses().Length;
                Add(list, "驻留软件", "进程数量", pc + " 个",
                    pc > 260 ? Sev.Warn : Sev.Ok,
                    pc > 260 ? "后台进程偏多，建议用「进程」页按 Score 排序清理" : "数量正常");
            }
            catch { }

            lock (_statLock) { _checks.Clear(); _checks.AddRange(list); }
            Write("system checks complete (" + list.Count + " items)", LogLevel.Info);
        }

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid,
            IntPtr subGroupGuid, IntPtr powerSettingGuid, byte[] buffer, ref uint bufferSize);

        /// <summary>读电源方案的友好名（失败时退回内置 GUID 映射）。</summary>
        private static string FriendlyPowerPlan(Guid guid)
        {
            try
            {
                uint size = 512;
                byte[] buf = new byte[size];
                if (PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0)
                {
                    string s = Encoding.Unicode.GetString(buf, 0, (int)Math.Max(0, Math.Min(size, (uint)buf.Length) - 2)).Trim();
                    if (s.Length > 0) return s;
                }
            }
            catch { }
            if (guid == new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")) return "卓越性能";
            if (guid == new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")) return "高性能";
            if (guid == new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")) return "平衡";
            if (guid == new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")) return "节能";
            return "其他/自定义";
        }

        public List<CheckItem> Checks
        {
            get { lock (_statLock) return new List<CheckItem>(_checks); }
        }

        // =====================================================================
        //  启动项审计
        // =====================================================================
        private void ScanStartup()
        {
            List<StartupItem> list = new List<StartupItem>();

            try
            {
                string[] folders = {
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                };
                foreach (string f in folders)
                {
                    if (!Directory.Exists(f)) continue;
                    foreach (string file in Directory.GetFiles(f))
                    {
                        StartupItem it = new StartupItem();
                        it.Category = "启动文件夹";
                        it.Name = Path.GetFileName(file);
                        it.Detail = f;
                        it.Sev = Sev.Info;
                        list.Add(it);
                    }
                }
            }
            catch { }

            string[] keys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            foreach (string keyPath in keys)
            {
                for (int h = 0; h < 2; h++)
                {
                    try
                    {
                        RegistryKey root = h == 0 ? Registry.LocalMachine : Registry.CurrentUser;
                        using (RegistryKey k = root.OpenSubKey(keyPath))
                        {
                            if (k == null) continue;
                            foreach (string v in k.GetValueNames())
                            {
                                StartupItem it = new StartupItem();
                                it.Category = "注册表 Run";
                                it.Name = v;
                                it.Detail = (h == 0 ? "HKLM\\" : "HKCU\\") + keyPath + "  →  " + Convert.ToString(k.GetValue(v));
                                it.Sev = Sev.Info;
                                list.Add(it);
                            }
                        }
                    }
                    catch { }
                }
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks", "/query /fo CSV /nh");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process proc = Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                foreach (string line in output.Split('\n'))
                {
                    if (!line.Contains("Ready") && !line.Contains("Running")) continue;
                    if (line.Contains("\\Microsoft\\Windows\\")) continue;
                    if (line.Contains("ALPA")) continue;
                    string taskName = line.Split(',')[0].Trim('"');
                    StartupItem it = new StartupItem();
                    it.Category = "计划任务";
                    it.Name = taskName;
                    it.Detail = "schtasks —— 恶意软件常用藏身处，建议核对来源";
                    it.Sev = Sev.Warn;
                    list.Add(it);
                }
            }
            catch { }

            try
            {
                foreach (ServiceController s in ServiceController.GetServices())
                {
                    if (s.Status != ServiceControllerStatus.Running) continue;
                    // 只列第三方：微软自带服务名通常能以系统路径定位，这里用简单前缀过滤
                    string n = s.ServiceName;
                    if (n.StartsWith("Rpc") || n.StartsWith("Dcom") || n.StartsWith("Win") || n.StartsWith("Bth") ||
                        n.StartsWith("Audio") || n.StartsWith("App") || n.StartsWith("Dnscache") || n.StartsWith("Dhcp")) continue;
                    StartupItem it = new StartupItem();
                    it.Category = "服务";
                    it.Name = s.DisplayName;
                    it.Detail = s.ServiceName;
                    it.Sev = Sev.Neutral;
                    list.Add(it);
                }
            }
            catch { }

            lock (_statLock) { _startup.Clear(); _startup.AddRange(list); }
            Write("startup audit complete (" + list.Count + " items)", LogLevel.Info);
        }

        public List<StartupItem> Startup
        {
            get { lock (_statLock) return new List<StartupItem>(_startup); }
        }

        public void RescanStartup() { ScanStartup(); }
        public void RescanChecks() { BuildChecks(); }

        // =====================================================================
        //  导出
        // =====================================================================
        public string ExportDriversCsv(string dir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(CsvLine("Driver", "Type", "Count", "Current(us)", "Avg(us)", "P50(us)", "P95(us)", "P99(us)", "Max(us)"));
            lock (_statLock)
            {
                Append(sb, _dpc);
                Append(sb, _isr);
            }
            string f = Path.Combine(dir, "ALPA_v2_Drivers_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
            return f;
        }

        private static void Append(StringBuilder sb, Dictionary<string, DriverStat> d)
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
        /// 标准 CSV 字段转义：含逗号/引号/换行的字段用引号包起来，内部引号翻倍。
        ///
        /// 这里必须较真：分隔符选错，用户打开就是「整行挤在一列里」。
        /// 之前用分号做分隔，而中文版 Excel 的列表分隔符是逗号，整份文件就只显示一列 ——
        /// 所以统一改成逗号，并按 RFC 4180 转义（本工具的字段值不含逗号，理论上不会触发）。
        /// </summary>
        private static string CsvLine(params object[] fields)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string v = fields[i] == null ? "" : fields[i].ToString();
                if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
                    v = '"' + v.Replace("\"", "\"\"") + '"';
                sb.Append(v);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 导出尖峰明细。列设计刻意做了两件防呆：
        ///  · 除了可读时间，再给 T+(秒) 与 间隔(ms) 两个**纯数字**列 ——
        ///    用 Excel / WPS 直接打开时，时间列会被按本地日期格式重排（秒会丢掉），
        ///    数字列则不受影响，所以做间隔分析要看这两列；
        ///  · 文件头直接写入周期性分析的结论，拿到这个 CSV 就能看到"是不是固定间隔"。
        /// </summary>
        /// <summary>
        /// 导出尖峰明细（逗号分隔的标准 CSV，Excel 双击即可分列）。
        ///
        /// 设计要点：
        ///  · 除了可读时间，再给 T+(s) 与 SincePrev(ms) 两个纯数字列 ——
        ///    Excel/WPS 会把时间列按本地日期格式重排（秒会被吃掉），数字列不会受影响；
        ///  · 周期判定 / 间隔统计这类「结论」不塞进 CSV，统一放进报告文件（ExportReport），
        ///    保持 CSV 是纯数据，谁打开都不会多出几行对不上的东西。
        /// </summary>
        public string ExportSpikesCsv(string dir)
        {
            List<SpikeRec> snap;
            lock (_statLock) snap = new List<SpikeRec>(_spikes);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(CsvLine("Seq", "Time", "T+(s)", "SincePrev(ms)", "Driver", "Location", "Type", "Duration(us)"));

            int i = 0;
            foreach (SpikeRec r in snap)
            {
                i++;
                double prev = r.SincePrevMs;
                if (prev <= 0 && i > 1) prev = (r.AtSec - snap[i - 2].AtSec) * 1000.0;
                sb.AppendLine(CsvLine(i.ToString(),
                    r.T.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    r.AtSec.ToString("0.###"), prev.ToString("0.##"),
                    r.Driver, r.Where, r.Type, r.Us.ToString("F2")));
            }

            string f = Path.Combine(dir, "ALPA_v2_Spikes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
            return f;
        }

        /// <summary>
        /// 导出 LatencyMon 式的分析报告（纯文本）：结论、系统信息、DPC/ISR 统计、
        /// 分档计数、每核心数据、尖峰周期性、驱动排行与健康检查建议，一次拿全。
        /// </summary>
        public string ExportReport(string dir)
        {
            List<DriverStat> dpc, isr;
            List<SpikeRec> spikes;
            List<CheckItem> checks;
            double uptimeSec;
            lock (_statLock)
            {
                dpc = new List<DriverStat>(_dpc.Values);
                isr = new List<DriverStat>(_isr.Values);
                spikes = new List<SpikeRec>(_spikes);
                checks = new List<CheckItem>(_checks);
                uptimeSec = _uptime.Elapsed.TotalSeconds;
            }
            dpc.Sort(delegate (DriverStat a, DriverStat b) { return b.Max.CompareTo(a.Max); });
            isr.Sort(delegate (DriverStat a, DriverStat b) { return b.Max.CompareTo(a.Max); });

            uint tmi, tma, tcur;
            double timerMs = NtQueryTimerResolution(out tmi, out tma, out tcur) == 0 ? tcur / 10000.0 : 0;
            double mouseHz = _mouseHz;

            StringBuilder sb = new StringBuilder();
            Ruler(sb, '=');
            sb.AppendLine("ALPA v2 内核延迟分析报告 / Kernel latency report");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (uptimeSec > 0) sb.AppendLine("采样时长: " + uptimeSec.ToString("0") + " 秒 (" + TimeSpan.FromSeconds(uptimeSec).ToString().Substring(0, 8) + ")");
            sb.AppendLine("判定阈值: DPC >= " + DpcThreshold + " us ; ISR >= " + IsrThreshold + " us");
            Ruler(sb, '=');
            sb.AppendLine();

            // ---------- 一、结论 ----------
            Ruler(sb, '_');
            sb.AppendLine("一、结论  CONCLUSION");
            Ruler(sb, '_');
            List<string> con = BuildConclusions(dpc, isr, spikes, timerMs, checks);
            foreach (string c in con) sb.AppendLine("  * " + c);
            sb.AppendLine();
            sb.AppendLine("说明: 本报告由 ALPA v2 依据 ETW 内核追踪(PerfInfo DPC/ISR)与性能计数器生成;");
            sb.AppendLine("      每进程硬缺页与「中断到用户进程延迟」这两项未采集（需要额外的内核栈遍历）。");
            sb.AppendLine();

            // ---------- 二、系统信息 ----------
            Ruler(sb, '_');
            sb.AppendLine("二、系统信息  SYSTEM");
            Ruler(sb, '_');
            foreach (string[] kv in SystemInfoRows())
                sb.AppendLine(Pad(kv[0], 26) + kv[1]);
            sb.AppendLine("定时器精度: " + timerMs.ToString("0.####") + " ms");
            sb.AppendLine("鼠标回报率: " + (mouseHz > 1 ? mouseHz.ToString("0") + " Hz" : "未检测到移动"));
            sb.AppendLine();

            // ---------- 三、DPC ----------
            Ruler(sb, '_');
            sb.AppendLine("三、DPC 统计  REPORTED DPCs");
            Ruler(sb, '_');
            sb.AppendLine("DPC 例程运行时会占住当前 CPU，期间用户线程无法被调度 —— 这是掉帧与卡顿的直接来源之一。");
            WriteAgg(sb, "DPC", dpc, _dpcBuckets, _dpcTotalUs, uptimeSec);
            sb.AppendLine();

            // ---------- 四、ISR ----------
            Ruler(sb, '_');
            sb.AppendLine("四、ISR 统计  REPORTED ISRs");
            Ruler(sb, '_');
            WriteAgg(sb, "ISR", isr, _isrBuckets, _isrTotalUs, uptimeSec);
            sb.AppendLine();

            // ---------- 五、尖峰周期性 ----------
            Ruler(sb, '_');
            sb.AppendLine("五、尖峰周期性  PERIODICITY");
            Ruler(sb, '_');
            PeriodResult pr = AnalyzePeriods(spikes);
            sb.AppendLine("尖峰总数: " + spikes.Count.ToString("#,0"));
            if (spikes.Count > 1)
            {
                double span = spikes[spikes.Count - 1].AtSec - spikes[0].AtSec;
                sb.AppendLine("缓冲覆盖: " + FmtMs(span * 1000)
                    + (spikes.Count >= MaxSpikes ? "（已达 " + MaxSpikes + " 条上限，更早的尖峰已被丢弃）" : "（全部尖峰）"));
            }
            sb.AppendLine("周期判定: " + (pr == null ? "样本不足或未发现固定间隔" : pr.Describe()));
            double med, mn, mx;
            IntervalStatsOf(spikes, out med, out mn, out mx);
            if (med > 0)
                sb.AppendLine("相邻间隔: 中位 " + FmtMs(med) + " ; 最小 " + FmtMs(mn) + " ; 最大 " + FmtMs(mx)
                    + "（中位远小于最大值 = 成阵出现：阵内密、阵间疏）");

            List<double> burstAt = BurstStarts(spikes, med);
            if (burstAt.Count > 0)
            {
                double thSec = Math.Max(30.0, med / 1000.0 * 20.0);
                sb.AppendLine("成阵情况: 共 " + burstAt.Count + " 个阵（相邻间隔 > " + FmtMs(thSec * 1000) + " 视为分阵）");
                if (burstAt.Count >= 2)
                {
                    List<double> gaps = new List<double>();
                    for (int i = 1; i < burstAt.Count; i++) gaps.Add((burstAt[i] - burstAt[i - 1]) * 1000.0);
                    gaps.Sort();
                    sb.AppendLine("阵间间隔: 中位 " + FmtMs(gaps[gaps.Count / 2]) + " ; 最小 " + FmtMs(gaps[0])
                        + " ; 最大 " + FmtMs(gaps[gaps.Count - 1]) + "   <- 「多久来一批」看这一行");
                }
            }
            sb.AppendLine();

            // ---------- 六、每核心数据 ----------
            Ruler(sb, '_');
            sb.AppendLine("六、每核心数据  PER CPU");
            Ruler(sb, '_');
            int cores = Math.Max(_dpcCpu.Length, _isrCpu.Length);
            for (int c = 0; c < cores; c++)
            {
                CpuAgg dc = c < _dpcCpu.Length ? _dpcCpu[c] : null;
                CpuAgg ic = c < _isrCpu.Length ? _isrCpu[c] : null;
                if ((dc == null || dc.Count == 0) && (ic == null || ic.Count == 0)) continue;
                sb.AppendLine("CPU " + c + ":");
                if (dc != null && dc.Count > 0)
                    sb.AppendLine("    DPC 次数 " + dc.Count.ToString("#,0")
                        + " ; 最高 " + dc.MaxUs.ToString("0.#") + " us"
                        + " ; 总计 " + (dc.TotalUs / 1e6).ToString("0.###") + " 秒");
                if (ic != null && ic.Count > 0)
                    sb.AppendLine("    ISR 次数 " + ic.Count.ToString("#,0")
                        + " ; 最高 " + ic.MaxUs.ToString("0.#") + " us"
                        + " ; 总计 " + (ic.TotalUs / 1e6).ToString("0.###") + " 秒");
            }
            sb.AppendLine();

            // ---------- 七、驱动排行 ----------
            Ruler(sb, '_');
            sb.AppendLine("七、驱动排行（按最高值，前 15）  TOP DRIVERS");
            Ruler(sb, '_');
            sb.AppendLine(Pad("驱动", 26) + Pad("类型", 7) + Pad("次数", 12) + Pad("P50", 10) + Pad("P95", 10) + Pad("P99", 10) + "最高(us)");
            int shown = 0;
            foreach (DriverStat d in dpc)
            {
                if (shown++ >= 15) break;
                sb.AppendLine(Pad(d.Name, 26) + Pad(d.Type, 7) + Pad(d.Count.ToString("#,0"), 12)
                    + Pad(d.P50.ToString("0.#"), 10) + Pad(d.P95.ToString("0.#"), 10)
                    + Pad(d.P99.ToString("0.#"), 10) + d.Max.ToString("0.#"));
            }
            foreach (DriverStat d in isr)
            {
                if (shown++ >= 15) break;
                sb.AppendLine(Pad(d.Name, 26) + Pad(d.Type, 7) + Pad(d.Count.ToString("#,0"), 12)
                    + Pad(d.P50.ToString("0.#"), 10) + Pad(d.P95.ToString("0.#"), 10)
                    + Pad(d.P99.ToString("0.#"), 10) + d.Max.ToString("0.#"));
            }
            sb.AppendLine();

            // ---------- 八、健康检查 ----------
            Ruler(sb, '_');
            sb.AppendLine("八、健康检查中的非绿项  CHECKS");
            Ruler(sb, '_');
            int bad = 0;
            foreach (CheckItem c in checks)
            {
                if (c.Sev == Sev.Ok) continue;
                sb.AppendLine("  [" + c.Sev.ToString().ToUpper() + "] " + c.Title + " = " + c.Value
                    + (string.IsNullOrEmpty(c.Hint) ? "" : "   —— " + c.Hint));
                bad++;
            }
            if (bad == 0) sb.AppendLine("  （全部正常）");
            sb.AppendLine();

            string f = Path.Combine(dir, "ALPA_v2_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
            Write("report exported: " + f, LogLevel.Ok);
            return f;
        }

        /// <summary>
        /// 把尖峰按「阵」聚类，返回每个阵的起始时刻（秒）。
        ///
        /// 阈值取自相邻间隔中位的 20 倍、且不小于 30 秒 —— 这一步很关键：
        /// 相位折叠给出的细周期是「阵内间隔」（实测 1.5 秒），如果拿它当分阵阈值，
        /// 一个阵会被切成几十个假阵；用自适应的大阈值才能得到真正的「多久来一批」。
        /// </summary>
        private static List<double> BurstStarts(List<SpikeRec> spikes, double medianGapMs)
        {
            List<double> starts = new List<double>();
            if (spikes == null || spikes.Count < 4) return starts;
            double thSec = Math.Max(30.0, medianGapMs / 1000.0 * 20.0);
            for (int i = 0; i < spikes.Count; i++)
            {
                double prev = i == 0 ? double.MaxValue : (spikes[i].AtSec - spikes[i - 1].AtSec);
                if (prev > thSec) starts.Add(spikes[i].AtSec);
            }
            return starts;
        }

        private static void Ruler(StringBuilder sb, char ch)
        {
            sb.AppendLine(new string(ch, 78));
        }

        private static string Pad(string s, int w)
        {
            if (s == null) s = "";
            int width = 0;
            foreach (char ch in s) width += ch > 0x2E80 ? 2 : 1;   // CJK 按两列宽算，尽量对齐
            if (width >= w) return s;
            return s + new string(' ', w - width);
        }

        private static string FmtMs(double ms)
        {
            if (ms >= 1000) return (ms / 1000.0).ToString("0.##") + " 秒";
            return ms.ToString("0.#") + " 毫秒";
        }

        private static void WriteAgg(StringBuilder sb, string kind, List<DriverStat> list, long[] buckets, double totalUs, double uptimeSec)
        {
            if (list.Count == 0)
            {
                sb.AppendLine("  （没有 " + kind + " 数据 —— 需要以管理员权限运行才会采集）");
                return;
            }
            double maxUs = 0; string maxDrv = "";
            double p99Us = 0; string p99Drv = "";
            long total = 0;
            foreach (DriverStat d in list)
            {
                total += d.Count;
                if (d.Max > maxUs) { maxUs = d.Max; maxDrv = d.Name; }
                if (d.P99 > p99Us) { p99Us = d.P99; p99Drv = d.Name; }
            }
            sb.AppendLine("最高 " + kind + " 例程执行时间 (us): " + maxUs.ToString("0.#") + "   驱动: " + maxDrv);
            sb.AppendLine("P99 最高的驱动 (us): " + p99Us.ToString("0.#") + "   驱动: " + p99Drv);
            if (uptimeSec > 0.5)
            {
                double pct = (totalUs / 1e6) / (uptimeSec * Environment.ProcessorCount) * 100.0;
                sb.AppendLine(kind + " 总时间占比: " + pct.ToString("0.####") + " %"
                    + "   (总执行 " + (totalUs / 1e6).ToString("0.###") + " 秒 / "
                    + uptimeSec.ToString("0") + " 秒 x " + Environment.ProcessorCount + " 核)");
            }
            sb.AppendLine(kind + " 总次数: " + total.ToString("#,0"));
            sb.AppendLine("分档计数:");
            for (int i = 0; i < buckets.Length; i++)
                sb.AppendLine("    执行时间 " + BucketNames[i].PadRight(10) + " us : " + buckets[i].ToString("#,0"));

            list.Sort(delegate (DriverStat a, DriverStat b) { return b.P99.CompareTo(a.P99); });
            sb.AppendLine("按 P99 排序的前几名:");
            for (int i = 0; i < list.Count && i < 8; i++)
                sb.AppendLine("    " + Pad(list[i].Name, 24) + "P50 " + Pad(list[i].P50.ToString("0.#"), 9)
                    + "P95 " + Pad(list[i].P95.ToString("0.#"), 9) + "P99 " + Pad(list[i].P99.ToString("0.#"), 9)
                    + "Max " + list[i].Max.ToString("0.#") + " us");
            list.Sort(delegate (DriverStat a, DriverStat b) { return b.Max.CompareTo(a.Max); });
        }

        /// <summary>依据当前数据自动生成中文结论，一条一条列，全是可核对的事实。</summary>
        private List<string> BuildConclusions(List<DriverStat> dpc, List<DriverStat> isr,
            List<SpikeRec> spikes, double timerMs, List<CheckItem> checks)
        {
            List<string> c = new List<string>();
            if (dpc.Count == 0 && isr.Count == 0)
            {
                c.Add("没有采集到 DPC / ISR 数据 —— 内核追踪需要管理员权限，请提权后重新运行。");
                return c;
            }

            double maxDpc = 0; string maxDpcDrv = "";
            foreach (DriverStat d in dpc) if (d.Max > maxDpc) { maxDpc = d.Max; maxDpcDrv = d.Name; }
            if (maxDpc > 0)
            {
                if (maxDpc >= 1000)
                    c.Add("存在执行时间明显过长的 DPC 例程：最高 " + maxDpc.ToString("0.#") + " µs（" + maxDpcDrv
                        + "）。超过 1000 µs 的 DPC 足以让一帧画面迟到。");
                else if (maxDpc >= 500)
                    c.Add("DPC 峰值偏高：最高 " + maxDpc.ToString("0.#") + " µs（" + maxDpcDrv
                        + "）。高于 500 µs 属于需要关注，但尚未达到明显掉帧的水平。");
                else
                    c.Add("DPC 峰值 " + maxDpc.ToString("0.#") + " µs（" + maxDpcDrv + "），在健康范围内。");
            }

            string[] netDrivers = { "ndis", "tcpip", "netio", "wlan", "nwifi", "ndu", "vwififlt" };
            foreach (DriverStat d in dpc)
            {
                bool hit = false;
                foreach (string n in netDrivers) if (d.Name.ToLower().StartsWith(n)) { hit = true; break; }
                if (hit && d.P99 >= 300)
                {
                    c.Add("至少一个问题与网络相关（" + d.Name + "，P99 " + d.P99.ToString("0.#")
                        + " µs）。若使用无线网卡，可尝试禁用它对比；有线则检查网卡驱动与「中断调节」设置。");
                    break;
                }
            }

            string[] gpuDrivers = { "dxgkrnl", "dxgmms", "nvlddmkm", "atikmdag", "amdkmdag", "igdkmd64", "nv4" };
            foreach (DriverStat d in dpc)
            {
                bool hit = false;
                foreach (string n in gpuDrivers) if (d.Name.ToLower().StartsWith(n)) { hit = true; break; }
                if (hit && d.P99 >= 300)
                {
                    c.Add("显卡驱动（" + d.Name + "）的 DPC 参与度较高（P99 " + d.P99.ToString("0.#")
                        + " µs）。全屏游戏、硬件加速播放与多显示器场景会放大它。");
                    break;
                }
            }

            PeriodResult pr = AnalyzePeriods(spikes);

            // 尖峰成阵出现时，用户真正想知道的是「多久来一批」——单独算并写进结论
            double gmed, gmin, gmax;
            IntervalStatsOf(spikes, out gmed, out gmin, out gmax);
            List<double> bursts = BurstStarts(spikes, gmed);
            if (bursts.Count >= 3)
            {
                List<double> bg = new List<double>();
                for (int i = 1; i < bursts.Count; i++) bg.Add((bursts[i] - bursts[i - 1]) * 1000.0);
                bg.Sort();
                double bm = bg[bg.Count / 2];
                c.Add("尖峰成批出现：共 " + bursts.Count + " 批，批与批之间中位间隔 " + FmtMs(bm)
                    + "（最小 " + FmtMs(bg[0]) + "、最大 " + FmtMs(bg[bg.Count - 1])
                    + "）。固定节拍强烈指向某个常驻组件（安全软件心跳、厂商服务、监控代理）或设备周期性任务，"
                    + "而不是随机负载。");
            }
            if (pr != null && pr.Cycles >= 2)
                c.Add("批内尖峰呈规律间隔（" + pr.Describe() + "）；若批内间隔远小于批间间隔，"
                    + "说明触发源在一段时间内连续工作，而不是单次事件。");

            if (timerMs > 1.001)
                c.Add("系统定时器精度为 " + timerMs.ToString("0.####") + " ms（未锁到 1 ms）。低延迟场景可由程序调用 timeBeginPeriod(1) 锁定。");
            else if (timerMs > 0)
                c.Add("系统定时器精度 " + timerMs.ToString("0.####") + " ms，已处于 1 ms 级（有程序在请求高精度定时器）。");

            foreach (CheckItem k in checks)
            {
                if (k.Sev == Sev.Ok || string.IsNullOrEmpty(k.Hint)) continue;
                if (k.Sev == Sev.Crit || k.Sev == Sev.Warn)
                    c.Add("体检项「" + k.Title + "」= " + k.Value + "：" + k.Hint);
            }

            if (c.Count == 1) c.Add("未发现明确的异常模式。");
            return c;
        }

        /// <summary>系统信息（只查一次并缓存；WMI 查询失败时逐项降级为未知）。</summary>
        private static List<string[]> SystemInfoRows()
        {
            if (_sysInfoRows != null) return _sysInfoRows;
            List<string[]> r = new List<string[]>();
            r.Add(new string[] { "计算机名", Environment.MachineName });

            string os = "", osVer = "";
            try
            {
                using (System.Management.ManagementObjectSearcher se =
                    new System.Management.ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem"))
                {
                    foreach (System.Management.ManagementBaseObject o in se.Get())
                    {
                        os = Convert.ToString(o["Caption"]);
                        osVer = Convert.ToString(o["Version"]) + " build " + Convert.ToString(o["BuildNumber"]);
                        break;
                    }
                }
            }
            catch { }
            r.Add(new string[] { "操作系统", (os + "  " + osVer + " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")").Trim() });

            string hw = "";
            ulong ram = 0;
            try
            {
                using (System.Management.ManagementObjectSearcher se =
                    new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (System.Management.ManagementBaseObject o in se.Get())
                    {
                        hw = Convert.ToString(o["Manufacturer"]) + " " + Convert.ToString(o["Model"]);
                        ram = Convert.ToUInt64(o["TotalPhysicalMemory"]);
                        break;
                    }
                }
            }
            catch { }
            r.Add(new string[] { "硬件", hw });
            if (ram > 0) r.Add(new string[] { "物理内存", (ram / 1024.0 / 1024 / 1024).ToString("0") + " GB" });

            string cpuName = ""; int logical = 0;
            try
            {
                using (System.Management.ManagementObjectSearcher se =
                    new System.Management.ManagementObjectSearcher("SELECT Name, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (System.Management.ManagementBaseObject o in se.Get())
                    {
                        cpuName = Convert.ToString(o["Name"]);
                        logical += Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    }
                }
            }
            catch { }
            r.Add(new string[] { "CPU", cpuName });
            if (logical > 0) r.Add(new string[] { "逻辑处理器", logical.ToString() + " 个" });
            r.Add(new string[] { "内核追踪", "ETW PerfInfo (DPC / ISR / ImageLoad)" });

            _sysInfoRows = r;
            return r;
        }

        private static List<string[]> _sysInfoRows;

        /// <summary>对给定尖峰序列做周期性分析（UI 与导出共用）。</summary>
        public static PeriodResult AnalyzePeriods(List<SpikeRec> list)
        {
            if (list == null || list.Count < 6) return null;
            List<double> t = new List<double>(list.Count);
            for (int i = 0; i < list.Count; i++) t.Add(list[i].AtSec);
            return Periodicity.Analyze(t, 0.02, 3600.0);
        }

        /// <summary>对当前缓冲里的尖峰做一次分析（UI 用）。</summary>
        public PeriodResult AnalyzePeriodsNow()
        {
            List<SpikeRec> snap;
            lock (_statLock) snap = new List<SpikeRec>(_spikes);
            return AnalyzePeriods(snap);
        }

        /// <summary>当前尖峰的相邻间隔统计（UI 用，省得 UI 再取一次快照）。</summary>
        public void IntervalStats(out double med, out double mn, out double mx)
        {
            List<SpikeRec> snap;
            lock (_statLock) snap = new List<SpikeRec>(_spikes);
            IntervalStatsOf(snap, out med, out mn, out mx);
        }

        private static void IntervalStatsOf(List<SpikeRec> list, out double med, out double mn, out double mx)
        {
            med = mn = mx = 0;
            if (list == null || list.Count < 2) return;
            List<double> d = new List<double>(list.Count - 1);
            for (int i = 1; i < list.Count; i++)
            {
                double ms = list[i].SincePrevMs > 0 ? list[i].SincePrevMs : (list[i].AtSec - list[i - 1].AtSec) * 1000.0;
                if (ms >= 0) d.Add(ms);
            }
            if (d.Count == 0) return;
            d.Sort();
            mn = d[0]; mx = d[d.Count - 1]; med = d[d.Count / 2];
        }

        /// <summary>
        /// 自检入口：造一批合成尖峰，用来验证「导出格式 + 周期分析」这两段逻辑。
        /// 内核追踪需要管理员权限，有了这个入口，无权限时也能把这两段跑通验证。
        /// 仅由 --selftest 调用，不影响正常运行。
        /// </summary>
        public void InjectSyntheticSpikes(int bursts, double periodSec, int perBurst)
        {
            Random rnd = new Random(20260927);
            lock (_statLock)
            {
                _dpc.Clear(); _isr.Clear(); _spikes.Clear();
                _dpcTotalUs = 0; _isrTotalUs = 0;
                for (int i = 0; i < _dpcBuckets.Length; i++) { _dpcBuckets[i] = 0; _isrBuckets[i] = 0; }
                for (int b = 0; b < bursts; b++)
                {
                    double start = b * periodSec;
                    for (int k = 0; k < perBurst; k++)
                    {
                        // 同时喂驱动表与每核心统计，让报告的每个章节都有数据可核对
                        int cpu = rnd.Next(0, Math.Max(1, Environment.ProcessorCount));
                        string drv = "ntoskrnl.exe";
                        string where = "ntoskrnl.exe+0x" + rnd.Next(0x100000, 0x900000).ToString("x");
                        double us = 500 + rnd.NextDouble() * 160;
                        double t = start + k * 1.1 + rnd.NextDouble() * 0.15;   // 阵内约 1.1 秒一个
                        RecordSample(_dpc, drv, where, "DPC", us, cpu, t);

                        // 阵内再塞几条其它驱动的样本，让排行/分档更接近真实形态
                        if (k % 8 == 3)
                            RecordSample(_dpc, "nvlddmkm.sys", "nvlddmkm.sys+0x" + rnd.Next(0x10000, 0x90000).ToString("x"),
                                "DPC", 380 + rnd.NextDouble() * 260, cpu, t);
                        if (k % 11 == 5)
                            RecordSample(_isr, "ndis.sys", "ndis.sys+0x" + rnd.Next(0x10000, 0x50000).ToString("x"),
                                "ISR", 120 + rnd.NextDouble() * 620, cpu, t);
                        if (k % 13 == 7)
                            RecordSample(_dpc, "dxgkrnl.sys", "dxgkrnl.sys+0x" + rnd.Next(0x20000, 0x80000).ToString("x"),
                                "DPC", 240 + rnd.NextDouble() * 300, cpu, t);
                    }
                }
                for (int i = 0; i < _spikes.Count; i++)
                {
                    // 合成样本的时间轴是虚拟的，把可读时间与间隔都对齐过去
                    _spikes[i].T = DateTime.Now.AddSeconds(_spikes[i].AtSec - bursts * periodSec);
                    if (i > 0) _spikes[i].SincePrevMs = (_spikes[i].AtSec - _spikes[i - 1].AtSec) * 1000.0;
                }
            }
        }

        public List<SpikeRec> Spikes
        {
            get { lock (_statLock) return new List<SpikeRec>(_spikes); }
        }

        public void ResetStats()
        {
            lock (_statLock)
            {
                _dpc.Clear(); _isr.Clear(); _spikes.Clear();
                _dpcCountAtLast[0] = 0; _dpcCountAtLast[1] = 0;
                for (int i = 0; i < _dpcCpu.Length; i++) _dpcCpu[i] = null;
                for (int i = 0; i < _isrCpu.Length; i++) _isrCpu[i] = null;
                for (int i = 0; i < _dpcBuckets.Length; i++) _dpcBuckets[i] = 0;
                for (int i = 0; i < _isrBuckets.Length; i++) _isrBuckets[i] = 0;
                _dpcTotalUs = 0; _isrTotalUs = 0;
            }
            _spikeCount = 0;
            _lastSpikeTs = 0;
            Write("statistics reset", LogLevel.Info);
        }

        // =====================================================================
        //  严重度分级（集中一处，UI 与导出共用）
        // =====================================================================
        public static Sev GradeDpc(double us)
        {
            if (us <= 0) return Sev.Neutral;
            if (us < 250) return Sev.Ok;
            if (us < 1000) return Sev.Warn;
            return Sev.Crit;
        }

        public static Sev GradeIsr(double us)
        {
            if (us <= 0) return Sev.Neutral;
            if (us < 100) return Sev.Ok;
            if (us < 500) return Sev.Warn;
            return Sev.Crit;
        }

        public static Sev GradeFor(string type, double us)
        {
            return string.Equals(type, "ISR", StringComparison.OrdinalIgnoreCase) ? GradeIsr(us) : GradeDpc(us);
        }
    }
}
