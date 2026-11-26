@echo off
rem 重新生成 tests/Fixtures/expected 下的 v3 期望值。先运行 build-v3-oracle.bat。
setlocal
pushd "%~dp0\..\.."

if not exist "%TEMP%\OracleDump.exe" (
    echo ERROR: 先运行 tools\oracle\build-v3-oracle.bat
    popd
    exit /b 1
)

if not exist "tests\Fixtures\expected" mkdir "tests\Fixtures\expected"

for %%F in (tests\Fixtures\v13-*.txt) do (
    echo [Oracle] %%~nxF
    "%TEMP%\OracleDump.exe" "%%F" "tests\Fixtures\expected\%%~nF.dump"
    if errorlevel 1 (
        popd
        exit /b 1
    )
)

echo Done.
popd
endlocal
