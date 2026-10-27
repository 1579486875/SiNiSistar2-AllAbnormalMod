@echo off
chcp 936 >nul 2>&1
cd /d "%~dp0"

echo ============================================================
echo   SiNiSistar 2  诊断信息收集
echo ============================================================
echo.
echo   把排查需要的几个文件打包成一个 zip，放在当前文件夹。
echo   不上传任何东西，也不改动游戏。
echo.
echo   用法：本文件放在 SiNiSistar2.exe 同一层，双击运行。
echo.
pause
echo.

if not exist "SiNiSistar2.exe" goto NODIR

set OUT=诊断包.zip
if exist "%OUT%" del /q "%OUT%"

echo === 根目录文件清单 === > 文件清单.txt
dir /-c /a >> 文件清单.txt
echo. >> 文件清单.txt
echo === 关键目录 === >> 文件清单.txt
if exist Mods dir Mods >> 文件清单.txt
if exist "MelonLoader\Logs" dir "MelonLoader\Logs" >> 文件清单.txt
if exist UserData dir UserData >> 文件清单.txt
echo. >> 文件清单.txt
echo === build_info.txt === >> 文件清单.txt
if exist build_info.txt type build_info.txt >> 文件清单.txt
echo. >> 文件清单.txt
echo === 系统信息 === >> 文件清单.txt
ver >> 文件清单.txt

echo 正在打包，请稍候...
echo.

set FILES=文件清单.txt
if exist build_info.txt set FILES=%FILES% build_info.txt
if exist "MelonLoader\Logs" set FILES=%FILES% "MelonLoader\Logs"
if exist "Mods" set FILES=%FILES% "Mods"
if exist "UserData" set FILES=%FILES% "UserData"
if exist "MelonLoader\Il2CppAssemblies\Il2CppSiNiSistar2.dll" set FILES=%FILES% "MelonLoader\Il2CppAssemblies\Il2CppSiNiSistar2.dll"
if exist "MelonLoader\net6\MelonLoader.dll" set FILES=%FILES% "MelonLoader\net6\MelonLoader.dll"
if exist install.ps1 set FILES=%FILES% install.ps1

tar -a -c -f "%OUT%" %FILES%
if not exist "%OUT%" goto TARFAIL

echo.
echo ============================================================
echo   完成！
echo     %CD%\%OUT%
echo.
echo   把这个 zip 发给作者就行。
echo ============================================================
goto END

:NODIR
echo.
echo [错误] 这里不是游戏目录 - 没找到 SiNiSistar2.exe
echo        请把本文件放到游戏目录里再运行。
goto END

:TARFAIL
echo.
echo [失败] 没能生成 zip。请手动打包下面这些发我：
echo     build_info.txt
echo     MelonLoader\Logs\   整个文件夹
echo     Mods\               整个文件夹
echo     MelonLoader\Il2CppAssemblies\Il2CppSiNiSistar2.dll
goto END

:END
echo.
pause
