@echo off
chcp 949 >nul
setlocal
set "SRC=%~dp0CorridorSolids.bundle"
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\CorridorSolids.bundle"
echo.
echo  코리더 솔리드 일괄 추출 (CORSOLID) 설치
echo  ----------------------------------------
if not exist "%SRC%\PackageContents.xml" (
  echo  [오류] CorridorSolids.bundle 폴더를 찾을 수 없습니다.
  echo         압축을 모두 푼 뒤, 이 파일과 같은 폴더에서 실행하세요.
  pause
  exit /b 1
)
tasklist /FI "IMAGENAME eq acad.exe" 2>nul | find /I "acad.exe" >nul
if not errorlevel 1 (
  echo  [중지] AutoCAD/Civil 3D가 켜져 있습니다.
  echo         켜져 있는 동안에는 설치된 파일을 바꿀 수 없습니다.
  echo         Civil 3D 창을 모두 닫은 뒤 이 파일을 다시 실행하세요.
  echo.
  pause
  exit /b 1
)
echo  복사하는 중...
robocopy "%SRC%" "%DST%" /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo  [오류] 복사하지 못했습니다. AutoCAD/Civil 3D를 모두 끈 뒤 다시 실행하세요.
  pause
  exit /b 1
)
powershell -NoProfile -Command "Get-ChildItem -LiteralPath $env:DST -Recurse | Unblock-File" >nul 2>&1
echo  설치했습니다: %DST%
echo.
echo  Civil 3D 2026을 켜면 CORSOLID, CORLIST 명령을 쓸 수 있습니다.
echo  처음 켤 때 보안 경고가 나오면 [항상 로드]를 누르세요.
echo.
pause