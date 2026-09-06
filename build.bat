@echo off
rem Build script for KeyMouseWidget (uses the csc.exe bundled with Windows)
setlocal
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework csc.exe not found.
    exit /b 1
)

rem Detect if the app is already running (it locks the output file, preventing overwrite).
tasklist /FI "IMAGENAME eq KeyMouseStats.exe" 2>nul | find /I "KeyMouseStats.exe" >nul
if not errorlevel 1 (
    echo ERROR: KeyMouseStats.exe is currently running.
    echo Right-click the tray icon and choose "Quit", then run build.bat again.
    exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /win32manifest:"%~dp0app.manifest" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /resource:"%~dp0assets\wuxia-backgrounds.png",WuxiaBackgrounds /resource:"%~dp0assets\wuxia-widget.png",WuxiaWidget /resource:"%~dp0assets\wuxia-ui.png",WuxiaUi /resource:"%~dp0assets\balatro-backgrounds.png",BalatroBackgrounds /resource:"%~dp0assets\balatro-icons.png",BalatroIcons /resource:"%~dp0assets\nikki-dream.png",NikkiDream /resource:"%~dp0assets\nikki-emotes.png",NikkiEmotes /resource:"%~dp0assets\nikki-pose-trend.png",NikkiPoseTrend /resource:"%~dp0assets\nikki-pose-hours.png",NikkiPoseHours /resource:"%~dp0assets\nikki-pose-keys.png",NikkiPoseKeys /resource:"%~dp0assets\nikki-pose-apps.png",NikkiPoseApps /resource:"%~dp0assets\nikki-pose-insights.png",NikkiPoseInsights /resource:"%~dp0assets\halo-icons.png",HaloIcons /resource:"%~dp0assets\halo-overview.png",HaloOverview /resource:"%~dp0assets\halo-trend.png",HaloTrend /resource:"%~dp0assets\halo-hours.png",HaloHours /resource:"%~dp0assets\halo-keys.png",HaloKeys /resource:"%~dp0assets\halo-apps.png",HaloApps /resource:"%~dp0assets\halo-insights.png",HaloInsights /resource:"%~dp0assets\resident-overview.png",ResidentOverview /resource:"%~dp0assets\resident-trend.png",ResidentTrend /resource:"%~dp0assets\resident-hours.png",ResidentHours /resource:"%~dp0assets\resident-keys.png",ResidentKeys /resource:"%~dp0assets\resident-apps.png",ResidentApps /resource:"%~dp0assets\resident-insights.png",ResidentInsights /resource:"%~dp0assets\resident-icons.png",ResidentIcons /resource:"%~dp0assets\minecraft-overview.png",MinecraftOverview /resource:"%~dp0assets\minecraft-trend.png",MinecraftTrend /resource:"%~dp0assets\minecraft-hours.png",MinecraftHours /resource:"%~dp0assets\minecraft-keys.png",MinecraftKeys /resource:"%~dp0assets\minecraft-apps.png",MinecraftApps /resource:"%~dp0assets\minecraft-insights.png",MinecraftInsights /resource:"%~dp0assets\minecraft-icons.png",MinecraftIcons /out:"%~dp0KeyMouseStats.exe" "%~dp0KeyMouseWidget.cs" "%~dp0Dashboard.cs" "%~dp0MouseDistance.cs" "%~dp0AppActivity.cs" "%~dp0AppPanel.cs" "%~dp0ActivityTime.cs" "%~dp0ShortcutStats.cs" "%~dp0Insights.cs" "%~dp0Themes.cs" "%~dp0HourlyTrend.cs" "%~dp0ReportDesign.cs" "%~dp0ReportHabits.cs" "%~dp0StatisticsReport.cs" "%~dp0AppTelemetry.cs" "%~dp0KeyboardHeatmap.cs" "%~dp0NikkiTheme.cs" "%~dp0HaloTheme.cs" "%~dp0ResidentTheme.cs" "%~dp0GameHud.cs" "%~dp0MinecraftTheme.cs" "%~dp0ReleaseAnalytics.cs" "%~dp0ReleaseReport.cs" "%~dp0WuxiaTheme.cs" "%~dp0AssemblyInfo.cs"

if errorlevel 1 (
    echo BUILD FAILED
    echo If the file is reported as "in use by another process", quit the running KeyMouseStats.exe and retry.
    exit /b 1
)
echo BUILD OK: KeyMouseStats.exe
endlocal



