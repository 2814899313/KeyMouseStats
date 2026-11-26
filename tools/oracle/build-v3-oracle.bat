@echo off
rem 构建 v3 参考实现 Oracle（只在本机开发/对拍时使用，不参与 v4 CI）。
rem 依赖 test.bat 生成的 %TEMP%\kms_test_cache\kms_test_core.dll 与 InternalsVisibleTo("OracleDump")。
setlocal
pushd "%~dp0\..\.."

call test.bat --logic-only
if errorlevel 1 (
    popd
    exit /b 1
)

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ ^
    /out:"%TEMP%\OracleDump.exe" ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /r:"%TEMP%\kms_test_cache\kms_test_core.dll" ^
    "tests\OracleDump.cs"
if errorlevel 1 (
    popd
    exit /b 1
)

copy /y "%TEMP%\kms_test_cache\kms_test_core.dll" "%TEMP%" >nul
echo Built %TEMP%\OracleDump.exe
popd
endlocal
