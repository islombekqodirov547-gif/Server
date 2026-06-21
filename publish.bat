@echo off
setlocal
chcp 65001 >nul
REM ============================================================
REM  JESKO Server — self-contained single-file publish
REM  Bu fayl "installer" papkasida turadi.
REM    %~dp0      = ...\installer\   (oxirida \ bilan)
REM    %~dp0..    = loyiha ildizi (StoreSystem.Api.csproj shu yerda)
REM  Loyiha fayli ANIQ ko'rsatilgan — MSB1003 (loyiha topilmadi) bo'lmaydi.
REM ============================================================
set "ROOT=%~dp0.."
set "PROJ=%ROOT%\StoreSystem.Api.csproj"

if not exist "%PROJ%" (
  echo [XATO] Loyiha fayli topilmadi: %PROJ%
  echo  publish.bat "installer" papkasida, csproj esa bir pog'ona yuqorida bo'lishi kerak.
  pause
  exit /b 1
)

echo JESKO Server publish qilinmoqda (win-x64, self-contained)...
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%~dp0publish"

if errorlevel 1 (
  echo.
  echo XATO: publish bajarilmadi. Yuqoridagi xabarni tekshiring.
  pause
  exit /b 1
)

echo.
echo Tayyor:  installer\publish\JESKO.Server.exe
echo Endi Inno Setup bilan installer\StoreServer.iss ni kompilyatsiya qiling
echo (yoki ildizdagi 1-SETUP-YASASH.bat hammasini avtomatik bajaradi).
pause
