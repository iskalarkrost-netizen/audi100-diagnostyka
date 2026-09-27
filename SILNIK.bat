@echo off
REM ============================================================
REM  Audi 100 C4 2.6 ABC - diagnostyka silnika
REM  Kluczowa poprawka: KW1281_R6=0 (potwierdzenie bez opoznienia)
REM  Sterownik silnika wymaga zerowej zwloki, inaczej sie zacina.
REM ============================================================
setlocal
set KW1281_R6=0
set PORT=COM1
set BAUD=4800
set ADRES=1

echo.
echo  1 - Identyfikacja silnika
echo  2 - Odczyt bledow
echo  3 - Kasowanie bledow
echo  4 - Grupy pomiarowe na zywo (grupa 1, Q konczy)
echo  5 - AutoScan calego auta
echo.
set /p WYBOR="Wybierz [1-5]: "

if "%WYBOR%"=="1" "%~dp0kw1281test-FIXED.exe" %PORT% %BAUD% %ADRES% ReadIdent
if "%WYBOR%"=="2" "%~dp0kw1281test-FIXED.exe" %PORT% %BAUD% %ADRES% ReadFaultCodes
if "%WYBOR%"=="3" "%~dp0kw1281test-FIXED.exe" %PORT% %BAUD% %ADRES% ClearFaultCodes
if "%WYBOR%"=="4" "%~dp0kw1281test-FIXED.exe" %PORT% %BAUD% %ADRES% GroupRead 1
if "%WYBOR%"=="5" "%~dp0kw1281test-FIXED.exe" %PORT% %BAUD% %ADRES% AutoScan
echo.
pause
