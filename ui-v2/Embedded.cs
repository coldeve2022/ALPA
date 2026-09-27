using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ALP2
{
    /// <summary>
    /// 单文件发布支持：把依赖以资源形式塞进 exe，运行时按需取出。
    ///
    /// 这里有一条**必须区分对待**的分界线：
    ///
    ///  · 纯托管依赖（TraceEvent / FastSerialization / Unsafe / …）
    ///    可以直接 Assembly.Load(byte[]) 从内存加载，快且不落盘。
    ///
    ///  · OSExtensions.dll 不行。它内部要靠**原生 DLL** 才能启动内核会话：
    ///    ETWKernelControl.LoadKernelTraceControl() 会用 PROCESSOR_ARCHITECTURE
    ///    加 LoadLibrary 去加载 KernelTraceControl.dll，而 TraceEvent.dll 会从
    ///    「程序集所在目录」推出 amd64\ 子目录（字符串 Amd64/X86/AddCurrentDirectory
    ///    都在 TraceEvent.dll 里）。一旦从内存加载，Assembly.Location 是空字符串，
    ///    拼出来的路径非法 —— 现象就是启动追踪时报「路径中具有非法字符。」。
    ///
    ///  所以 OSExtensions 连同原生 amd64\ 目录必须落到真实磁盘路径，再用
    ///  Assembly.LoadFrom 加载，让 Assembly.Location 有真实值；同时把
    ///  amd64 目录加进进程 DLL 搜索路径，兜住「只给文件名直接 LoadLibrary」的写法。
    ///
    /// 实现约束（重要）：本类的方法体内**绝不能出现任何被嵌入程序集的类型**，
    /// 否则该方法一被 JIT 就会触发依赖加载，抢在 AssemblyResolve 注册之前失败。
    /// </summary>
    internal static class Boot
    {
        /// <summary>可以直接从内存加载的托管依赖。</summary>
        private static readonly string[] MemoryOnly =
        {
            "Microsoft.Diagnostics.FastSerialization",
            "System.Runtime.CompilerServices.Unsafe",
            "Dia2Lib",
            "TraceReloggerLib"
        };

        /// <summary>需要真实磁盘位置 + 原生同伙的程序集。</summary>
        private const string NativeHost = "OSExtensions";

        /// <summary>
        /// 原生文件（资源名 → 解包后的相对路径）。
        /// 架构固定 x64：工程按 /platform:x64 构建，只可能用到 amd64。
        /// </summary>
        private static readonly string[,] NativeFiles =
        {
            { "native_amd64_KernelTraceControl.dll",  @"amd64\KernelTraceControl.dll" },
            { "native_amd64_msdia140.dll",            @"amd64\msdia140.dll" },
            { "native_amd64_msvcp140.dll",            @"amd64\msvcp140.dll" },
            { "native_amd64_vcruntime140.dll",        @"amd64\vcruntime140.dll" },
            { "native_amd64_vcruntime140_1.dll",      @"amd64\vcruntime140_1.dll" }
        };

        private static readonly object _lock = new object();
        private static bool _hooked;
        private static string _stageDir;
        private static bool _staged;
        private static string _stageError;
        private static readonly Dictionary<string, Assembly> Loaded =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>诊断用：原生依赖解包到了哪里、有没有出错。</summary>
        public static string StageDir { get { return _stageDir; } }
        public static string StageError { get { return _stageError; } }
        public static bool Staged { get { return _staged; } }

        /// <summary>在程序入口最前面调用一次。</summary>
        public static void Attach()
        {
            if (_hooked) return;
            _hooked = true;
            try { AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
            catch { }
        }

        /// <summary>
        /// 预检：把原生依赖落盘，并验证 OSExtensions 确实是从磁盘加载的（Location 有效）。
        ///
        /// 启动时就做这一步的价值在于：**不用等提权就能知道单文件版的内核追踪能不能用**。
        /// 一旦这里失败，界面会直接说清楚原因，而不是给用户一片空白数据。
        /// </summary>
        public static string Preflight()
        {
            if (!EnsureStaged()) return "fail: " + (_stageError == null ? "unknown" : _stageError);
            try
            {
                string native = Path.Combine(_stageDir, "amd64", "KernelTraceControl.dll");
                if (!File.Exists(native)) return "fail: native missing at " + native;

                Assembly oe = Assembly.LoadFrom(Path.Combine(_stageDir, "OSExtensions.dll"));
                if (oe == null) return "fail: LoadFrom returned null";

                string loc = "";
                try { loc = oe.Location; }
                catch { }
                if (string.IsNullOrEmpty(loc))
                    return "fail: staged OSExtensions has empty Location (native lookup would break)";

                FileInfo fi = new FileInfo(native);
                return "ok: " + _stageDir + "  (native " + fi.Length + " bytes, OSExtensions loaded from disk)";
            }
            catch (Exception ex)
            {
                return "fail: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        // ------------------------------------------------------------------ 解包

        /// <summary>
        /// 把 OSExtensions.dll 与原生 amd64\ 目录解包到磁盘，并注册进 DLL 搜索路径。
        /// 只在真的要用内核追踪时才会被触发（OSExtensions 是按需加载的）。
        /// </summary>
        private static bool EnsureStaged()
        {
            lock (_lock)
            {
                if (_staged) return true;
                if (_stageError != null) return false;

                try
                {
                    string dir = MakeStageDir();
                    if (dir == null) { _stageError = "no writable directory"; return false; }

                    WriteResource("OSExtensions.dll", Path.Combine(dir, "OSExtensions.dll"));
                    for (int i = 0; i < NativeFiles.GetLength(0); i++)
                    {
                        string rel = NativeFiles[i, 1];
                        string full = Path.Combine(dir, rel);
                        string parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
                        WriteResource(NativeFiles[i, 0], full);
                    }

                    // 兜底：把 amd64 加进进程 DLL 搜索路径，
                    // 这样即使对方只写 LoadLibrary("KernelTraceControl.dll") 也能命中。
                    try { SetDllDirectory(Path.Combine(dir, "amd64")); }
                    catch { }

                    _stageDir = dir;
                    _staged = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _stageError = ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        /// <summary>本程序集版本（三段），用作解包目录名。</summary>
        private static string BuildStamp()
        {
            try { return typeof(Boot).Assembly.GetName().Version.ToString(3); }
            catch { return "0.0.0"; }
        }

        private static string MakeStageDir()
        {
            string[] roots = new string[]
            {
                SafeGet(delegate { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }),
                SafeGet(delegate { return Path.GetTempPath(); }),
                SafeGet(delegate { return Path.GetDirectoryName(typeof(Boot).Assembly.Location); })
            };

            for (int i = 0; i < roots.Length; i++)
            {
                string root = roots[i];
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    // 目录名跟程序集版本绑定：升级后自动用新目录，不会误用上一版解包的原生文件
                    string dir = Path.Combine(root, "ALPA_v2", "runtime", BuildStamp());
                    Directory.CreateDirectory(dir);
                    // 实际验证一下写权限，别只看目录存不存在
                    string probe = Path.Combine(dir, ".w");
                    File.WriteAllBytes(probe, new byte[] { 0 });
                    File.Delete(probe);
                    return dir;
                }
                catch { }
            }
            return null;
        }

        /// <summary>把指定资源写出来；已存在且大小一致就跳过，避免每次启动都重写几 MB。</summary>
        private static void WriteResource(string resourceName, string targetPath)
        {
            Assembly self = typeof(Boot).Assembly;
            string hit = FindResource(self, resourceName);
            if (hit == null) throw new FileNotFoundException("embedded resource not found: " + resourceName);

            using (Stream s = self.GetManifestResourceStream(hit))
            {
                if (s == null) throw new FileNotFoundException("resource stream null: " + hit);
                long size = s.Length;
                try
                {
                    FileInfo fi = new FileInfo(targetPath);
                    if (fi.Exists && fi.Length == size) return;
                }
                catch { }

                byte[] buf = new byte[64 * 1024];
                using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
        }

        private static string FindResource(Assembly self, string want)
        {
            string[] all = self.GetManifestResourceNames();
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i], want, StringComparison.OrdinalIgnoreCase)
                    || all[i].EndsWith("." + want, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }

        // ------------------------------------------------------------------ 解析

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string simple = args.Name;
                int comma = simple.IndexOf(',');
                if (comma > 0) simple = simple.Substring(0, comma);

                Assembly cached;
                if (Loaded.TryGetValue(simple, out cached)) return cached;

                // 1) 需要原生同伙的：解包后从磁盘加载，保证 Assembly.Location 有效
                if (string.Equals(simple, NativeHost, StringComparison.OrdinalIgnoreCase))
                {
                    if (!EnsureStaged()) return null;
                    Assembly staged = Assembly.LoadFrom(Path.Combine(_stageDir, "OSExtensions.dll"));
                    Loaded[simple] = staged;
                    return staged;
                }

                // 2) 纯托管：直接从内存加载（TraceEvent 也走这条，
                //    真正加载原生 DLL 的是 OSExtensions，不是它）
                bool wanted = string.Equals(simple, "Microsoft.Diagnostics.Tracing.TraceEvent", StringComparison.OrdinalIgnoreCase);
                if (!wanted)
                {
                    for (int i = 0; i < MemoryOnly.Length; i++)
                    {
                        if (string.Equals(MemoryOnly[i], simple, StringComparison.OrdinalIgnoreCase)) { wanted = true; break; }
                    }
                }
                if (!wanted) return null;

                string resource = FindResource(typeof(Boot).Assembly, simple + ".dll");
                if (resource == null) return null;

                byte[] buf;
                using (Stream s = typeof(Boot).Assembly.GetManifestResourceStream(resource))
                {
                    if (s == null) return null;
                    buf = new byte[s.Length];
                    int off = 0;
                    while (off < buf.Length)
                    {
                        int n = s.Read(buf, off, buf.Length - off);
                        if (n <= 0) break;
                        off += n;
                    }
                }
                Assembly asm = Assembly.Load(buf);
                Loaded[simple] = asm;
                return asm;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeGet(Func<string> f)
        {
            try { return f(); }
            catch { return null; }
        }
    }
}
