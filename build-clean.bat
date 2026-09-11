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
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /define:CLEAN_EDITION /out:"%~dp0KeyMouseStats.Clean.exe" "%~dp0KeyMouseWidget.cs" "%~dp0Dashboard.cs" "%~dp0MouseDistance.cs" "%~dp0AppActivity.cs" "%~dp0AppPanel.cs" "%~dp0ActivityTime.cs" "%~dp0ShortcutStats.cs" "%~dp0Insights.cs" "%~dp0Themes.cs" "%~dp0HourlyTrend.cs" "%~dp0ReportDesign.cs" "%~dp0ReportHabits.cs" "%~dp0StatisticsReport.cs" "%~dp0AppTelemetry.cs" "%~dp0KeyboardHeatmap.cs" "%~dp0NikkiTheme.cs" "%~dp0HaloTheme.cs" "%~dp0ResidentTheme.cs" "%~dp0GameHud.cs" "%~dp0MinecraftTheme.cs" "%~dp0ReleaseAnalytics.cs" "%~dp0ReleaseReport.cs" "%~dp0WuxiaTheme.cs" "%~dp0EuroTruckTheme.cs" "%~dp0WeeklyReview.cs" "%~dp0RangeAnalysis.cs" "%~dp0RangeReport.cs" "%~dp0DataManagement.cs" "%~dp0DataSettingsDialog.cs" "%~dp0Wellbeing.cs" "%~dp0WellbeingSettingsDialog.cs" "%~dp0Version.cs" "%~dp0Motion.cs" "%~dp0PressDuration.cs" "%~dp0PressReport.cs" "%~dp0HoldTrendReport.cs" "%~dp0AssemblyInfo.cs"

if errorlevel 1 (
    echo BUILD FAILED
    echo If the file is reported as "in use by another process", quit the running KeyMouseStats.Clean.exe and retry.
    exit /b 1
)
echo BUILD OK: KeyMouseStats.Clean.exe
endlocal
