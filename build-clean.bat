@echo off
rem Clean edition build: all features, no embedded art assets (uses the csc.exe bundled with Windows)
setlocal
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework csc.exe not found.
    exit /b 1
)

rem Detect if the app is already running (it locks the output file, preventing overwrite).
tasklist /FI "IMAGENAME eq KeyMouseStats.Clean.exe" 2>nul | find /I "KeyMouseStats.Clean.exe" >nul
if not errorlevel 1 (
    echo ERROR: KeyMouseStats.Clean.exe is currently running.
    echo Right-click the tray icon and choose "Quit", then run build.bat again.
    exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /win32manifest:"%~dp0app.manifest" ^
    /win32icon:"%~dp0assets\app-icon.ico" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /define:CLEAN_EDITION /out:"%~dp0KeyMouseStats.Clean.exe" "%~dp0KeyMouseWidget.cs" "%~dp0Dashboard.cs" "%~dp0MouseDistance.cs" "%~dp0AppActivity.cs" "%~dp0AppFlow.cs" "%~dp0TrendCutoff.cs" "%~dp0AxisScale.cs" "%~dp0AppPanel.cs" "%~dp0ActivityTime.cs" "%~dp0ShortcutStats.cs" "%~dp0Insights.cs" "%~dp0Themes.cs" "%~dp0HourlyTrend.cs" "%~dp0HourProfile.cs" "%~dp0ReportDesign.cs" "%~dp0ReportHabits.cs" "%~dp0StatisticsReport.cs" "%~dp0AppTelemetry.cs" "%~dp0KeyboardHeatmap.cs" "%~dp0ClickHeatmap.cs" "%~dp0NikkiTheme.cs" "%~dp0HaloTheme.cs" "%~dp0ResidentTheme.cs" "%~dp0GameHud.cs" "%~dp0MinecraftTheme.cs" "%~dp0ReleaseAnalytics.cs" "%~dp0ReleaseReport.cs" "%~dp0WuxiaTheme.cs" "%~dp0EuroTruckTheme.cs" "%~dp0OriTheme.cs" "%~dp0GujianTheme.cs" "%~dp0WeeklyReview.cs" "%~dp0RangeAnalysis.cs" "%~dp0RangeReport.cs" "%~dp0DataManagement.cs" "%~dp0DataSettingsDialog.cs" "%~dp0Wellbeing.cs" "%~dp0WellbeingSettingsDialog.cs" "%~dp0StorageUsage.cs" "%~dp0Version.cs" "%~dp0Motion.cs" "%~dp0KeyPalette.cs" "%~dp0PressDuration.cs" "%~dp0PressReport.cs" "%~dp0HoldTrendReport.cs" "%~dp0RimeLog.cs" "%~dp0TextPanel.cs" "%~dp0AutoStart.cs" "%~dp0DailyReport.cs" "%~dp0DailyReportPanel.cs" "%~dp0DailyReportPoster.cs" "%~dp0PeriodReport.cs" "%~dp0PeriodPoster.cs" "%~dp0AssemblyInfo.cs" "%~dp0StatsStore.cs" "%~dp0NativeBoot.cs" "%~dp0KmsDb.cs" "%~dp0KmsSchema.cs" "%~dp0EventRing.cs" "%~dp0DbWriter.cs" "%~dp0V2Migrator.cs" "%~dp0CategoryService.cs" "%~dp0SampleData.cs" "%~dp0LayoutFrame.cs" "%~dp0FlowAnalyzer.cs" "%~dp0ContextCost.cs" "%~dp0TimelineWindow.cs" "%~dp0PrivacyMask.cs" "%~dp0ShellNotification.cs" "%~dp0AutomationTrigger.cs" "%~dp0TimelineDrag.cs" "%~dp0FlowBoard.cs" "%~dp0InsightsFlow.cs" "%~dp0KmsPackage.cs" "%~dp0BattleReport.cs" "%~dp0DailyReportBattle.cs" "%~dp0KmsUi.cs" "%~dp0LayoutFlow.cs" "%~dp0UiPrimitives.cs" "%~dp0ChipLedger.cs"

if errorlevel 1 (
    echo BUILD FAILED
    echo If the file is reported as "in use by another process", quit the running KeyMouseStats.Clean.exe and retry.
    exit /b 1
)
echo BUILD OK: KeyMouseStats.Clean.exe
endlocal
