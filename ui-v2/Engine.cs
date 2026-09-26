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
                if (acc >= target) return Edges[Math.Min(i, Edges.Length - 1)];
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

    internal class ProcRow
    {
        public int Pid;
        public string Name = "";
        public int Threads;
        public string Prio = "Norm";
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
        private readonly Dictionary<ulong, string> _resolveCache = new Dictionary<ulong, string>();
        private readonly List<SpikeRec> _spikes = new List<SpikeRec>();
        private const int MaxSpikes = 600;

        private TraceEventSession _session;
        private Thread _traceThread;
        private Thread _sampleThread;
        private volatile bool _running;
        private volatile bool _stopping;

        private readonly long[] _dpcCountAtLast = new long[2];
        private readonly long[] _isrCountAtLast = new long[2];
        private long _spikeCount;

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

            Write(File.Exists("Microsoft.Diagnostics.Tracing.TraceEvent.dll")
                ? "TraceEvent.dll found"
                : "TraceEvent.dll missing - DPC/ISR tracing unavailable", File.Exists("Microsoft.Diagnostics.Tracing.TraceEvent.dll") ? LogLevel.Ok : LogLevel.Crit);

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
                                _resolveCache.Clear();   // 模块表变了，解析缓存失效
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
                    session.Source.Process();
                }
                catch (Exception ex)
                {
                    Write("kernel tracing error: " + ex.Message, LogLevel.Crit);
                    _session = null;
                }
            });
            _traceThread.IsBackground = true;
            _traceThread.Name = "ALPA-ETW";
            _traceThread.Start();
        }

        private void LoadDriverList()
        {
            try
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
            if (addr == 0) return "unknown";
            string cached;
            lock (_modMap)
            {
                if (_resolveCache.TryGetValue(addr, out cached)) return cached;
                if (_sortedBases.Length != _modMap.Count)
                {
                    _sortedBases = new ulong[_modMap.Count];
                    _modMap.Keys.CopyTo(_sortedBases, 0);
                    Array.Sort(_sortedBases);
                }
                string result = "addr_" + addr.ToString("X");
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
                    if (_modMap.TryGetValue(_sortedBases[best], out nm)) result = nm;
                }
                if (_resolveCache.Count > 20000) _resolveCache.Clear();
                _resolveCache[addr] = result;
                return result;
            }
        }

        private void OnDpcOrIsr(object data, Dictionary<string, DriverStat> target, string type)
        {
            double us = 0;
            bool have = false;
            ulong routine = 0;

            DPCTraceData d = data as DPCTraceData;
            if (d != null)
            {
                object v = d.PayloadByName("ElapsedTimeMSec");
                if (v != null) { us = Convert.ToDouble(v) * 1000.0; have = true; }
                routine = (ulong)d.Routine;
            }
            else
            {
                ISRTraceData i = data as ISRTraceData;
                if (i != null)
                {
                    object v = i.PayloadByName("ElapsedTimeMSec");
                    if (v != null) { us = Convert.ToDouble(v) * 1000.0; have = true; }
                    routine = (ulong)i.Routine;
                }
            }
            if (!have) return;

            string driver = ResolveName(routine);

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
            }

            double thr = type == "DPC" ? DpcThreshold : IsrThreshold;
            if (us >= thr)
            {
                Interlocked.Increment(ref _spikeCount);
                SpikeRec r = new SpikeRec();
                r.T = DateTime.Now;
                r.Driver = driver;
                r.Type = type;
                r.Us = us;
                lock (_statLock)
                {
                    _spikes.Add(r);
                    if (_spikes.Count > MaxSpikes) _spikes.RemoveRange(0, _spikes.Count - MaxSpikes);
                }
                Action<SpikeRec> h = Spike;
                if (h != null) h(r);
            }
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
                    string name = guid == new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61") ? "卓越性能"
                        : guid == new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c") ? "高性能"
                        : guid == new Guid("381b4222-f694-41f0-9685-ff5bb260df2e") ? "平衡"
                        : guid == new Guid("a1841308-3541-4fab-bc81-f71556f20b4a") ? "节能"
                        : "其他/自定义";
                    Sev sv = name == "卓越性能" || name == "高性能" ? Sev.Ok : (name == "平衡" ? Sev.Info : Sev.Warn);
                    Add(list, "电源与性能", "当前电源计划", name, sv,
                        sv == Sev.Ok ? "适合做延迟测试" : "做延迟基准测试建议切到「高性能」或「卓越性能」");
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
            sb.AppendLine("Driver;Type;Count;Current(us);Avg(us);P50(us);P95(us);P99(us);Max(us)");
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
            foreach (DriverStat s in list)
            {
                sb.AppendLine(string.Format("{0};{1};{2};{3:F2};{4:F2};{5:F2};{6:F2};{7:F2};{8:F2}",
                    s.Name, s.Type, s.Count, s.Cur, s.Avg, s.P50, s.P95, s.P99, s.Max));
            }
        }

        public string ExportSpikesCsv(string dir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Time;Driver;Type;Duration(us)");
            lock (_statLock)
            {
                foreach (SpikeRec r in _spikes)
                    sb.AppendLine(string.Format("{0:yyyy-MM-dd HH:mm:ss};{1};{2};{3:F2}", r.T, r.Driver, r.Type, r.Us));
            }
            string f = Path.Combine(dir, "ALPA_v2_Spikes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            File.WriteAllText(f, sb.ToString(), new UTF8Encoding(true));
            return f;
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
            }
            _spikeCount = 0;
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
