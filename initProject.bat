@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

REM ============================================
REM  Unity Project Structure Generator
REM  Батник должен лежать в КОРНЕ проекта.
REM ============================================

REM Корень = папка, где лежит сам .bat файл
set "ROOT=%~dp0"
REM Убираем завершающий слеш
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo Создание структуры Unity проекта в: %ROOT%
echo.

REM ---------- Assets ----------
call :makeDir "%ROOT%\Assets"
call :makeDir "%ROOT%\Assets\Scenes"
call :makeDir "%ROOT%\Assets\Scripts"
call :makeDir "%ROOT%\Assets\Scripts\Core"
call :makeDir "%ROOT%\Assets\Scripts\Gameplay"
call :makeDir "%ROOT%\Assets\Scripts\UI"
call :makeDir "%ROOT%\Assets\Scripts\Data"
call :makeDir "%ROOT%\Assets\Scripts\Utils"
call :makeDir "%ROOT%\Assets\Prefabs"
call :makeDir "%ROOT%\Assets\Materials"
call :makeDir "%ROOT%\Assets\Textures"
call :makeDir "%ROOT%\Assets\Sprites"
call :makeDir "%ROOT%\Assets\Audio"
call :makeDir "%ROOT%\Assets\Audio\Music"
call :makeDir "%ROOT%\Assets\Audio\SFX"
call :makeDir "%ROOT%\Assets\Animations"
call :makeDir "%ROOT%\Assets\Animators"
call :makeDir "%ROOT%\Assets\Shaders"
call :makeDir "%ROOT%\Assets\Fonts"
call :makeDir "%ROOT%\Assets\Resources"
call :makeDir "%ROOT%\Assets\Editor"
call :makeDir "%ROOT%\Assets\Plugins"
call :makeDir "%ROOT%\Assets\StreamingAssets"
call :makeDir "%ROOT%\Assets\Settings"
call :makeDir "%ROOT%\Assets\Models"
call :makeDir "%ROOT%\Assets\ThirdParty"

REM ---------- Служебные папки Unity ----------
if not exist "%ROOT%\ProjectSettings" mkdir "%ROOT%\ProjectSettings"
if not exist "%ROOT%\Packages"        mkdir "%ROOT%\Packages"
if not exist "%ROOT%\Library"         mkdir "%ROOT%\Library"
if not exist "%ROOT%\Logs"            mkdir "%ROOT%\Logs"
if not exist "%ROOT%\UserSettings"    mkdir "%ROOT%\UserSettings"
if not exist "%ROOT%\Builds"          mkdir "%ROOT%\Builds"
if not exist "%ROOT%\Temp"            mkdir "%ROOT%\Temp"
if not exist "%ROOT%\Obj"             mkdir "%ROOT%\Obj"

REM ---------- Packages/manifest.json ----------
if not exist "%ROOT%\Packages\manifest.json" (
    (
        echo {
        echo   "dependencies": {
        echo     "com.unity.collab-proxy": "2.4.3",
        echo     "com.unity.ide.rider": "3.0.31",
        echo     "com.unity.ide.visualstudio": "2.0.22",
        echo     "com.unity.test-framework": "1.4.5",
        echo     "com.unity.textmeshpro": "3.0.9",
        echo     "com.unity.timeline": "1.8.7",
        echo     "com.unity.ugui": "2.0.0",
        echo     "com.unity.modules.ai": "1.0.0",
        echo     "com.unity.modules.androidjni": "1.0.0",
        echo     "com.unity.modules.animation": "1.0.0",
        echo     "com.unity.modules.assetbundle": "1.0.0",
        echo     "com.unity.modules.audio": "1.0.0",
        echo     "com.unity.modules.cloth": "1.0.0",
        echo     "com.unity.modules.director": "1.0.0",
        echo     "com.unity.modules.imageconversion": "1.0.0",
        echo     "com.unity.modules.imgui": "1.0.0",
        echo     "com.unity.modules.jsonserialize": "1.0.0",
        echo     "com.unity.modules.particlesystem": "1.0.0",
        echo     "com.unity.modules.physics": "1.0.0",
        echo     "com.unity.modules.physics2d": "1.0.0",
        echo     "com.unity.modules.screencapture": "1.0.0",
        echo     "com.unity.modules.terrain": "1.0.0",
        echo     "com.unity.modules.terrainphysics": "1.0.0",
        echo     "com.unity.modules.tilemap": "1.0.0",
        echo     "com.unity.modules.ui": "1.0.0",
        echo     "com.unity.modules.uielements": "1.0.0",
        echo     "com.unity.modules.umbra": "1.0.0",
        echo     "com.unity.modules.unityanalytics": "1.0.0",
        echo     "com.unity.modules.unitywebrequest": "1.0.0",
        echo     "com.unity.modules.unitywebrequestassetbundle": "1.0.0",
        echo     "com.unity.modules.unitywebrequestaudio": "1.0.0",
        echo     "com.unity.modules.unitywebrequesttexture": "1.0.0",
        echo     "com.unity.modules.unitywebrequestwww": "1.0.0",
        echo     "com.unity.modules.vehicles": "1.0.0",
        echo     "com.unity.modules.video": "1.0.0",
        echo     "com.unity.modules.vr": "1.0.0",
        echo     "com.unity.modules.wind": "1.0.0",
        echo     "com.unity.modules.xr": "1.0.0"
        echo   }
        echo }
    ) > "%ROOT%\Packages\manifest.json"
    echo [OK] Packages\manifest.json
)

REM ---------- ProjectSettings/ProjectVersion.txt ----------
if not exist "%ROOT%\ProjectSettings\ProjectVersion.txt" (
    (
        echo m_EditorVersion: 6000.0.23f1
        echo m_EditorVersionWithRevision: 6000.0.23f1 ^(1c4764c07fb4^)
    ) > "%ROOT%\ProjectSettings\ProjectVersion.txt"
    echo [OK] ProjectSettings\ProjectVersion.txt
)

REM ---------- .gitignore ----------
if not exist "%ROOT%\.gitignore" (
    (
        echo # Unity generated
        echo [Ll]ibrary/
        echo [Tt]emp/
        echo [Oo]bj/
        echo [Bb]uild/
        echo [Bb]uilds/
        echo [Ll]ogs/
        echo [Uu]ser[Ss]ettings/
        echo.
        echo # Autogenerated VS/MD/Consulo solution and project files
        echo ExportedObj/
        echo .consulo/
        echo *.csproj
        echo *.unityproj
        echo *.sln
        echo *.suo
        echo *.tmp
        echo *.user
        echo *.userprefs
        echo *.pidb
        echo *.booproj
        echo *.svd
        echo *.pdb
        echo *.mdb
        echo *.opendb
        echo *.VC.db
        echo.
        echo # Unity3D generated meta files
        echo *.pidb.meta
        echo *.pdb.meta
        echo *.mdb.meta
        echo.
        echo # Unity3D generated file on crash reports
        echo sysinfo.txt
        echo.
        echo # Builds
        echo *.apk
        echo *.aab
        echo *.unitypackage
        echo.
        echo # Crashlytics
        echo crashlytics-build.properties
        echo.
        echo # OS
        echo .DS_Store
        echo Thumbs.db
        echo.
        echo # Batch helper - remove if you want to commit it
        echo *.bat
    ) > "%ROOT%\.gitignore"
    echo [OK] .gitignore
)

echo.
echo ============================================
echo  Готово! Структура создана в: %ROOT%
echo ============================================
echo.
pause
endlocal
exit /b 0

REM ============================================
REM  Функция: создать папку + .gitkeep
REM ============================================
:makeDir
set "DIR=%~1"
if not exist "%DIR%" mkdir "%DIR%"

if not exist "%DIR%\.gitkeep" (
    type nul > "%DIR%\.gitkeep"
    echo [OK] %DIR%
) else (
    echo [SKIP] %DIR%
)
exit /b 0