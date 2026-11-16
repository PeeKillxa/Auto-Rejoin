@echo off
setlocal
cd /d "%~dp0"
set "CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

set "BUILD_OUTPUT=%~1"
if not defined BUILD_OUTPUT set "BUILD_OUTPUT=NexusRobloxManager.exe"
echo Compiling %BUILD_OUTPUT% ...
"%CSC%" /nologo /target:winexe /optimize+ /utf8output "/out:%BUILD_OUTPUT%" /win32icon:app.ico /win32manifest:app.manifest /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:lib\WebView2\Microsoft.Web.WebView2.Core.dll /r:lib\WebView2\Microsoft.Web.WebView2.WinForms.dll /resource:lib\WebView2\Microsoft.Web.WebView2.Core.dll,Nexus.WebView2.Core.dll /resource:lib\WebView2\Microsoft.Web.WebView2.WinForms.dll,Nexus.WebView2.WinForms.dll /resource:lib\WebView2\x64\WebView2Loader.dll,Nexus.WebView2.Loader.x64.dll /resource:lib\WebView2\x86\WebView2Loader.dll,Nexus.WebView2.Loader.x86.dll /resource:lib\WebView2\LICENSE.txt,Nexus.WebView2.License.txt src\*.cs
if errorlevel 1 (
    echo Build Failed!
    exit /b 1
)
echo Build OK - %BUILD_OUTPUT% ready!
