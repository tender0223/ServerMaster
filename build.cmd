@echo off
rem ============================================================
rem  ServerMaster offline build script (no VS / no dotnet SDK)
rem  Uses the .NET Framework compiler bundled with Windows.
rem  Output: build\ServerMaster.exe (+ dependency DLLs)
rem  NOTE: keep this file ASCII-only. cmd.exe parses batch files
rem        in the OEM/ANSI codepage, non-ASCII text corrupts it.
rem ============================================================
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] .NET Framework C# compiler not found.
    echo         Build with Visual Studio 2022 ^(ServerMaster.sln^) instead,
    echo         or run: dotnet build src\ServerMaster\ServerMaster.csproj
    exit /b 1
)

if not exist build mkdir build

set RSP=%TEMP%\servermaster_sources.rsp
dir /s /b src\ServerMaster\*.cs > "%RSP%"

echo [1/2] Compiling...
"%CSC%" /nologo /t:winexe /platform:anycpu /utf8output /out:build\ServerMaster.exe /r:lib\AxMSTSCLib.dll /r:lib\MSTSCLib.dll /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "@%RSP%"
if errorlevel 1 (
    echo [FAILED] Compile errors - see output above.
    del "%RSP%" 2>nul
    exit /b 1
)

echo [2/2] Copying dependencies...
copy /y lib\AxMSTSCLib.dll build\ >nul
copy /y lib\MSTSCLib.dll     build\ >nul
del "%RSP%" 2>nul

echo [OK] build\ServerMaster.exe  - runs on Win10/Server2019+ with zero installs.
endlocal
