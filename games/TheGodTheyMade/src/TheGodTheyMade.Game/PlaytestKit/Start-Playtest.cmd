@echo off
setlocal
chcp 65001 >nul
title The God They Made - Blind Playtest
echo 《神意难测》鸣钟谷外部盲测
echo.
echo 请准备好主持人分配的测试编号，然后按提示输入。
echo 游戏退出后，证据文件会保存在 PlaytestData 文件夹。
echo.
"%~dp0TheGodTheyMade.exe" --playtest-prompt
set "GAME_EXIT=%ERRORLEVEL%"
echo.
if not "%GAME_EXIT%"=="0" (
  echo 会话未正常结束，退出码：%GAME_EXIT%
) else (
  echo 会话已保存。请把整个 PlaytestData 文件夹交给主持人。
)
pause
exit /b %GAME_EXIT%
