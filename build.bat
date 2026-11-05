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

pushd "%~dp0"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /win32manifest:"%~dp0app.manifest" ^
    /win32icon:"%~dp0assets\app-icon.ico" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    @"%~dp0theme-extra-resources.rsp" ^
    /resource:"%~dp0assets\eurotruck-overview.jpg",EuroTruckOverview /resource:"%~dp0assets\eurotruck-trend.jpg",EuroTruckTrend /resource:"%~dp0assets\eurotruck-hours.jpg",EuroTruckHours /resource:"%~dp0assets\eurotruck-keys.jpg",EuroTruckKeys /resource:"%~dp0assets\eurotruck-apps.jpg",EuroTruckApps /resource:"%~dp0assets\eurotruck-insights.jpg",EuroTruckInsights /resource:"%~dp0assets\eurotruck-widget.jpg",EuroTruckWidget /resource:"%~dp0assets\eurotruck-icons.png",EuroTruckIcons /resource:"%~dp0assets\wuxia-backgrounds.jpg",WuxiaBackgrounds /resource:"%~dp0assets\wuxia-widget.jpg",WuxiaWidget /resource:"%~dp0assets\wuxia-ui.png",WuxiaUi /resource:"%~dp0assets\balatro-backgrounds.jpg",BalatroBackgrounds /resource:"%~dp0assets\balatro-icons.png",BalatroIcons /resource:"%~dp0assets\nikki-dream.jpg",NikkiDream /resource:"%~dp0assets\nikki-emotes.png",NikkiEmotes /resource:"%~dp0assets\nikki-pose-trend.jpg",NikkiPoseTrend /resource:"%~dp0assets\nikki-pose-hours.jpg",NikkiPoseHours /resource:"%~dp0assets\nikki-pose-keys.jpg",NikkiPoseKeys /resource:"%~dp0assets\nikki-pose-apps.jpg",NikkiPoseApps /resource:"%~dp0assets\nikki-pose-insights.jpg",NikkiPoseInsights /resource:"%~dp0assets\halo-icons.png",HaloIcons /resource:"%~dp0assets\halo-overview.jpg",HaloOverview /resource:"%~dp0assets\halo-trend.jpg",HaloTrend /resource:"%~dp0assets\halo-hours.jpg",HaloHours /resource:"%~dp0assets\halo-keys.jpg",HaloKeys /resource:"%~dp0assets\halo-apps.jpg",HaloApps /resource:"%~dp0assets\halo-insights.jpg",HaloInsights /resource:"%~dp0assets\resident-overview.jpg",ResidentOverview /resource:"%~dp0assets\resident-trend.jpg",ResidentTrend /resource:"%~dp0assets\resident-hours.jpg",ResidentHours /resource:"%~dp0assets\resident-keys.jpg",ResidentKeys /resource:"%~dp0assets\resident-apps.jpg",ResidentApps /resource:"%~dp0assets\resident-insights.jpg",ResidentInsights /resource:"%~dp0assets\resident-icons.png",ResidentIcons /resource:"%~dp0assets\minecraft-overview.jpg",MinecraftOverview /resource:"%~dp0assets\minecraft-trend.jpg",MinecraftTrend /resource:"%~dp0assets\minecraft-hours.jpg",MinecraftHours /resource:"%~dp0assets\minecraft-keys.jpg",MinecraftKeys /resource:"%~dp0assets\minecraft-apps.jpg",MinecraftApps /resource:"%~dp0assets\minecraft-insights.jpg",MinecraftInsights /resource:"%~dp0assets\minecraft-icons.png",MinecraftIcons /resource:"%~dp0assets\ori-overview.jpg",OriOverview /resource:"%~dp0assets\ori-trend.jpg",OriTrend /resource:"%~dp0assets\ori-hours.jpg",OriHours /resource:"%~dp0assets\ori-keys.jpg",OriKeys /resource:"%~dp0assets\ori-apps.jpg",OriApps /resource:"%~dp0assets\ori-insights.jpg",OriInsights /resource:"%~dp0assets\ori-text.jpg",OriText /resource:"%~dp0assets\ori-day.jpg",OriDay /resource:"%~dp0assets\ori-widget.jpg",OriWidget /resource:"%~dp0assets\ori-icons.png",OriIcons /resource:"%~dp0assets\ori-key-ornaments.png",OriKeyOrnaments /resource:"%~dp0assets\ori-report.jpg",OriReport /resource:"%~dp0assets\app-icon.ico",AppIcon /out:"%~dp0KeyMouseStats.exe" "%~dp0KeyMouseWidget.cs" "%~dp0Dashboard.cs" "%~dp0MouseDistance.cs" "%~dp0AppActivity.cs" "%~dp0AppFlow.cs" "%~dp0TrendCutoff.cs" "%~dp0AxisScale.cs" "%~dp0AppPanel.cs" "%~dp0ActivityTime.cs" "%~dp0ShortcutStats.cs" "%~dp0Insights.cs" "%~dp0Themes.cs" "%~dp0HourlyTrend.cs" "%~dp0HourProfile.cs" "%~dp0ReportDesign.cs" "%~dp0ReportHabits.cs" "%~dp0StatisticsReport.cs" "%~dp0AppTelemetry.cs" "%~dp0KeyboardHeatmap.cs" "%~dp0ClickHeatmap.cs" "%~dp0NikkiTheme.cs" "%~dp0HaloTheme.cs" "%~dp0ResidentTheme.cs" "%~dp0GameHud.cs" "%~dp0MinecraftTheme.cs" "%~dp0ReleaseAnalytics.cs" "%~dp0ReleaseReport.cs" "%~dp0WuxiaTheme.cs" "%~dp0EuroTruckTheme.cs" "%~dp0OriTheme.cs" "%~dp0GujianTheme.cs" "%~dp0WeeklyReview.cs" "%~dp0RangeAnalysis.cs" "%~dp0RangeReport.cs" "%~dp0DataManagement.cs" "%~dp0DataSettingsDialog.cs" "%~dp0Wellbeing.cs" "%~dp0WellbeingSettingsDialog.cs" "%~dp0StorageUsage.cs" "%~dp0Version.cs" "%~dp0Motion.cs" "%~dp0KeyPalette.cs" "%~dp0PressDuration.cs" "%~dp0PressReport.cs" "%~dp0HoldTrendReport.cs" "%~dp0RimeLog.cs" "%~dp0TextPanel.cs" "%~dp0AutoStart.cs" "%~dp0DailyReport.cs" "%~dp0DailyReportPanel.cs" "%~dp0AssemblyInfo.cs"

if errorlevel 1 (
    popd
    echo BUILD FAILED
    echo If the file is reported as "in use by another process", quit the running KeyMouseStats.exe and retry.
    exit /b 1
)
popd
echo BUILD OK: KeyMouseStats.exe
endlocal



