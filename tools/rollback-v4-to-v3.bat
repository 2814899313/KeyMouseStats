@echo off
rem v4 -> v13 text rollback: export v4 stats.txt into a v3-readable v13 file.
rem Usage: tools\rollback-v4-to-v3.bat [v4-stats.txt] [out-v13.txt]
setlocal
pushd "%~dp0.."

set "SOURCE=%~1"
if "%SOURCE%"=="" set "SOURCE=%LOCALAPPDATA%\KeyMouseStats4\stats.txt"

set "OUTPUT=%~2"
if "%OUTPUT%"=="" (
    if not exist "artifacts\rollback" mkdir "artifacts\rollback"
    set "OUTPUT=artifacts\rollback\stats-v13.txt"
)

if not exist "%SOURCE%" (
    echo [rollback] v4 stats not found: %SOURCE%
    popd
    exit /b 1
)

echo [rollback] %SOURCE%  ->  %OUTPUT%
dotnet run --project src\Kms.Oracle\Kms.Oracle.csproj -- save "%SOURCE%" "%OUTPUT%"
if errorlevel 1 (
    echo [rollback] export failed
    popd
    exit /b 1
)

echo [rollback] done. Back up v3 stats.txt before replacing it.
popd
endlocal
