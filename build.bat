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
    /win32manifest:"app.manifest" ^
    /win32icon:"assets\app-icon.ico" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    @"theme-extra-resources.rsp" ^
    /resource:"assets\native\x64\e_sqlite3.dll",KmsNativeX64 ^
    /resource:"assets\native\x86\e_sqlite3.dll",KmsNativeX86 ^
    /resource:"assets\eurotruck-overview.jpg",EuroTruckOverview /resource:"assets\eurotruck-trend.jpg",EuroTruckTrend /resource:"assets\eurotruck-hours.jpg",EuroTruckHours /resource:"assets\eurotruck-keys.jpg",EuroTruckKeys /resource:"assets\eurotruck-apps.jpg",EuroTruckApps /resource:"assets\eurotruck-insights.jpg",EuroTruckInsights /resource:"assets\eurotruck-widget.jpg",EuroTruckWidget /resource:"assets\eurotruck-icons.png",EuroTruckIcons /resource:"assets\wuxia-backgrounds.jpg",WuxiaBackgrounds /resource:"assets\wuxia-widget.jpg",WuxiaWidget /resource:"assets\wuxia-ui.png",WuxiaUi /resource:"assets\balatro-backgrounds.jpg",BalatroBackgrounds /resource:"assets\balatro-icons.png",BalatroIcons /resource:"assets\nikki-dream.jpg",NikkiDream /resource:"assets\nikki-emotes.png",NikkiEmotes /resource:"assets\nikki-pose-trend.jpg",NikkiPoseTrend /resource:"assets\nikki-pose-hours.jpg",NikkiPoseHours /resource:"assets\nikki-pose-keys.jpg",NikkiPoseKeys /resource:"assets\nikki-pose-apps.jpg",NikkiPoseApps /resource:"assets\nikki-pose-insights.jpg",NikkiPoseInsights /resource:"assets\halo-icons.png",HaloIcons /resource:"assets\halo-overview.jpg",HaloOverview /resource:"assets\halo-trend.jpg",HaloTrend /resource:"assets\halo-hours.jpg",HaloHours /resource:"assets\halo-keys.jpg",HaloKeys /resource:"assets\halo-apps.jpg",HaloApps /resource:"assets\halo-insights.jpg",HaloInsights /resource:"assets\resident-overview.jpg",ResidentOverview /resource:"assets\resident-trend.jpg",ResidentTrend /resource:"assets\resident-hours.jpg",ResidentHours /resource:"assets\resident-keys.jpg",ResidentKeys /resource:"assets\resident-apps.jpg",ResidentApps /resource:"assets\resident-insights.jpg",ResidentInsights /resource:"assets\resident-icons.png",ResidentIcons /resource:"assets\minecraft-overview.jpg",MinecraftOverview /resource:"assets\minecraft-trend.jpg",MinecraftTrend /resource:"assets\minecraft-hours.jpg",MinecraftHours /resource:"assets\minecraft-keys.jpg",MinecraftKeys /resource:"assets\minecraft-apps.jpg",MinecraftApps /resource:"assets\minecraft-insights.jpg",MinecraftInsights /resource:"assets\minecraft-icons.png",MinecraftIcons /resource:"assets\ori-overview.jpg",OriOverview /resource:"assets\ori-trend.jpg",OriTrend /resource:"assets\ori-hours.jpg",OriHours /resource:"assets\ori-keys.jpg",OriKeys /resource:"assets\ori-apps.jpg",OriApps /resource:"assets\ori-insights.jpg",OriInsights /resource:"assets\ori-text.jpg",OriText /resource:"assets\ori-day.jpg",OriDay /resource:"assets\ori-widget.jpg",OriWidget /resource:"assets\ori-icons.png",OriIcons /resource:"assets\ori-key-ornaments.png",OriKeyOrnaments /resource:"assets\ori-report.jpg",OriReport /resource:"assets\app-icon.ico",AppIcon /out:"KeyMouseStats.exe" "KeyMouseWidget.cs" "Dashboard.cs" "MouseDistance.cs" "AppActivity.cs" "AppFlow.cs" "TrendCutoff.cs" "AxisScale.cs" "AppPanel.cs" "ActivityTime.cs" "ShortcutStats.cs" "Insights.cs" "Themes.cs" "HourlyTrend.cs" "HourProfile.cs" "ReportDesign.cs" "ReportHabits.cs" "StatisticsReport.cs" "AppTelemetry.cs" "KeyboardHeatmap.cs" "ClickHeatmap.cs" "NikkiTheme.cs" "HaloTheme.cs" "ResidentTheme.cs" "GameHud.cs" "MinecraftTheme.cs" "ReleaseAnalytics.cs" "ReleaseReport.cs" "WuxiaTheme.cs" "EuroTruckTheme.cs" "OriTheme.cs" "GujianTheme.cs" "WeeklyReview.cs" "RangeAnalysis.cs" "RangeReport.cs" "DataManagement.cs" "DataSettingsDialog.cs" "Wellbeing.cs" "WellbeingSettingsDialog.cs" "StorageUsage.cs" "Version.cs" "Motion.cs" "KeyPalette.cs" "PressDuration.cs" "PressReport.cs" "HoldTrendReport.cs" "RimeLog.cs" "TextPanel.cs" "AutoStart.cs" "DailyReport.cs" "DailyReportPanel.cs" "DailyReportPoster.cs" "PeriodReport.cs" "PeriodPoster.cs" "AssemblyInfo.cs" "StatsStore.cs" "NativeBoot.cs" "KmsDb.cs" "KmsSchema.cs" "EventRing.cs" "DbWriter.cs" "V2Migrator.cs" "CategoryService.cs" "SampleData.cs" "LayoutFrame.cs" "FlowAnalyzer.cs" "ContextCost.cs" "TimelineWindow.cs" "PrivacyMask.cs" "ShellNotification.cs" "AutomationTrigger.cs" "TimelineDrag.cs" "FlowBoard.cs" "InsightsFlow.cs" "KmsPackage.cs" "BattleReport.cs" "DailyReportBattle.cs" "KmsUi.cs" "LayoutFlow.cs" "UiPrimitives.cs" "ChipLedger.cs"

if errorlevel 1 (
    popd
    echo BUILD FAILED
    echo If the file is reported as "in use by another process", quit the running KeyMouseStats.exe and retry.
    exit /b 1
)
popd
echo BUILD OK: KeyMouseStats.exe
endlocal



