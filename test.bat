@echo off
rem One-click compile and run all tests (5 logic tests + 1 render test).
rem Usage: double-click test.bat, or run from the command line.
rem The render test refreshes the previews/ screenshots.
setlocal enabledelayedexpansion
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework csc.exe not found.
    exit /b 1
)

set "SRC=%~dp0KeyMouseWidget.cs %~dp0Dashboard.cs %~dp0MouseDistance.cs %~dp0AppActivity.cs %~dp0AppPanel.cs %~dp0ActivityTime.cs %~dp0ShortcutStats.cs %~dp0Insights.cs %~dp0Themes.cs %~dp0HourlyTrend.cs %~dp0AppTelemetry.cs %~dp0ReportDesign.cs %~dp0ReportHabits.cs %~dp0StatisticsReport.cs %~dp0KeyboardHeatmap.cs %~dp0NikkiTheme.cs %~dp0HaloTheme.cs %~dp0ResidentTheme.cs %~dp0GameHud.cs %~dp0MinecraftTheme.cs %~dp0ReleaseAnalytics.cs %~dp0ReleaseReport.cs %~dp0WuxiaTheme.cs %~dp0AssemblyInfo.cs"
set "RES=/resource:"%~dp0assets\wuxia-backgrounds.png",WuxiaBackgrounds /resource:"%~dp0assets\wuxia-widget.png",WuxiaWidget /resource:"%~dp0assets\wuxia-ui.png",WuxiaUi /resource:"%~dp0assets\balatro-backgrounds.png",BalatroBackgrounds /resource:"%~dp0assets\balatro-icons.png",BalatroIcons /resource:"%~dp0assets\nikki-dream.png",NikkiDream /resource:"%~dp0assets\nikki-emotes.png",NikkiEmotes /resource:"%~dp0assets\nikki-pose-trend.png",NikkiPoseTrend /resource:"%~dp0assets\nikki-pose-hours.png",NikkiPoseHours /resource:"%~dp0assets\nikki-pose-keys.png",NikkiPoseKeys /resource:"%~dp0assets\nikki-pose-apps.png",NikkiPoseApps /resource:"%~dp0assets\nikki-pose-insights.png",NikkiPoseInsights /resource:"%~dp0assets\halo-icons.png",HaloIcons /resource:"%~dp0assets\halo-overview.png",HaloOverview /resource:"%~dp0assets\halo-trend.png",HaloTrend /resource:"%~dp0assets\halo-hours.png",HaloHours /resource:"%~dp0assets\halo-keys.png",HaloKeys /resource:"%~dp0assets\halo-apps.png",HaloApps /resource:"%~dp0assets\halo-insights.png",HaloInsights /resource:"%~dp0assets\resident-overview.png",ResidentOverview /resource:"%~dp0assets\resident-trend.png",ResidentTrend /resource:"%~dp0assets\resident-hours.png",ResidentHours /resource:"%~dp0assets\resident-keys.png",ResidentKeys /resource:"%~dp0assets\resident-apps.png",ResidentApps /resource:"%~dp0assets\resident-insights.png",ResidentInsights /resource:"%~dp0assets\resident-icons.png",ResidentIcons /resource:"%~dp0assets\minecraft-overview.png",MinecraftOverview /resource:"%~dp0assets\minecraft-trend.png",MinecraftTrend /resource:"%~dp0assets\minecraft-hours.png",MinecraftHours /resource:"%~dp0assets\minecraft-keys.png",MinecraftKeys /resource:"%~dp0assets\minecraft-apps.png",MinecraftApps /resource:"%~dp0assets\minecraft-insights.png",MinecraftInsights /resource:"%~dp0assets\minecraft-icons.png",MinecraftIcons"
set "OUT=%TEMP%\KeyMouseStatsTests"
if not exist "%OUT%" mkdir "%OUT%"
set "FAIL=0"

for %%T in (DistanceTests ActivityTimeTests AppActivityTests ShortcutTests StatisticsTests HourlyCompareTests KeyboardHeatTests LiveRateTests PopupLifetimeTests ReportVisualTests Report110Tests KeyRangeTests Release120Tests ThemeLoadingTests ThemeOwnershipTests WuxiaThemeTests Release100Tests AccessibilityPatternTests AnomalyRenderTests) do (
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

echo.
if "!FAIL!"=="1" (
    echo ============ SOME TESTS FAILED ============
    exit /b 1
) else (
    echo ============ ALL TESTS PASSED ============
)
endlocal



