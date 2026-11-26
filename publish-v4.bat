@echo off
rem v4 M7: self-contained single-file publish (win-x64). Output: artifacts\publish.
setlocal
pushd "%~dp0"

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
    -o "artifacts\publish"
if errorlevel 1 (
    popd
    exit /b 1
)

for %%F in ("artifacts\publish\Kms.App.exe") do (
    echo Published: %%~fF  %%~zF bytes
)
del /q "artifacts\publish\*.pdb" 2>nul
popd
endlocal
