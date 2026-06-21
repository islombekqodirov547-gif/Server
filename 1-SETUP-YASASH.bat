@echo off
setlocal
chcp 65001 >nul
title JESKO Server - Setup yasash
REM ============================================================
REM  Bu fayl loyiha ILDIZIDA (StoreSystem.Api.csproj yonida) turadi.
REM  %~dp0 = shu faylning papkasi (oxirida \ bilan) = loyiha ildizi.
REM  Barcha yo'llar shunga nisbatan ABSOLYUT qilingan — qaysi
REM  papkadan ishga tushirilsa ham to'g'ri ishlaydi.
REM ============================================================
set "ROOT=%~dp0"
set "PROJ=%ROOT%StoreSystem.Api.csproj"
cd /d "%ROOT%"

echo ================================================================
echo    JESKO SERVER  -  bitta tugmada setup .exe yasash
echo ================================================================
echo.
echo  Bu fayl: 1) dasturni exe ga yigadi
echo           2) Inno Setup bilan SETUP .exe ni tayyorlaydi
echo.

REM ---------- 0. Loyiha fayli bormi? ----------
if not exist "%PROJ%" (
  echo [XATO] Loyiha fayli topilmadi:
  echo        %PROJ%
  echo  Iltimos bu .bat faylni arxivdan CHIQARIB, StoreSystem.Api.csproj
  echo  yonidagi (ildiz) papkadan ishga tushiring.
  echo.
  pause
  exit /b 1
)

REM ---------- 1. .NET 8 SDK tekshirish ----------
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [XATO] .NET 8 SDK topilmadi.
  echo.
  echo  Iltimos shuni ornating (bir marta):
  echo    https://dotnet.microsoft.com/download/dotnet/8.0
  echo  ".NET 8.0 SDK" ni tanlang (Runtime emas, SDK).
  echo  Ornatgach shu faylni qayta ishga tushiring.
  echo.
  pause
  exit /b 1
)

echo [1/2] Dastur exe ga yigilmoqda... (biroz vaqt oladi, kuting)
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%ROOT%installer\publish"
if errorlevel 1 (
  echo.
  echo [XATO] Yigishda xato yuz berdi. Yuqoridagi qizil matnni menga yuboring.
  pause
  exit /b 1
)
echo     OK: installer\publish\JESKO.Server.exe tayyor.
echo.

REM ---------- 2. Inno Setup bilan setup .exe ----------
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not defined ISCC (
  echo [DIQQAT] Inno Setup topilmadi - SETUP .exe yasalmadi.
  echo.
  echo  Dasturning OZI tayyor: installer\publish\JESKO.Server.exe
  echo.
  echo  SETUP .exe (1 ta ornatuvchi fayl) yasash uchun Inno Setup ornating:
  echo    https://jrsoftware.org/isdl.php
  echo  Ornatgach shu faylni QAYTA ishga tushiring - hammasi avtomatik bo'ladi.
  echo.
  pause
  exit /b 0
)

echo [2/2] SETUP .exe yasalmoqda (Inno Setup)...
"%ISCC%" "%ROOT%installer\StoreServer.iss"
if errorlevel 1 (
  echo [XATO] Inno Setup xato berdi. Yuqoridagi matnni menga yuboring.
  pause
  exit /b 1
)

echo.
echo ================================================================
echo    TAYYOR!  Sizning setup faylingiz:
echo    installer\Output\JESKO-Server-Setup.exe
echo ================================================================
echo  Shu BITTA faylni dokon (server) kompyuteriga olib borib ornating.
echo.
pause
