@echo off
rem ============================================================
rem  ServerMaster offline build script (no VS / no dotnet SDK)
rem  Uses the .NET Framework compiler bundled with Windows.
rem  Output: build\ServerMaster.exe (+ dependency DLLs)
rem  NOTE: keep this file ASCII-only. cmd.exe parses batch files
rem        in the OEM/ANSI codepage, non-ASCII text corrupts it.
rem  Hot-swap: if ServerMaster.exe is locked by a running
rem        instance, the new build is kept as ServerMaster.new.exe
rem        and swapped in on the next run after the app closes.
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
"%CSC%" /nologo /t:winexe /platform:anycpu /utf8output /out:build\ServerMaster.new.exe /r:lib\AxMSTSCLib.dll /r:lib\MSTSCLib.dll /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "@%RSP%"
if errorlevel 1 (
    echo [FAILED] Compile errors - see output above.
    del "%RSP%" 2>nul
    exit /b 1
)

echo [2/2] Copying dependencies...
copy /y lib\AxMSTSCLib.dll build\ >nul
copy /y lib\MSTSCLib.dll     build\ >nul
del "%RSP%" 2>nul

rem If the old exe is locked by a running instance, keep the new
rem build as ServerMaster.new.exe; the next run swaps it in.
del build\ServerMaster.exe 2>nul
if exist build\ServerMaster.exe (
    echo [WARN] ServerMaster.exe is running - new build kept as build\ServerMaster.new.exe
    echo        Close the app, then run build.cmd again to swap it in.
) else (
    if exist build\ServerMaster.new.exe ren build\ServerMaster.new.exe ServerMaster.exe
)

echo [OK] build\ServerMaster.exe  - runs on Win10/Server2019+ with zero installs.
endlocal
