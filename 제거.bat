@echo off
chcp 949 >nul
setlocal
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\CorridorSolids.bundle"
if not exist "%DST%" (
  echo  설치되어 있지 않습니다.
  pause
  exit /b 0
)
tasklist /FI "IMAGENAME eq acad.exe" 2>nul | find /I "acad.exe" >nul
if not errorlevel 1 (
  echo  [중지] AutoCAD/Civil 3D가 켜져 있습니다. 모두 닫은 뒤 다시 실행하세요.
  pause
  exit /b 1
)
rmdir /s /q "%DST%"
if exist "%DST%" (
  echo  [오류] 지우지 못했습니다. AutoCAD/Civil 3D를 모두 끈 뒤 다시 실행하세요.
) else (
  echo  제거했습니다.
)
pause