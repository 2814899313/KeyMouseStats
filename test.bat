@echo off
rem One-click compile and run all tests (21 logic/render tests + 1 desktop render test).
rem Usage: double-click test.bat, or run from the command line.
rem   test.bat --logic-only   skips RenderDashboard and ThemePerf, which need an
rem                           interactive desktop session; used by CI.
rem The render test refreshes the previews/ screenshots.
setlocal enabledelayedexpansion
set "LOGIC_ONLY="
if /I "%~1"=="--logic-only" set "LOGIC_ONLY=1"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework csc.exe not found.
    exit /b 1
)

set "SRC=%~dp0KeyMouseWidget.cs %~dp0Dashboard.cs %~dp0MouseDistance.cs %~dp0AppActivity.cs %~dp0AppPanel.cs %~dp0ActivityTime.cs %~dp0ShortcutStats.cs %~dp0Insights.cs %~dp0Themes.cs %~dp0HourlyTrend.cs %~dp0AppTelemetry.cs %~dp0ReportDesign.cs %~dp0ReportHabits.cs %~dp0StatisticsReport.cs %~dp0KeyboardHeatmap.cs %~dp0NikkiTheme.cs %~dp0HaloTheme.cs %~dp0ResidentTheme.cs %~dp0GameHud.cs %~dp0MinecraftTheme.cs %~dp0ReleaseAnalytics.cs %~dp0ReleaseReport.cs %~dp0WuxiaTheme.cs %~dp0EuroTruckTheme.cs %~dp0WeeklyReview.cs %~dp0RangeAnalysis.cs %~dp0RangeReport.cs %~dp0DataManagement.cs %~dp0DataSettingsDialog.cs %~dp0Wellbeing.cs %~dp0WellbeingSettingsDialog.cs %~dp0Version.cs %~dp0PressDuration.cs %~dp0PressReport.cs %~dp0AssemblyInfo.cs"
set "RES=/resource:"%~dp0assets\eurotruck-overview.jpg",EuroTruckOverview /resource:"%~dp0assets\eurotruck-trend.jpg",EuroTruckTrend /resource:"%~dp0assets\eurotruck-hours.jpg",EuroTruckHours /resource:"%~dp0assets\eurotruck-keys.jpg",EuroTruckKeys /resource:"%~dp0assets\eurotruck-apps.jpg",EuroTruckApps /resource:"%~dp0assets\eurotruck-insights.jpg",EuroTruckInsights /resource:"%~dp0assets\eurotruck-widget.jpg",EuroTruckWidget /resource:"%~dp0assets\eurotruck-icons.png",EuroTruckIcons /resource:"%~dp0assets\wuxia-backgrounds.jpg",WuxiaBackgrounds /resource:"%~dp0assets\wuxia-widget.jpg",WuxiaWidget /resource:"%~dp0assets\wuxia-ui.png",WuxiaUi /resource:"%~dp0assets\balatro-backgrounds.jpg",BalatroBackgrounds /resource:"%~dp0assets\balatro-icons.png",BalatroIcons /resource:"%~dp0assets\nikki-dream.jpg",NikkiDream /resource:"%~dp0assets\nikki-emotes.png",NikkiEmotes /resource:"%~dp0assets\nikki-pose-trend.jpg",NikkiPoseTrend /resource:"%~dp0assets\nikki-pose-hours.jpg",NikkiPoseHours /resource:"%~dp0assets\nikki-pose-keys.jpg",NikkiPoseKeys /resource:"%~dp0assets\nikki-pose-apps.jpg",NikkiPoseApps /resource:"%~dp0assets\nikki-pose-insights.jpg",NikkiPoseInsights /resource:"%~dp0assets\halo-icons.png",HaloIcons /resource:"%~dp0assets\halo-overview.jpg",HaloOverview /resource:"%~dp0assets\halo-trend.jpg",HaloTrend /resource:"%~dp0assets\halo-hours.jpg",HaloHours /resource:"%~dp0assets\halo-keys.jpg",HaloKeys /resource:"%~dp0assets\halo-apps.jpg",HaloApps /resource:"%~dp0assets\halo-insights.jpg",HaloInsights /resource:"%~dp0assets\resident-overview.jpg",ResidentOverview /resource:"%~dp0assets\resident-trend.jpg",ResidentTrend /resource:"%~dp0assets\resident-hours.jpg",ResidentHours /resource:"%~dp0assets\resident-keys.jpg",ResidentKeys /resource:"%~dp0assets\resident-apps.jpg",ResidentApps /resource:"%~dp0assets\resident-insights.jpg",ResidentInsights /resource:"%~dp0assets\resident-icons.png",ResidentIcons /resource:"%~dp0assets\minecraft-overview.jpg",MinecraftOverview /resource:"%~dp0assets\minecraft-trend.jpg",MinecraftTrend /resource:"%~dp0assets\minecraft-hours.jpg",MinecraftHours /resource:"%~dp0assets\minecraft-keys.jpg",MinecraftKeys /resource:"%~dp0assets\minecraft-apps.jpg",MinecraftApps /resource:"%~dp0assets\minecraft-insights.jpg",MinecraftInsights /resource:"%~dp0assets\minecraft-icons.png",MinecraftIcons"
set "OUT=%TEMP%\KeyMouseStatsTests"
if not exist "%OUT%" mkdir "%OUT%"
set "FAIL=0"

for %%T in (DistanceTests SaveReliabilityTests DataManagementTests WellbeingTests PressDurationTests RangeStatsTests RangeReportTests ActivityTimeTests AppActivityTests ShortcutTests StatisticsTests HourlyCompareTests KeyboardHeatTests LiveRateTests PopupLifetimeTests ReportVisualTests Report110Tests KeyRangeTests Release120Tests ThemeLoadingTests ThemeOwnershipTests WuxiaThemeTests Release100Tests AnomalyRenderTests) do (
    if /I "%%T"=="ThemeLoadingTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else if /I "%%T"=="ThemeOwnershipTests" (
        "%CSC%" /nologo /target:exe /optimize+ /main:%%T /out:"%OUT%\%%T.exe" %RES% %SRC% "%~dp0tests\%%T.cs"
    ) else if /I "%%T"=="WuxiaThemeTests" (
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
    exit /b 1
) else (
    echo ============ ALL TESTS PASSED ============
)
endlocal



