@echo off
rem ===========================================================================
rem  ALPA v2 - 用系统自带的 .NET Framework 编译器重建（无需安装 .NET SDK）
rem
rem  用法：双击运行，或在命令行执行  build-v2.bat
rem  产物：仓库根目录的 ALPA_v2.exe —— 单文件，双击即用
rem
rem  说明：
rem    ui-v2\ 是全新的界面层版本，与原版根目录的 Program.cs / ALPA.exe 完全隔离，
rem    两者可以并存。这个脚本只是给没有 .NET SDK 的机器用的备用构建方式；
rem    有 SDK 的话直接  dotnet build ui-v2\ALPA.V2.csproj -c Release
rem    （产物在 ui-v2\build\ALPA_v2.exe，复制到根目录即可）。
rem
rem    4 个依赖 DLL 以【嵌入资源】形式打进 exe，由 ui-v2\Embedded.cs 在运行时解出加载，
rem    所以不需要把它们跟 exe 放在一起。下面 /resource: 那几行就是塞进去的动作。
rem ===========================================================================
setlocal

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [x] 找不到 csc.exe，请确认已安装 .NET Framework 4.x
    exit /b 1
)

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\System.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"

echo [*] 编译 ui-v2 ...
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /langversion:5 ^
  /win32manifest:"ui-v2\app.manifest" ^
  /out:"ALPA_v2.exe" ^
  /reference:"%FW%\System.dll" ^
  /reference:"%FW%\System.Core.dll" ^
  /reference:"%FW%\System.Drawing.dll" ^
  /reference:"%FW%\System.Windows.Forms.dll" ^
  /reference:"%FW%\System.Management.dll" ^
  /reference:"%FW%\System.ServiceProcess.dll" ^
  /reference:"%FW%\netstandard.dll" ^
  /reference:"Microsoft.Diagnostics.Tracing.TraceEvent.dll" ^
  /reference:"Microsoft.Diagnostics.FastSerialization.dll" ^
  /reference:"OSExtensions.dll" ^
  /reference:"System.Runtime.CompilerServices.Unsafe.dll" ^
  /resource:"Microsoft.Diagnostics.Tracing.TraceEvent.dll",Microsoft.Diagnostics.Tracing.TraceEvent.dll ^
  /resource:"Microsoft.Diagnostics.FastSerialization.dll",Microsoft.Diagnostics.FastSerialization.dll ^
  /resource:"OSExtensions.dll",OSExtensions.dll ^
  /resource:"System.Runtime.CompilerServices.Unsafe.dll",System.Runtime.CompilerServices.Unsafe.dll ^
  /resource:"Dia2Lib.dll",Dia2Lib.dll ^
  /resource:"TraceReloggerLib.dll",TraceReloggerLib.dll ^
  ui-v2\*.cs

if errorlevel 1 (
    echo [x] 编译失败
    exit /b 1
)

echo.
echo [OK] 完成：%ROOT%ALPA_v2.exe   （单文件，不需要任何 DLL 陪着）
echo      双击运行；需要管理员权限时程序会自己弹 UAC，拒绝提权也能降级打开。
endlocal
