@echo off
rem One-click compile and run all logic tests plus the desktop render previews.
rem Usage: double-click test.bat, or run from the command line.
rem   test.bat --logic-only   skips RenderDashboard and ThemePerf, which need an
rem                           interactive desktop session; used by CI.
rem The render test refreshes the previews/ screenshots.
setlocal enabledelayedexpansion
set "LOGIC_ONLY="
if /I "%~1"=="--logic-only" set "LOGIC_ONLY=1"
set "THEME_ONLY="
if /I "%~1"=="--theme-only" (
    set "THEME_ONLY=1"
    set "LOGIC_ONLY=1"
)
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework csc.exe not found.
    exit /b 1
)
pushd "%~dp0"

set "SRC=KeyMouseWidget.cs Dashboard.cs MouseDistance.cs AppActivity.cs AppFlow.cs TrendCutoff.cs AxisScale.cs AppPanel.cs ActivityTime.cs ShortcutStats.cs Insights.cs Themes.cs HourlyTrend.cs HourProfile.cs AppTelemetry.cs ReportDesign.cs ReportHabits.cs StatisticsReport.cs KeyboardHeatmap.cs ClickHeatmap.cs NikkiTheme.cs HaloTheme.cs ResidentTheme.cs GameHud.cs MinecraftTheme.cs ReleaseAnalytics.cs ReleaseReport.cs WuxiaTheme.cs EuroTruckTheme.cs OriTheme.cs GujianTheme.cs WeeklyReview.cs RangeAnalysis.cs RangeReport.cs DataManagement.cs DataSettingsDialog.cs Wellbeing.cs WellbeingSettingsDialog.cs StorageUsage.cs Version.cs Motion.cs KeyPalette.cs PressDuration.cs PressReport.cs HoldTrendReport.cs RimeLog.cs TextPanel.cs AutoStart.cs DailyReport.cs DailyReportPanel.cs DailyReportPoster.cs PeriodReport.cs PeriodPoster.cs tests\TestDiagnostics.cs AssemblyInfo.cs StatsStore.cs NativeBoot.cs KmsDb.cs KmsSchema.cs EventRing.cs DbWriter.cs V2Migrator.cs CategoryService.cs SampleData.cs LayoutFrame.cs FlowAnalyzer.cs ContextCost.cs TimelineWindow.cs PrivacyMask.cs ShellNotification.cs AutomationTrigger.cs TimelineDrag.cs FlowBoard.cs InsightsFlow.cs KmsPackage.cs BattleReport.cs DailyReportBattle.cs KmsUi.cs LayoutFlow.cs UiPrimitives.cs ChipLedger.cs"
set "RES=/resource:"assets\eurotruck-overview.jpg",EuroTruckOverview /resource:"assets\eurotruck-trend.jpg",EuroTruckTrend /resource:"assets\eurotruck-hours.jpg",EuroTruckHours /resource:"assets\eurotruck-keys.jpg",EuroTruckKeys /resource:"assets\eurotruck-apps.jpg",EuroTruckApps /resource:"assets\eurotruck-insights.jpg",EuroTruckInsights /resource:"assets\eurotruck-widget.jpg",EuroTruckWidget /resource:"assets\eurotruck-icons.png",EuroTruckIcons /resource:"assets\wuxia-backgrounds.jpg",WuxiaBackgrounds /resource:"assets\wuxia-widget.jpg",WuxiaWidget /resource:"assets\wuxia-ui.png",WuxiaUi /resource:"assets\balatro-backgrounds.jpg",BalatroBackgrounds /resource:"assets\balatro-icons.png",BalatroIcons /resource:"assets\nikki-dream.jpg",NikkiDream /resource:"assets\nikki-emotes.png",NikkiEmotes /resource:"assets\nikki-pose-trend.jpg",NikkiPoseTrend /resource:"assets\nikki-pose-hours.jpg",NikkiPoseHours /resource:"assets\nikki-pose-keys.jpg",NikkiPoseKeys /resource:"assets\nikki-pose-apps.jpg",NikkiPoseApps /resource:"assets\nikki-pose-insights.jpg",NikkiPoseInsights /resource:"assets\halo-icons.png",HaloIcons /resource:"assets\halo-overview.jpg",HaloOverview /resource:"assets\halo-trend.jpg",HaloTrend /resource:"assets\halo-hours.jpg",HaloHours /resource:"assets\halo-keys.jpg",HaloKeys /resource:"assets\halo-apps.jpg",HaloApps /resource:"assets\halo-insights.jpg",HaloInsights /resource:"assets\resident-overview.jpg",ResidentOverview /resource:"assets\resident-trend.jpg",ResidentTrend /resource:"assets\resident-hours.jpg",ResidentHours /resource:"assets\resident-keys.jpg",ResidentKeys /resource:"assets\resident-apps.jpg",ResidentApps /resource:"assets\resident-insights.jpg",ResidentInsights /resource:"assets\resident-icons.png",ResidentIcons /resource:"assets\minecraft-overview.jpg",MinecraftOverview /resource:"assets\minecraft-trend.jpg",MinecraftTrend /resource:"assets\minecraft-hours.jpg",MinecraftHours /resource:"assets\minecraft-keys.jpg",MinecraftKeys /resource:"assets\minecraft-apps.jpg",MinecraftApps /resource:"assets\minecraft-insights.jpg",MinecraftInsights /resource:"assets\minecraft-icons.png",MinecraftIcons /resource:"assets\ori-overview.jpg",OriOverview /resource:"assets\ori-trend.jpg",OriTrend /resource:"assets\ori-hours.jpg",OriHours /resource:"assets\ori-keys.jpg",OriKeys /resource:"assets\ori-apps.jpg",OriApps /resource:"assets\ori-insights.jpg",OriInsights /resource:"assets\ori-text.jpg",OriText /resource:"assets\ori-day.jpg",OriDay /resource:"assets\ori-widget.jpg",OriWidget /resource:"assets\ori-icons.png",OriIcons /resource:"assets\ori-key-ornaments.png",OriKeyOrnaments /resource:"assets\ori-report.jpg",OriReport /resource:"assets\app-icon.ico",AppIcon"
set "RES=%RES% @"theme-extra-resources.rsp""
rem v3.0: 原生 SQLite 引擎只给数据库用例编译,避免每个测试 exe 都背上 2.9 MB。
set "RESNATIVE=/resource:"assets\native\x64\e_sqlite3.dll",KmsNativeX64 /resource:"assets\native\x86\e_sqlite3.dll",KmsNativeX86"
set "OUT=%TEMP%\KeyMouseStatsTests"
if not exist "%OUT%" mkdir "%OUT%"
set "FAIL=0"
set "TESTS=DistanceTests SaveReliabilityTests PersistenceGuardTests DataManagementTests WellbeingTests PressDurationTests MotionTests KeyPaletteTests RangeStatsTests RangeReportTests ActivityTimeTests AppActivityTests AppFlowTests HourProfileTests RimePhraseTests StorageUsageTests TrendCutoffTests AxisScaleTests ShortcutTests StatisticsTests HourlyCompareTests KeyboardHeatTests ClickHeatTests LiveRateTests PopupLifetimeTests ReportVisualTests Report110Tests KeyRangeTests Release120Tests ThemeLoadingTests ThemeOwnershipTests WuxiaThemeTests OriThemeTests GujianThemeTests Release100Tests AnomalyRenderTests RimeLogTests AutoStartTests DailyReportTests HookWatchdogTests RawInputTests MonthlyStorageTests SegmentTrayTests DbBootTests DbSchemaTests RingBufferTests DbWriterTests MigrationTests ClickChannelTests CategoryProviderTests ClickChannelUiTests WidgetHoverTests SampleModeTests LayoutMetricsTests FlowAnalyzerTests TimelineWindowTests PrivacyMaskTests WellbeingAutomationTests TimelineInteractionTests BattleReportTests KmsPackageTests LayoutFlowTests PeriodReportTests PeriodPosterTests ReportPosterTests TransitionGhostTests"
if defined THEME_ONLY set "TESTS=ThemeLoadingTests WuxiaThemeTests OriThemeTests GujianThemeTests"

for %%T in (%TESTS%) do (
    if /I "%%T"=="ThemeLoadingTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="ThemeOwnershipTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="WuxiaThemeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="OriThemeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="GujianThemeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="DbBootTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="DbSchemaTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="DbWriterTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="MigrationTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="SampleModeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="KmsPackageTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else if /I "%%T"=="LayoutFlowTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RESNATIVE% %SRC% "tests\%%T.cs"
    ) else (
    "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %SRC% "tests\%%T.cs"
    )
    if !ERRORLEVEL! neq 0 (
        echo [COMPILE FAIL] %%T
        set "FAIL=1"
    ) else (
        "%OUT%\%%T.exe"
        if !ERRORLEVEL! neq 0 (
            echo [RUN FAIL] %%T
            set "FAIL=1"
        ) else (
            echo [PASS] %%T
        )
    )
)

if defined LOGIC_ONLY goto skipvisual
echo [Render] RenderDashboard (refreshes previews/)
"%CSC%" /nologo /target:exe /optimize+ /main:RenderDashboard /out:"%OUT%\RenderDashboard.exe" %RES% %SRC% "tests\RenderDashboard.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] RenderDashboard
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\RenderDashboard.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] RenderDashboard
        set "FAIL=1"
    ) else (
        echo [PASS] RenderDashboard
    )
)

echo [Render] DashboardAdditionsRender (dashboard-hours-intensity-*.png 等)
"%CSC%" /nologo /target:exe /optimize+ /main:DashboardAdditionsRender /out:"%OUT%\DashboardAdditionsRender.exe" %RES% %SRC% "tests\DashboardAdditionsRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] DashboardAdditionsRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\DashboardAdditionsRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] DashboardAdditionsRender
        set "FAIL=1"
    ) else (
        echo [PASS] DashboardAdditionsRender
    )
)

echo [Render] ClickHeatRender (previews/click-heat-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:ClickHeatRender /out:"%OUT%\ClickHeatRender.exe" %RES% %SRC% "tests\ClickHeatRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] ClickHeatRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\ClickHeatRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] ClickHeatRender
        set "FAIL=1"
    ) else (
        echo [PASS] ClickHeatRender
    )
)

echo [Render] AppsRender (previews/apps-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:AppsRender /out:"%OUT%\AppsRender.exe" %RES% %SRC% "tests\AppsRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] AppsRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\AppsRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] AppsRender
        set "FAIL=1"
    ) else (
        echo [PASS] AppsRender
    )
)

echo [Render] TrendRender (previews/trend-partial*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:TrendRender /out:"%OUT%\TrendRender.exe" %RES% %SRC% "tests\TrendRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] TrendRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\TrendRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] TrendRender
        set "FAIL=1"
    ) else (
        echo [PASS] TrendRender
    )
)

echo [Render] HoursRender (previews/hours-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:HoursRender /out:"%OUT%\HoursRender.exe" %RES% %SRC% "tests\HoursRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] HoursRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\HoursRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] HoursRender
        set "FAIL=1"
    ) else (
        echo [PASS] HoursRender
    )
)

echo [Render] KeysRender (previews/keys-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:KeysRender /out:"%OUT%\KeysRender.exe" %RES% %SRC% "tests\KeysRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] KeysRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\KeysRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] KeysRender
        set "FAIL=1"
    ) else (
        echo [PASS] KeysRender
    )
)

echo [Render] InsightsRender (previews/insights-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:InsightsRender /out:"%OUT%\InsightsRender.exe" %RES% %SRC% "tests\InsightsRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] InsightsRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\InsightsRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] InsightsRender
        set "FAIL=1"
    ) else (
        echo [PASS] InsightsRender
    )
)

echo [Render] TextRender (previews/text-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:TextRender /out:"%OUT%\TextRender.exe" %RES% %SRC% "tests\TextRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] TextRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\TextRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] TextRender
        set "FAIL=1"
    ) else (
        echo [PASS] TextRender
    )
)

echo [Render] WidgetStorageRender (previews/widget-storage-preview.png)
"%CSC%" /nologo /target:exe /optimize+ /main:WidgetStorageRender /out:"%OUT%\WidgetStorageRender.exe" %RES% %SRC% "tests\WidgetStorageRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] WidgetStorageRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\WidgetStorageRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] WidgetStorageRender
        set "FAIL=1"
    ) else (
        echo [PASS] WidgetStorageRender
    )
)

echo [Render] ReportHabitRender (previews/report-habit*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:ReportHabitRender /out:"%OUT%\ReportHabitRender.exe" %SRC% "tests\ReportHabitRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] ReportHabitRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\ReportHabitRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] ReportHabitRender
        set "FAIL=1"
    ) else (
        echo [PASS] ReportHabitRender
    )
)

echo [Render] ReportLiveRender (previews/report-live.png)
"%CSC%" /nologo /target:exe /optimize+ /main:ReportLiveRender /out:"%OUT%\ReportLiveRender.exe" %SRC% "tests\ReportLiveRender.cs"
if !ERRORLEVEL! neq 0 (
    echo [COMPILE FAIL] ReportLiveRender
    set "FAIL=1"
) else (
    pushd "%~dp0"
    "%OUT%\ReportLiveRender.exe"
    set "RC=!ERRORLEVEL!"
    popd
    if !RC! neq 0 (
        echo [RUN FAIL] ReportLiveRender
        set "FAIL=1"
    ) else (
        echo [PASS] ReportLiveRender
    )
)

echo.
echo [Bench] ThemePerf (keyboard heatmap frames, informational only)
"%CSC%" /nologo /target:exe /optimize+ /main:ThemePerf /out:"%OUT%\ThemePerf.exe" %RES% %SRC% "tests\ThemePerf.cs"
if !ERRORLEVEL! equ 0 (
    pushd "%~dp0"
    "%OUT%\ThemePerf.exe"
    popd
)

:skipvisual
echo.
if "!FAIL!"=="1" (
    echo ============ SOME TESTS FAILED ============
    popd
    exit /b 1
) else (
    echo ============ ALL TESTS PASSED ============
)
popd
endlocal



