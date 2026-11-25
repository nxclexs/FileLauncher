@echo off
setlocal
rem Build FileLauncher.exe without Visual Studio: csc.exe ships with .NET Framework 4.x

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [error] csc.exe not found. Install .NET Framework 4.x.
    exit /b 1
)

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ ^
    /win32icon:assets\app.ico ^
    /resource:assets\background.png,FileLauncher.background.png ^
    /resource:assets\hero.png,FileLauncher.hero.png ^
    /out:FileLauncher.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    src\FileLauncher.cs

if errorlevel 1 (
    echo [error] build failed
    exit /b 1
)

echo [ok] FileLauncher.exe
