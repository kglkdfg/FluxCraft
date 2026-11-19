@echo off
setlocal
pushd "%~dp0"

echo ========================================
echo          FluxCraft - Build
echo ========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: .NET SDK was not found.
    echo Install the .NET 8 SDK:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    popd
    exit /b 1
)

echo Restoring and building FluxCraft WPF...
dotnet build "src\FluxCraft.App\FluxCraft.App.csproj" --configuration Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED. See the errors above.
    pause
    popd
    exit /b 1
)

set "OUTPUT_DIR=%~dp0src\FluxCraft.App\bin\Release\net8.0-windows"
if not exist "%OUTPUT_DIR%" (
    echo.
    echo ERROR: Build succeeded, but output folder was not found:
    echo "%OUTPUT_DIR%"
    pause
    popd
    exit /b 1
)

echo.
echo BUILD SUCCEEDED.
echo Opening build output folder...
start "" explorer.exe "%OUTPUT_DIR%"

popd
exit /b 0
