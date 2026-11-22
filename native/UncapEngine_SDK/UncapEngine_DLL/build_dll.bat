@echo off
setlocal
echo ===================================================
echo Compiling Standalone UncapEngine.dll (x64 Release)
echo ===================================================

set VCVARS="C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if exist %VCVARS% goto msvc

where g++ >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Neither Visual Studio 2022 vcvars64.bat nor g++ was found.
    echo         Install VS 2022 Build Tools or WinLibs/MinGW and retry.
    pause
    exit /b 1
)

cd /d "%~dp0"
if not exist "..\payload" mkdir "..\payload"
echo [MinGW] Compiling with g++...
g++ -std=c++20 -O2 -shared -static -DUNICODE -D_UNICODE -DNOMINMAX -DWIN32_LEAN_AND_MEAN -Wno-unknown-pragmas -o "..\payload\UncapEngine.dll" UncapEngine.cpp -ld3d11 -ldxgi -ldxguid -luser32 -lgdi32 -lole32 -luuid
if errorlevel 1 (
    echo.
    echo [ERROR] Compilation failed with error code %ERRORLEVEL%
    pause
    exit /b 1
)
echo.
echo [SUCCESS] UncapEngine.dll compiled successfully!
echo Output: %~dp0..\payload\UncapEngine.dll
goto done

:msvc
call %VCVARS%
cd /d "%~dp0"
if not exist "..\payload" mkdir "..\payload"

echo [MSVC] Compiling with cl.exe...
cl /nologo /std:c++20 /O2 /EHsc /MT /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /D_CRT_SECURE_NO_WARNINGS /W3 /Fo"..\payload\UncapEngine.obj" /Fe"..\payload\UncapEngine.dll" UncapEngine.cpp /link /DLL d3d11.lib dxgi.lib dxguid.lib user32.lib gdi32.lib kernel32.lib

if %ERRORLEVEL% equ 0 (
    echo.
    echo [SUCCESS] UncapEngine.dll compiled successfully!
    echo Output: %~dp0..\payload\UncapEngine.dll
) else (
    echo.
    echo [ERROR] Compilation failed with error code %ERRORLEVEL%
    pause
    exit /b 1
)

:done
endlocal
