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

set "SRC=%~dp0KeyMouseWidget.cs %~dp0Dashboard.cs %~dp0MouseDistance.cs %~dp0AppActivity.cs %~dp0AppFlow.cs %~dp0TrendCutoff.cs %~dp0AxisScale.cs %~dp0AppPanel.cs %~dp0ActivityTime.cs %~dp0ShortcutStats.cs %~dp0Insights.cs %~dp0Themes.cs %~dp0HourlyTrend.cs %~dp0HourProfile.cs %~dp0AppTelemetry.cs %~dp0ReportDesign.cs %~dp0ReportHabits.cs %~dp0StatisticsReport.cs %~dp0KeyboardHeatmap.cs %~dp0ClickHeatmap.cs %~dp0NikkiTheme.cs %~dp0HaloTheme.cs %~dp0ResidentTheme.cs %~dp0GameHud.cs %~dp0MinecraftTheme.cs %~dp0ReleaseAnalytics.cs %~dp0ReleaseReport.cs %~dp0WuxiaTheme.cs %~dp0EuroTruckTheme.cs %~dp0OriTheme.cs %~dp0GujianTheme.cs %~dp0WeeklyReview.cs %~dp0RangeAnalysis.cs %~dp0RangeReport.cs %~dp0DataManagement.cs %~dp0DataSettingsDialog.cs %~dp0Wellbeing.cs %~dp0WellbeingSettingsDialog.cs %~dp0StorageUsage.cs %~dp0Version.cs %~dp0Motion.cs %~dp0KeyPalette.cs %~dp0PressDuration.cs %~dp0PressReport.cs %~dp0HoldTrendReport.cs %~dp0RimeLog.cs %~dp0TextPanel.cs %~dp0AutoStart.cs %~dp0DailyReport.cs %~dp0DailyReportPanel.cs %~dp0tests\TestDiagnostics.cs %~dp0AssemblyInfo.cs"
set "RES=/resource:"%~dp0assets\eurotruck-overview.jpg",EuroTruckOverview /resource:"%~dp0assets\eurotruck-trend.jpg",EuroTruckTrend /resource:"%~dp0assets\eurotruck-hours.jpg",EuroTruckHours /resource:"%~dp0assets\eurotruck-keys.jpg",EuroTruckKeys /resource:"%~dp0assets\eurotruck-apps.jpg",EuroTruckApps /resource:"%~dp0assets\eurotruck-insights.jpg",EuroTruckInsights /resource:"%~dp0assets\eurotruck-widget.jpg",EuroTruckWidget /resource:"%~dp0assets\eurotruck-icons.png",EuroTruckIcons /resource:"%~dp0assets\wuxia-backgrounds.jpg",WuxiaBackgrounds /resource:"%~dp0assets\wuxia-widget.jpg",WuxiaWidget /resource:"%~dp0assets\wuxia-ui.png",WuxiaUi /resource:"%~dp0assets\balatro-backgrounds.jpg",BalatroBackgrounds /resource:"%~dp0assets\balatro-icons.png",BalatroIcons /resource:"%~dp0assets\nikki-dream.jpg",NikkiDream /resource:"%~dp0assets\nikki-emotes.png",NikkiEmotes /resource:"%~dp0assets\nikki-pose-trend.jpg",NikkiPoseTrend /resource:"%~dp0assets\nikki-pose-hours.jpg",NikkiPoseHours /resource:"%~dp0assets\nikki-pose-keys.jpg",NikkiPoseKeys /resource:"%~dp0assets\nikki-pose-apps.jpg",NikkiPoseApps /resource:"%~dp0assets\nikki-pose-insights.jpg",NikkiPoseInsights /resource:"%~dp0assets\halo-icons.png",HaloIcons /resource:"%~dp0assets\halo-overview.jpg",HaloOverview /resource:"%~dp0assets\halo-trend.jpg",HaloTrend /resource:"%~dp0assets\halo-hours.jpg",HaloHours /resource:"%~dp0assets\halo-keys.jpg",HaloKeys /resource:"%~dp0assets\halo-apps.jpg",HaloApps /resource:"%~dp0assets\halo-insights.jpg",HaloInsights /resource:"%~dp0assets\resident-overview.jpg",ResidentOverview /resource:"%~dp0assets\resident-trend.jpg",ResidentTrend /resource:"%~dp0assets\resident-hours.jpg",ResidentHours /resource:"%~dp0assets\resident-keys.jpg",ResidentKeys /resource:"%~dp0assets\resident-apps.jpg",ResidentApps /resource:"%~dp0assets\resident-insights.jpg",ResidentInsights /resource:"%~dp0assets\resident-icons.png",ResidentIcons /resource:"%~dp0assets\minecraft-overview.jpg",MinecraftOverview /resource:"%~dp0assets\minecraft-trend.jpg",MinecraftTrend /resource:"%~dp0assets\minecraft-hours.jpg",MinecraftHours /resource:"%~dp0assets\minecraft-keys.jpg",MinecraftKeys /resource:"%~dp0assets\minecraft-apps.jpg",MinecraftApps /resource:"%~dp0assets\minecraft-insights.jpg",MinecraftInsights /resource:"%~dp0assets\minecraft-icons.png",MinecraftIcons /resource:"%~dp0assets\ori-overview.jpg",OriOverview /resource:"%~dp0assets\ori-trend.jpg",OriTrend /resource:"%~dp0assets\ori-hours.jpg",OriHours /resource:"%~dp0assets\ori-keys.jpg",OriKeys /resource:"%~dp0assets\ori-apps.jpg",OriApps /resource:"%~dp0assets\ori-insights.jpg",OriInsights /resource:"%~dp0assets\ori-text.jpg",OriText /resource:"%~dp0assets\ori-day.jpg",OriDay /resource:"%~dp0assets\ori-widget.jpg",OriWidget /resource:"%~dp0assets\ori-icons.png",OriIcons /resource:"%~dp0assets\ori-key-ornaments.png",OriKeyOrnaments /resource:"%~dp0assets\ori-report.jpg",OriReport /resource:"%~dp0assets\app-icon.ico",AppIcon"
set "RES=%RES% @"%~dp0theme-extra-resources.rsp""
set "OUT=%TEMP%\KeyMouseStatsTests"
if not exist "%OUT%" mkdir "%OUT%"
set "FAIL=0"
set "TESTS=DistanceTests SaveReliabilityTests DataManagementTests WellbeingTests PressDurationTests MotionTests KeyPaletteTests RangeStatsTests RangeReportTests ActivityTimeTests AppActivityTests AppFlowTests HourProfileTests RimePhraseTests StorageUsageTests TrendCutoffTests AxisScaleTests ShortcutTests StatisticsTests HourlyCompareTests KeyboardHeatTests ClickHeatTests LiveRateTests PopupLifetimeTests ReportVisualTests Report110Tests KeyRangeTests Release120Tests ThemeLoadingTests ThemeOwnershipTests WuxiaThemeTests OriThemeTests GujianThemeTests Release100Tests AnomalyRenderTests RimeLogTests AutoStartTests DailyReportTests HookWatchdogTests RawInputTests"
if defined THEME_ONLY set "TESTS=ThemeLoadingTests WuxiaThemeTests OriThemeTests GujianThemeTests"

for %%T in (%TESTS%) do (
    if /I "%%T"=="ThemeLoadingTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else if /I "%%T"=="ThemeOwnershipTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else if /I "%%T"=="WuxiaThemeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else if /I "%%T"=="OriThemeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else if /I "%%T"=="GujianThemeTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else (
    "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %SRC% "%~dp0tests\%%T.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:RenderDashboard /out:"%OUT%\RenderDashboard.exe" %RES% %SRC% "%~dp0tests\RenderDashboard.cs"
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

echo [Render] ClickHeatRender (previews/click-heat-*.png)
"%CSC%" /nologo /target:exe /optimize+ /main:ClickHeatRender /out:"%OUT%\ClickHeatRender.exe" %RES% %SRC% "%~dp0tests\ClickHeatRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:AppsRender /out:"%OUT%\AppsRender.exe" %RES% %SRC% "%~dp0tests\AppsRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:TrendRender /out:"%OUT%\TrendRender.exe" %RES% %SRC% "%~dp0tests\TrendRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:HoursRender /out:"%OUT%\HoursRender.exe" %RES% %SRC% "%~dp0tests\HoursRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:KeysRender /out:"%OUT%\KeysRender.exe" %RES% %SRC% "%~dp0tests\KeysRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:InsightsRender /out:"%OUT%\InsightsRender.exe" %RES% %SRC% "%~dp0tests\InsightsRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:TextRender /out:"%OUT%\TextRender.exe" %RES% %SRC% "%~dp0tests\TextRender.cs"
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
"%CSC%" /nologo /target:exe /optimize+ /main:WidgetStorageRender /out:"%OUT%\WidgetStorageRender.exe" %RES% %SRC% "%~dp0tests\WidgetStorageRender.cs"
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

echo.
echo [Bench] ThemePerf (keyboard heatmap frames, informational only)
"%CSC%" /nologo /target:exe /optimize+ /main:ThemePerf /out:"%OUT%\ThemePerf.exe" %RES% %SRC% "%~dp0tests\ThemePerf.cs"
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



