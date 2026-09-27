using System;
using System.Runtime.InteropServices;

namespace EtwProbe
{
    /// <summary>
    /// 原生依赖自检：不需要管理员权限，就能确认单文件版解包出来的
    /// KernelTraceControl.dll 到底能不能被加载、导出函数能不能解析。
    ///
    /// 这一层是「内核追踪有没有数据」的前置条件：DLL 加载不了，
    /// 后面 ETW 会话一定起不来（现象就是延迟页一片空白）。
    ///
    /// 用法: etwprobe-native.exe &lt;解包目录&gt;
    /// </summary>
    internal static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr h, string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string dir = args.Length > 0 ? args[0] : ".";
            string arch = Environment.Is64BitProcess ? "amd64" : "x86";
            string archDir = System.IO.Path.Combine(dir, arch);
            string target = System.IO.Path.Combine(archDir, "KernelTraceControl.dll");

            Console.WriteLine("==== 原生依赖自检 ====");
            Console.WriteLine("解包目录   = " + dir);
            Console.WriteLine("进程       = " + (Environment.Is64BitProcess ? "64 位" : "32 位"));
            Console.WriteLine("目标 DLL   = " + target + "  存在=" + System.IO.File.Exists(target));
            if (!System.IO.File.Exists(target)) { Console.WriteLine("结果: 失败 - 文件不存在"); return 1; }

            // 模拟 TraceEvent 的做法：把架构子目录加进 DLL 搜索路径
            bool sd = SetDllDirectory(archDir);
            Console.WriteLine("SetDllDirectory(" + arch + ") = " + sd);

            // 1) 用完整路径加载
            IntPtr h1 = LoadLibraryW(target);
            int e1 = Marshal.GetLastWin32Error();
            Console.WriteLine("LoadLibrary(完整路径) = " + h1 + "  错误=" + e1);
            if (h1 != IntPtr.Zero) Report(h1, "（完整路径加载）");

            // 2) 只用文件名加载（依赖搜索路径，覆盖对方「只给文件名」的写法）
            IntPtr h2 = LoadLibraryW("KernelTraceControl.dll");
            int e2 = Marshal.GetLastWin32Error();
            Console.WriteLine("LoadLibrary(仅文件名) = " + h2 + "  错误=" + e2);
            if (h2 != IntPtr.Zero && h2 != h1) Report(h2, "（搜索路径加载）");

            bool ok = (h1 != IntPtr.Zero) || (h2 != IntPtr.Zero);
            Console.WriteLine();
            Console.WriteLine("结果: " + (ok ? "通过 —— 原生依赖可加载，内核会话具备运行条件" : "失败 —— 原生依赖无法加载"));
            Console.WriteLine();
            Console.WriteLine("说明：真正的内核会话还需要管理员权限，本自检不验证那一层。");
            return ok ? 0 : 1;
        }

        private static void Report(IntPtr h, string tag)
        {
            string[] exports = { "StartKernelTrace", "ControlTraceW", "ControlTraceA", "CreateMergedTraceFile" };
            foreach (string x in exports)
            {
                IntPtr p = GetProcAddress(h, x);
                Console.WriteLine("    导出 " + x.PadRight(22) + " = " + p + (p == IntPtr.Zero ? "  (未找到)" : "  OK"));
            }
            Console.WriteLine("    ^ " + tag);
        }
    }
}
