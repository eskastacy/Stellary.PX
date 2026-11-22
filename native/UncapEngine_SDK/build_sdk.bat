@echo off
setlocal enabledelayedexpansion
echo ==========================================================
echo Compiling UNCAP ENGINE SDK (DLL + Example App) [x64 Release]
echo ==========================================================

set VCVARS="C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if not exist %VCVARS% (
    echo [ERROR] Visual Studio 2022 vcvars64.bat not found at %VCVARS%
    pause
    exit /b 1
)

call %VCVARS%
cd /d "%~dp0"

if not exist payload mkdir payload

echo.
echo [1/2] Compiling UncapEngine.dll...
cl /nologo /std:c++20 /O2 /EHsc /MT /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /D_CRT_SECURE_NO_WARNINGS /W3 /Fo"payload\UncapEngine.obj" /Fe"payload\UncapEngine.dll" "UncapEngine_DLL\UncapEngine.cpp" /link /DLL d3d11.lib dxgi.lib dxguid.lib user32.lib gdi32.lib kernel32.lib
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to compile UncapEngine.dll!
    pause
    exit /b 1
)

echo.
echo [2/2] Compiling Example_Main.exe with UncapInstaller.cpp...
cl /nologo /std:c++20 /O2 /EHsc /MT /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /D_CRT_SECURE_NO_WARNINGS /W3 /I. /Fo"payload\\" /Fe"payload\Example_Main.exe" Example_Main.cpp "UncapInstaller\UncapInstaller.cpp" /link advapi32.lib user32.lib kernel32.lib
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to compile Example_Main.exe!
    pause
    exit /b 1
)

copy /Y "UncapEngine_DLL\UncapEngine.ini" "payload\UncapEngine.ini" >nul

echo.
echo ==========================================================
echo [SUCCESS] Build completed!
echo Binaries located in: %~dp0payload
echo   - payload\UncapEngine.dll
echo   - payload\UncapEngine.ini
echo   - payload\Example_Main.exe
echo ==========================================================
pause
endlocal
