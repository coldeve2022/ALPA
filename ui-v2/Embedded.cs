using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ALP2
{
    /// <summary>
    /// 单文件发布支持：把 4 个第三方依赖 DLL 以资源形式塞进 exe，运行时按需解出来加载。
    ///
    /// 为什么需要它：上游的发布方式是「exe + 6 个 DLL 放同一个目录」，
    /// 少任何一个都起不来，对只想双击运行的人来说是个不必要的门槛。
    /// 现在发布物就是一个文件，随便丢到哪、从哪运行都行。
    ///
    /// 实现约束（重要）：本类的方法体内**绝不能出现任何被嵌入程序集的类型**，
    /// 否则该方法一被 JIT 就会触发依赖加载，抢在 AssemblyResolve 注册之前失败。
    /// 所以这里只用 System / System.Reflection / System.IO。
    /// </summary>
    internal static class Boot
    {
        /// <summary>被嵌入的依赖程序集（简单名）。逻辑资源名就是「简单名 + .dll」。</summary>
        private static readonly string[] Names =
        {
            "Microsoft.Diagnostics.Tracing.TraceEvent",
            "Microsoft.Diagnostics.FastSerialization",
            "OSExtensions",
            "System.Runtime.CompilerServices.Unsafe",
            "Dia2Lib",
            "TraceReloggerLib"
        };

        private static bool _hooked;
        private static readonly Dictionary<string, Assembly> Loaded =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        /// <summary>在程序入口最前面调用一次。</summary>
        public static void Attach()
        {
            if (_hooked) return;
            _hooked = true;
            try { AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
            catch { }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string simple = args.Name;
                int comma = simple.IndexOf(',');
                if (comma > 0) simple = simple.Substring(0, comma);

                Assembly cached;
                if (Loaded.TryGetValue(simple, out cached)) return cached;

                bool wanted = false;
                for (int i = 0; i < Names.Length; i++)
                {
                    if (string.Equals(Names[i], simple, StringComparison.OrdinalIgnoreCase)) { wanted = true; break; }
                }
                if (!wanted) return null;

                Assembly self = typeof(Boot).Assembly;
                string want = simple + ".dll";
                string hit = null;
                string[] all = self.GetManifestResourceNames();
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i], want, StringComparison.OrdinalIgnoreCase)
                        || all[i].EndsWith("." + want, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = all[i];
                        break;
                    }
                }
                if (hit == null) return null;

                byte[] buf;
                using (Stream s = self.GetManifestResourceStream(hit))
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

                // 部分依赖是强名称程序集：Load(byte[]) 会保留其原始标识，
                // 只要版本对得上就能满足引用（这正是 Costura 之类方案的原理）。
                Assembly asm = Assembly.Load(buf);
                Loaded[simple] = asm;
                return asm;
            }
            catch
            {
                return null;
            }
        }
    }
}
