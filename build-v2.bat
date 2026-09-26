@echo off
rem ===========================================================================
rem  ALPA v2 - 用系统自带的 .NET Framework 编译器重建（无需安装 .NET SDK）
rem
rem  用法：双击运行，或在命令行执行  build-v2.bat
rem  产物：dist\ALPA_v2.exe  + 同目录下的 6 个依赖 DLL
rem
rem  说明：
rem    ui-v2\ 是全新的界面层版本，与原版根目录的 Program.cs / ALPA.exe 完全隔离，
rem    两者可以并存。这个脚本只是给没有 .NET SDK 的机器用的备用构建方式，
rem    有 SDK 的话直接  dotnet build ui-v2\ALPA.V2.csproj -c Release  即可。
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

if not exist "dist" mkdir "dist"

echo [*] 编译 ui-v2 ...
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /langversion:5 ^
  /win32manifest:"ui-v2\app.manifest" ^
  /out:"dist\ALPA_v2.exe" ^
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
  ui-v2\*.cs

if errorlevel 1 (
    echo [x] 编译失败
    exit /b 1
)

echo [*] 复制运行时依赖 ...
for %%F in (
    Microsoft.Diagnostics.Tracing.TraceEvent.dll
    Microsoft.Diagnostics.FastSerialization.dll
    Microsoft.Diagnostics.Tracing.TraceEvent.xml
    Microsoft.Diagnostics.FastSerialization.xml
    OSExtensions.dll
    System.Runtime.CompilerServices.Unsafe.dll
    System.Runtime.CompilerServices.Unsafe.xml
    Dia2Lib.dll
    TraceReloggerLib.dll
    app.config
) do (
    if exist "%%F" copy /y "%%F" "dist\" >nul
)

echo.
echo [OK] 完成：%ROOT%dist\ALPA_v2.exe
echo      双击即可运行；需要管理员权限时程序会自己弹 UAC，拒绝提权也能降级打开。
endlocal
