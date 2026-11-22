@echo off
setlocal enabledelayedexpansion
set "LOGIC_ONLY="
set "THEME_ONLY="
set "REBUILD="
set "FILTER="

:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--logic-only" (
    set "LOGIC_ONLY=1"
    shift
    goto parse_args
)
if /I "%~1"=="--theme-only" (
    set "THEME_ONLY=1"
    set "LOGIC_ONLY=1"
    shift
    goto parse_args
)
if /I "%~1"=="--rebuild" (
    set "REBUILD=1"
    shift
    goto parse_args
)
set "FILTER=%~1"
shift
goto parse_args
:args_done

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework csc.exe not found.
    exit /b 1
)
pushd "%~dp0"

set "CORE_SRC=KeyMouseWidget.cs Dashboard.cs MouseDistance.cs AppActivity.cs AppFlow.cs TrendCutoff.cs AxisScale.cs AppPanel.cs ActivityTime.cs ShortcutStats.cs Insights.cs Themes.cs HourlyTrend.cs HourProfile.cs AppTelemetry.cs ReportDesign.cs ReportHabits.cs StatisticsReport.cs KeyboardHeatmap.cs ClickHeatmap.cs NikkiTheme.cs HaloTheme.cs ResidentTheme.cs GameHud.cs MinecraftTheme.cs ReleaseAnalytics.cs ReleaseReport.cs WuxiaTheme.cs EuroTruckTheme.cs OriTheme.cs GujianTheme.cs WeeklyReview.cs RangeAnalysis.cs RangeReport.cs DataManagement.cs DataSettingsDialog.cs Wellbeing.cs WellbeingSettingsDialog.cs FocusMonitor.cs FocusBreak.cs FocusPillForm.cs FocusBreakToast.cs PeripheralWear.cs PeripheralMouseWear.cs KeyboardDeviceIdentity.cs MouseDeviceIdentity.cs PeripheralHealthView.cs ClickScreenIdentity.cs StorageUsage.cs Version.cs Motion.cs KeyPalette.cs PressDuration.cs PressReport.cs HoldTrendReport.cs RimeLog.cs TextPanel.cs AutoStart.cs DailyReport.cs DailyReportPanel.cs DailyReportPoster.cs PeriodReport.cs PeriodPoster.cs AssemblyInfo.cs StatsStore.cs NativeBoot.cs KmsDb.cs UnixTime.cs KmsSchema.cs EventRing.cs DbWriter.cs V2Migrator.cs CategoryService.cs SampleData.cs LayoutFrame.cs FlowAnalyzer.cs ContextCost.cs TimelineWindow.cs PrivacyMask.cs ShellNotification.cs AutomationTrigger.cs TimelineDrag.cs FlowBoard.cs InsightsFlow.cs KmsPackage.cs BattleReport.cs DailyReportBattle.cs KmsUi.cs LayoutFlow.cs UiPrimitives.cs ChipLedger.cs CustomTheme.cs IconPack.cs SkinPrompt.cs CustomThemeDialog.cs"
set "TESTDIAG=tests\TestDiagnostics.cs"
set "RES=/resource:"assets\eurotruck-overview.jpg",EuroTruckOverview /resource:"assets\eurotruck-trend.jpg",EuroTruckTrend /resource:"assets\eurotruck-hours.jpg",EuroTruckHours /resource:"assets\eurotruck-keys.jpg",EuroTruckKeys /resource:"assets\eurotruck-apps.jpg",EuroTruckApps /resource:"assets\eurotruck-insights.jpg",EuroTruckInsights /resource:"assets\eurotruck-widget.jpg",EuroTruckWidget /resource:"assets\eurotruck-icons.png",EuroTruckIcons /resource:"assets\wuxia-backgrounds.jpg",WuxiaBackgrounds /resource:"assets\wuxia-widget.jpg",WuxiaWidget /resource:"assets\wuxia-ui.png",WuxiaUi /resource:"assets\balatro-backgrounds.jpg",BalatroBackgrounds /resource:"assets\balatro-icons.png",BalatroIcons /resource:"assets\nikki-dream.jpg",NikkiDream /resource:"assets\nikki-emotes.png",NikkiEmotes /resource:"assets\nikki-pose-trend.jpg",NikkiPoseTrend /resource:"assets\nikki-pose-hours.jpg",NikkiPoseHours /resource:"assets\nikki-pose-keys.jpg",NikkiPoseKeys /resource:"assets\nikki-pose-apps.jpg",NikkiPoseApps /resource:"assets\nikki-pose-insights.jpg",NikkiPoseInsights /resource:"assets\halo-icons.png",HaloIcons /resource:"assets\halo-overview.jpg",HaloOverview /resource:"assets\halo-trend.jpg",HaloTrend /resource:"assets\halo-hours.jpg",HaloHours /resource:"assets\halo-keys.jpg",HaloKeys /resource:"assets\halo-apps.jpg",HaloApps /resource:"assets\halo-insights.jpg",HaloInsights /resource:"assets\resident-overview.jpg",ResidentOverview /resource:"assets\resident-trend.jpg",ResidentTrend /resource:"assets\resident-hours.jpg",ResidentHours /resource:"assets\resident-keys.jpg",ResidentKeys /resource:"assets\resident-apps.jpg",ResidentApps /resource:"assets\resident-insights.jpg",ResidentInsights /resource:"assets\resident-icons.png",ResidentIcons /resource:"assets\minecraft-overview.jpg",MinecraftOverview /resource:"assets\minecraft-trend.jpg",MinecraftTrend /resource:"assets\minecraft-hours.jpg",MinecraftHours /resource:"assets\minecraft-keys.jpg",MinecraftKeys /resource:"assets\minecraft-apps.jpg",MinecraftApps /resource:"assets\minecraft-insights.jpg",MinecraftInsights /resource:"assets\minecraft-icons.png",MinecraftIcons /resource:"assets\ori-overview.jpg",OriOverview /resource:"assets\ori-trend.jpg",OriTrend /resource:"assets\ori-hours.jpg",OriHours /resource:"assets\ori-keys.jpg",OriKeys /resource:"assets\ori-apps.jpg",OriApps /resource:"assets\ori-insights.jpg",OriInsights /resource:"assets\ori-text.jpg",OriText /resource:"assets\ori-day.jpg",OriDay /resource:"assets\ori-widget.jpg",OriWidget /resource:"assets\ori-icons.png",OriIcons /resource:"assets\ori-key-ornaments.png",OriKeyOrnaments /resource:"assets\ori-report.jpg",OriReport /resource:"assets\app-icon.ico",AppIcon"
set "RES=%RES% @"theme-extra-resources.rsp""
set "RESNATIVE=/resource:"assets\native\x64\e_sqlite3.dll",KmsNativeX64 /resource:"assets\native\x86\e_sqlite3.dll",KmsNativeX86"

set "CACHE=%TEMP%\kms_test_cache"
set "OUT=%CACHE%"
set "CORE=%CACHE%\kms_test_core.dll"
set "IVT=%CACHE%\kms_test_internals.cs"
set "SIG=%CACHE%\sources.sig"
set "TESTSIG=%CACHE%\tests.sig"
set "WORKER=%CACHE%\compile_one.cmd"
set "JOBS=6"
set "FAIL=0"
set "START_SEC="

if defined REBUILD (
    if exist "%CACHE%" rmdir /s /q "%CACHE%"
)
if not exist "%CACHE%" mkdir "%CACHE%"
if not exist "%OUT%" mkdir "%OUT%"
del /q "%OUT%\*.running" "%OUT%\*.ok" "%OUT%\*.fail" 2>nul

for /f %%T in ('powershell -NoProfile -Command "[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()" 2^>nul') do set "START_SEC=%%T"

set "TESTS=DistanceTests SaveReliabilityTests PersistenceGuardTests DataManagementTests WellbeingTests PressDurationTests MotionTests KeyPaletteTests RangeStatsTests RangeReportTests ActivityTimeTests AppActivityTests AppFlowTests HourProfileTests RimePhraseTests StorageUsageTests TrendCutoffTests AxisScaleTests ShortcutTests StatisticsTests HourlyCompareTests KeyboardHeatTests ClickHeatTests LiveRateTests FocusMonitorTests FocusBreakTests FocusPillTests FocusBreakToastTests PeripheralWearTests PeripheralWearMultiTests MouseWearMultiTests KeyboardIdentityTests MouseIdentityTests HealthLayoutTests HwheelDisplayTests ClickScreenIdentityTests PopupLifetimeTests ReportVisualTests Report110Tests KeyRangeTests Release120Tests ThemeLoadingTests ThemeOwnershipTests WuxiaThemeTests OriThemeTests GujianThemeTests Release100Tests AnomalyRenderTests RimeLogTests AutoStartTests DailyReportTests HookWatchdogTests RawInputTests MonthlyStorageTests SegmentTrayTests DbBootTests DbSchemaTests RingBufferTests DbWriterTests EpochMigrationTests MigrationTests ClickChannelTests CategoryProviderTests ClickChannelUiTests WidgetHoverTests SampleModeTests LayoutMetricsTests FlowAnalyzerTests TimelineWindowTests PrivacyMaskTests WellbeingAutomationTests TimelineInteractionTests BattleReportTests KmsPackageTests LayoutFlowTests PeriodReportTests PeriodPosterTests ReportPosterTests TransitionGhostTests CustomThemeTests PageSkinTests IconPackTests"
if defined THEME_ONLY set "TESTS=ThemeLoadingTests WuxiaThemeTests OriThemeTests GujianThemeTests CustomThemeTests"
set "RENDERS=RenderDashboard DashboardAdditionsRender KeysHealthRender ClickHeatRender AppsRender TrendRender HoursRender KeysRender InsightsRender TextRender WidgetStorageRender ReportHabitRender ReportLiveRender CustomSkinRender PageIconRender"
set "BENCH=ThemePerf"

set "SELECTED_LOGIC="
for %%T in (%TESTS%) do (
    set "MATCH=0"
    if "!FILTER!"=="" (
        set "MATCH=1"
    ) else (
        echo %%T| findstr /I /C:"!FILTER!" >nul
        if not errorlevel 1 set "MATCH=1"
    )
    if "!MATCH!"=="1" set "SELECTED_LOGIC=!SELECTED_LOGIC! %%T"
)

set "SELECTED_RENDERS="
if not defined LOGIC_ONLY (
    for %%T in (%RENDERS%) do (
        set "MATCH=0"
        if "!FILTER!"=="" (
            set "MATCH=1"
        ) else (
            echo %%T| findstr /I /C:"!FILTER!" >nul
            if not errorlevel 1 set "MATCH=1"
        )
        if "!MATCH!"=="1" set "SELECTED_RENDERS=!SELECTED_RENDERS! %%T"
    )
)

set "RUN_BENCH="
if not defined LOGIC_ONLY (
    if "!FILTER!"=="" (
        set "RUN_BENCH=1"
    ) else (
        echo %BENCH%| findstr /I /C:"!FILTER!" >nul
        if not errorlevel 1 set "RUN_BENCH=1"
    )
)

if "!SELECTED_LOGIC!"=="" if "!SELECTED_RENDERS!"=="" if not defined RUN_BENCH (
    echo [ERROR] No test matched "!FILTER!".
    popd
    exit /b 1
)

> "%IVT%" echo // generated by test.bat
for %%F in (tests\*.cs) do >> "%IVT%" echo [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("%%~nF")]

> "%SIG%.new" powershell -NoProfile -Command "Get-ChildItem -File -Filter *.cs | Sort-Object Name | ForEach-Object { $_.Name + ';' + $_.LastWriteTimeUtc.Ticks + ';' + $_.Length }; Get-ChildItem tests\*.cs | Sort-Object Name | ForEach-Object { 'test-name:' + $_.BaseName }; $t=Get-Item test.bat; 'test.bat;' + $t.LastWriteTimeUtc.Ticks + ';' + $t.Length; $r=Get-Item theme-extra-resources.rsp; 'theme-extra-resources.rsp;' + $r.LastWriteTimeUtc.Ticks + ';' + $r.Length"
powershell -NoProfile -Command "Get-ChildItem tests\*.cs | Sort-Object Name | ForEach-Object { $_.BaseName + ';' + $_.LastWriteTimeUtc.Ticks + ';' + $_.Length }" > "%TESTSIG%"

set "NEED_CORE=0"
if not exist "%CORE%" set "NEED_CORE=1"
if not exist "%SIG%" set "NEED_CORE=1"
if "!NEED_CORE!"=="0" (
    fc /b "%SIG%" "%SIG%.new" >nul 2>&1
    if errorlevel 1 set "NEED_CORE=1"
)

if "!NEED_CORE!"=="1" (
    move /y "%SIG%.new" "%SIG%" >nul 2>&1
    echo [Core] compiling kms_test_core.dll ...
    "%CSC%" /nologo /target:library /platform:anycpu /optimize+ ^
        /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
        %RES% %RESNATIVE% ^
        /out:"%CORE%" %CORE_SRC% "%IVT%"
    if errorlevel 1 (
        del /q "%CORE%" 2>nul
        echo [COMPILE FAIL] kms_test_core.dll
        popd
        exit /b 1
    )
) else (
    del /q "%SIG%.new" 2>nul
    echo [Core] cache hit: kms_test_core.dll
)

set "CORE_TAG="
for /f "delims=" %%T in ('powershell -NoProfile -Command "$f=Get-Item '%CORE%'; $f.LastWriteTimeUtc.Ticks.ToString() + ';' + $f.Length" 2^>nul') do set "CORE_TAG=%%T"
set "DIAG_TAG="
for /f "tokens=2,3 delims=;" %%A in ('findstr /B /C:"TestDiagnostics;" "%TESTSIG%"') do set "DIAG_TAG=%%A;%%B"

set "REBUILD_LIST="
set "BUILD_COUNT=0"
set "ALL_SELECTED=!SELECTED_LOGIC! !SELECTED_RENDERS!"
if defined RUN_BENCH set "ALL_SELECTED=!ALL_SELECTED! %BENCH%"
for %%T in (!ALL_SELECTED!) do (
    set "NEED_BUILD="
    set "STAMP="
    set "FILE_TAG="
    for /f "tokens=2,3 delims=;" %%A in ('findstr /B /C:"%%T;" "%TESTSIG%"') do set "FILE_TAG=%%A;%%B"
    set "WANT=!CORE_TAG!;!FILE_TAG!;!DIAG_TAG!"
    if not exist "%OUT%\%%T.exe" set "NEED_BUILD=1"
    if exist "%OUT%\%%T.stamp" set /p STAMP=<"%OUT%\%%T.stamp"
    if /I not "!STAMP!"=="!WANT!" set "NEED_BUILD=1"
    if "!NEED_BUILD!"=="1" (
        set "REBUILD_LIST=!REBUILD_LIST! %%T"
        set /a BUILD_COUNT+=1
    )
)

> "%WORKER%" echo @echo off
>> "%WORKER%" echo "%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /main:%%~1 /out:"%OUT%\%%~1.exe" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%CORE%" "%TESTDIAG%" "tests\%%~1.cs" ^> "%OUT%\%%~1.compile.log" 2^>^&1
>> "%WORKER%" echo if errorlevel 1 (
>> "%WORKER%" echo   echo FAIL^> "%OUT%\%%~1.fail"
>> "%WORKER%" echo ^) else ^(
>> "%WORKER%" echo   echo %%~2^> "%OUT%\%%~1.stamp"
>> "%WORKER%" echo   echo OK^> "%OUT%\%%~1.ok"
>> "%WORKER%" echo ^)
>> "%WORKER%" echo del "%OUT%\%%~1.running" ^>nul 2^>^&1

if not "!REBUILD_LIST!"=="" (
    echo [Build] !BUILD_COUNT! test binaries, %JOBS% parallel jobs ...
    for %%T in (!REBUILD_LIST!) do (
        call :throttle
        set "FILE_TAG="
    for /f "tokens=2,3 delims=;" %%A in ('findstr /B /C:"%%T;" "%TESTSIG%"') do set "FILE_TAG=%%A;%%B"
        set "WANT=!CORE_TAG!;!FILE_TAG!;!DIAG_TAG!"
        echo running> "%OUT%\%%T.running"
        start "" /b cmd /c call "%WORKER%" "%%T" "!WANT!"
    )
    call :wait_jobs
)

set "COMPILE_FAIL=0"
for %%T in (!REBUILD_LIST!) do (
    if exist "%OUT%\%%T.fail" (
        echo [COMPILE FAIL] %%T
        type "%OUT%\%%T.compile.log"
        set "COMPILE_FAIL=1"
    )
)
if "!COMPILE_FAIL!"=="1" (
    echo ============ SOME TESTS FAILED TO COMPILE ============
    popd
    exit /b 1
)

for %%T in (!SELECTED_LOGIC!) do (
    if not exist "%OUT%\%%T.exe" (
        echo [MISSING] %%T
        set "FAIL=1"
    ) else (
        "%OUT%\%%T.exe"
        if errorlevel 1 (
            echo [RUN FAIL] %%T
            set "FAIL=1"
        ) else (
            echo [PASS] %%T
        )
    )
)

if not defined LOGIC_ONLY (
    for %%T in (!SELECTED_RENDERS!) do (
        if not exist "%OUT%\%%T.exe" (
            echo [MISSING] %%T
            set "FAIL=1"
        ) else (
            echo [Render] %%T
            "%OUT%\%%T.exe"
            if errorlevel 1 (
                echo [RUN FAIL] %%T
                set "FAIL=1"
            ) else (
                echo [PASS] %%T
            )
        )
    )
    if defined RUN_BENCH (
        if exist "%OUT%\%BENCH%.exe" (
            echo.
            echo [Bench] ThemePerf (keyboard heatmap frames, informational only)
            "%OUT%\%BENCH%.exe"
        )
    )
)

set "END_SEC="
for /f %%T in ('powershell -NoProfile -Command "[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()" 2^>nul') do set "END_SEC=%%T"
if defined START_SEC if defined END_SEC (
    set /a ELAPSED=END_SEC-START_SEC
    echo.
    echo Total elapsed: !ELAPSED!s
)

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
exit /b 0

:throttle
set "RUNNING=0"
for %%R in ("%OUT%\*.running") do set /a RUNNING+=1
if !RUNNING! GEQ %JOBS% (
    ping -n 2 127.0.0.1 >nul
    goto throttle
)
exit /b 0

:wait_jobs
set "RUNNING=0"
for %%R in ("%OUT%\*.running") do set /a RUNNING+=1
if !RUNNING! GTR 0 (
    ping -n 2 127.0.0.1 >nul
    goto wait_jobs
)
exit /b 0
