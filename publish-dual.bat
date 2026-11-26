@echo off
rem Dual-track publish: v4 self-contained single-file + v3 .NET Framework exe.
rem Outputs: artifacts\publish-v4\Kms.App.exe and artifacts\publish-v3\KeyMouseStats.exe
rem Does not touch artifacts\publish (the currently running v4/v3 process may lock it).
setlocal
pushd "%~dp0"

echo [dual] v4 -> artifacts\publish-v4
dotnet publish "src\Kms.App\Kms.App.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -o "artifacts\publish-v4"
if errorlevel 1 (
    echo [dual] v4 publish failed
    popd
    exit /b 1
)
del /q "artifacts\publish-v4\*.pdb" 2>nul

echo [dual] v3 -> build.bat
call build.bat
if errorlevel 1 (
    echo [dual] v3 build skipped/failed; close KeyMouseStats.exe and retry
    popd
    exit /b 1
)
if not exist "artifacts\publish-v3" mkdir "artifacts\publish-v3"
copy /y "KeyMouseStats.exe" "artifacts\publish-v3\KeyMouseStats.exe" >nul
if errorlevel 1 (
    echo [dual] v3 copy failed
    popd
    exit /b 1
)

echo [dual] done
for %%F in ("artifacts\publish-v4\Kms.App.exe") do echo   v4: %%~fF  %%~zF bytes
for %%F in ("artifacts\publish-v3\KeyMouseStats.exe") do echo   v3: %%~fF  %%~zF bytes
popd
endlocal
