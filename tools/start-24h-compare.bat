@echo off
rem Start a 24h v3/v4 side-by-side count comparison.
rem Usage: tools\start-24h-compare.bat
rem Stop: close both tray icons normally; do not kill while saving.
setlocal
pushd "%~dp0.."

if not exist "KeyMouseStats.exe" (
    echo [compare] v3 KeyMouseStats.exe not found. Run build.bat first.
    popd
    exit /b 1
)
set "V4=artifacts\publish-v4\Kms.App.exe"
if not exist "%V4%" set "V4=artifacts\publish-next\Kms.App.exe"
if not exist "%V4%" (
    echo [compare] v4 exe not found. Run publish-v4.bat or publish-dual.bat.
    popd
    exit /b 1
)

echo [compare] start v3
start "" "KeyMouseStats.exe"
timeout /t 2 /nobreak >nul
echo [compare] start v4
start "" "%V4%"

echo [compare] started. Keep both running for at least 24h.
echo [compare] Then follow docs\v4-acceptance.md section 1 to dump and diff.
popd
endlocal
