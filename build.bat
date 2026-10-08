@echo off
rem Builds MMServerManager.exe with the C# compiler that ships with Windows (no Visual Studio needed).
rem Optional: put Assets\icon.ico next to this file and it becomes the icon of the exe.
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "ICON="
if exist "Assets\icon.ico" set "ICON=/win32icon:Assets\icon.ico"
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe %ICON% /out:MMServerManager.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll Program.cs
if errorlevel 1 ( echo BUILD FAILED & pause & exit /b 1 )
echo Built MMServerManager.exe
pause
