# ALPA v2 — 界面重制版 / UI Rebuild

> 原版 ALPA（Amazing Latency Performance Audit，作者 amazingb01 / Adiru）的延迟检测能力很强，
> 但整套 UI 是深色单色系，刻度、表格线、正文挤在一起，长时间盯着看很累。
> 这一版把界面层整个换掉，保留原有的 ETW / 性能计数器采集逻辑，并修掉了几处会误导结论的引擎缺陷。

- **不覆盖原版**：根目录的 `Program.cs` / `ALPA.exe` 原样保留，v2 的代码全在 `ui-v2/`，两者可以并存。
- **产物**：`dist\ALPA_v2.exe`（单文件 + 6 个依赖 DLL，免安装）。
- **技术栈不变**：C# / .NET Framework 4.8 / WinForms，仍然只依赖原有的 TraceEvent 一套 DLL。

---

## 1. 直接运行

```
dist\ALPA_v2.exe
```

- 启动时会请求一次 UAC，**拒绝也能用**：程序降级为普通权限运行，顶部挂一条提示，
  只是 DPC / ISR 内核追踪不可用（其余页面照常）。
- 原版是 `requireAdministrator`，非管理员直接拒绝启动，还会偷偷建一个 `ALPA_AutoRun` 计划任务；
  v2 没有这个行为，也不会改动系统。

## 2. 重新构建

有 .NET SDK：

```
dotnet build ui-v2\ALPA.V2.csproj -c Release
```

只有 .NET Framework（没装 SDK）：

```
build-v2.bat
```

`build-v2.bat` 用系统自带的 `csc.exe` 编译，并把依赖 DLL 一并复制到 `dist\`。
代码本身只用到 C# 5 语法，所以老编译器也能过。

---

## 3. 界面上的变化

| | 原版 | v2 |
|---|---|---|
| 主题 | 只有深色 | 浅色为主（可切深色），右上角切换 |
| 控件 | ListView + 系统滚动条 | 全部自绘：卡片、表格、分段控件、按钮、图标 |
| 布局 | 控件绝对定位，窗口一改就错位 | 按实测文字行高计算，DPI 与屏幕尺寸自动适配 |
| 表格 | 每秒 `BeginUpdate/EndUpdate` 整表重建 | 只画可见行，原地排序，选中行跟随驱动不丢 |
| 曲线 | 无 | DPC / ISR 延迟滚动时间线（面积图 + 峰值线 + 阈值线 + 悬停读数） |
| 指标 | Min / Max / Avg | 增加 **P50 / P95 / P99** 百分位（对数分桶直方图） |

页面结构：**概览 / DPC·ISR 延迟 / 进程 / 系统体检 / 启动项 / 控制台**，
`Ctrl+1…6` 切换，也可以用 `--page=N` 直接启动到指定页（写脚本截图时很有用）。

---

## 4. 顺手修掉的引擎问题

这些不是界面问题，是会让人得出错误结论的问题：

1. **ETW 会话释放不掉**
   原版退出时按 `"ALPKernelSession"` 找会话，实际会话名是 `"NT Kernel Logger"`，
   所以内核会话一直挂在系统里。v2 用正确的名字 `Stop()`。

2. **驱动名解析是 O(n) 线性扫描**
   原版每来一个 DPC 事件，都在几百个内核模块里线性查一遍符号。
   改成排序基址数组 + 二分查找 + 结果缓存。

3. **进程枚举卡 UI**
   原版在 UI 线程里 `Process.GetProcesses()`，每秒卡一下。
   挪到后台线程，并把周期放宽到 2 秒。

4. **性能计数器每秒重建**
   网络、磁盘计数器原版每秒 `new` 一次，v2 只建一次。

5. **提权后工作目录跑到 system32**
   原版用相对路径写日志和 CSV，提权后文件落到 `C:\Windows\System32`。
   v2 启动时把工作目录显式设成 exe 所在目录。

6. **显存计量读错计数器（本机实测）**
   `GPU Process Memory\Dedicated Usage` 在本机驱动上会返回离谱值：
   dandanplay 报 `4,616,381,632,512` 字节（≈4.2 TB），同一时刻
   `Local Usage` 只报 1.07 GB。这是 WDDM 计数器自身的怪癖，不是读数写错。
   v2 改用 `Local Usage` 优先，并对 >256 GB 的读数做兜底丢弃 ——
   否则「按显存排序」和 Score 列会被一个假数据整体带歪。

7. **进程 Score 口径**
   `线程数 + 内存/50 + 显存 + I/O(MB)` 保持不变，只是把上面那条显存问题解决后，
   Score 才恢复可比性。

---

## 5. 已知限制

- **DPC / ISR 数据需要管理员权限**：非管理员运行时不采集，概览页曲线会是空的，这符合预期。
- **`Processor Queue Length` 偏高不一定是异常**：这台机器实测过 68~74，
  PowerShell 直接读同一个计数器也是一样的值，属于系统真实负载，不是工具的 bug。
- **表格在极窄窗口下会出现横向滚动条**：这是有意为之 —— 列宽按内容自动量出来，
  放不下就横向滚，而不是把 PID / 内存这类关键数字截成 `4402…`。
- **依赖 DLL 必须和 exe 同目录**：`Microsoft.Diagnostics.Tracing.TraceEvent.dll` 等 6 个文件，
  `build-v2.bat` 会自动复制。

---

## 6. 目录

```
ALPA/
├─ Program.cs / ALPA.exe / comp.bat      # 原版，未改动
├─ ui-v2/                                # v2 源码
│  ├─ ALPA.V2.csproj                     #   SDK 风格工程（net48）
│  ├─ app.manifest                       #   asInvoker + DPI 感知
│  ├─ Palette.cs                         #   配色 / 主题 / 缩放 / 绘制与文本工具
│  ├─ UiKit.cs                           #   自绘控件库（卡片、按钮、导航、指标卡…）
│  ├─ Charts.cs                          #   时间线、横向条形排行、每核心中断条
│  ├─ TableView.cs                       #   自绘表格（列宽自适应 + 横纵向滚动）
│  ├─ Engine.cs                          #   ETW / 计数器 / 系统体检（与 UI 解耦）
│  ├─ Pages.cs / Pages2.cs               #   六个页面
│  └─ MainForm.cs                        #   窗体骨架与布局
├─ build-v2.bat                          # 无 SDK 时的备用构建脚本
├─ dist/                                 # ALPA_v2.exe 与依赖 DLL
└─ screenshots/                          # v2 六页实机截图
```

---

## 7. 许可与署名

界面层由 [@coldeve2022](https://github.com/coldeve2022) 重写；
延迟检测的原始思路与采集逻辑来自 **amazingb01 (Adiru)** 的 ALPA v1.5，
原版 README、捐赠链接与作者署名请见仓库根目录的 `README.md`。
