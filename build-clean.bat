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
    /win32manifest:"app.manifest" ^
    /win32icon:"assets\app-icon.ico" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /define:CLEAN_EDITION /out:"KeyMouseStats.Clean.exe" "KeyMouseWidget.cs" "Dashboard.cs" "MouseDistance.cs" "AppActivity.cs" "AppFlow.cs" "TrendCutoff.cs" "AxisScale.cs" "AppPanel.cs" "ActivityTime.cs" "ShortcutStats.cs" "Insights.cs" "Themes.cs" "HourlyTrend.cs" "HourProfile.cs" "ReportDesign.cs" "ReportHabits.cs" "StatisticsReport.cs" "AppTelemetry.cs" "KeyboardHeatmap.cs" "ClickHeatmap.cs" "NikkiTheme.cs" "HaloTheme.cs" "ResidentTheme.cs" "GameHud.cs" "MinecraftTheme.cs" "ReleaseAnalytics.cs" "ReleaseReport.cs" "WuxiaTheme.cs" "EuroTruckTheme.cs" "OriTheme.cs" "GujianTheme.cs" "WeeklyReview.cs" "RangeAnalysis.cs" "RangeReport.cs" "DataManagement.cs" "DataSettingsDialog.cs" "Wellbeing.cs" "WellbeingSettingsDialog.cs" "FocusMonitor.cs" "FocusBreak.cs" "FocusPillForm.cs" "FocusBreakToast.cs" "PeripheralWear.cs" "PeripheralMouseWear.cs" "KeyboardDeviceIdentity.cs" "MouseDeviceIdentity.cs" "PeripheralHealthView.cs" "ClickScreenIdentity.cs" "StorageUsage.cs" "Version.cs" "Motion.cs" "KeyPalette.cs" "PressDuration.cs" "PressReport.cs" "HoldTrendReport.cs" "RimeLog.cs" "TextPanel.cs" "AutoStart.cs" "DailyReport.cs" "DailyReportPanel.cs" "DailyReportPoster.cs" "PeriodReport.cs" "PeriodPoster.cs" "AssemblyInfo.cs" "StatsStore.cs" "NativeBoot.cs" "KmsDb.cs" "UnixTime.cs" "KmsSchema.cs" "EventRing.cs" "DbWriter.cs" "V2Migrator.cs" "CategoryService.cs" "SampleData.cs" "LayoutFrame.cs" "FlowAnalyzer.cs" "ContextCost.cs" "TimelineWindow.cs" "PrivacyMask.cs" "ShellNotification.cs" "AutomationTrigger.cs" "TimelineDrag.cs" "FlowBoard.cs" "InsightsFlow.cs" "KmsPackage.cs" "BattleReport.cs" "DailyReportBattle.cs" "KmsUi.cs" "LayoutFlow.cs" "UiPrimitives.cs" "ChipLedger.cs" "CustomTheme.cs" "IconPack.cs" "SkinPrompt.cs" "CustomThemeDialog.cs"

if errorlevel 1 (
    echo BUILD FAILED
    echo If the file is reported as "in use by another process", quit the running KeyMouseStats.Clean.exe and retry.
    exit /b 1
)
echo BUILD OK: KeyMouseStats.Clean.exe
endlocal
