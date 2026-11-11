@echo off
rem Offline Aprimorado - starts the server and the game. At login: "admin" = admin account; any other name creates a new account (not admin).
rem Offline aprimorado - inicia o servidor e o jogo. No login: "admin" = conta admin; qualquer outro nome cria uma conta nova (sem ser admin).
rem Keep this file in the same folder as DriftCity.exe and the Rice folder.
rem Copie este arquivo para a mesma pasta do DriftCity.exe e da pasta Rice.
rem Video settings: the game saves them in Data\Init\goption.ini. This .bat keeps a copy (goption_salvo.ini)
rem Configuracao de video: o jogo grava em Data\Init\goption.ini. Este .bat guarda uma copia (goption_salvo.ini)
rem when the game closes and puts it back before opening, so your settings do not reset.
rem quando o jogo fecha e coloca ela de volta antes de abrir, entao a sua configuracao nao volta para o padrao.
taskkill /F /IM Rice.exe
tasklist /FI "IMAGENAME eq DriftCity.exe" | find /I "DriftCity.exe" >nul && (echo Close the game from its menu before opening it again, otherwise the settings are not saved. / Feche o jogo pelo menu antes de abrir de novo. & pause & exit /b)
rem Windows with display scaling (125 or 150 percent): without this the game in high resolution (e.g. 1920x1080) gets zoomed
rem and cut (HUD, minimap and speedometer off the screen). Marks DriftCity.exe as "high DPI aware" for this user
rem (same as Properties > Compatibility > Change high DPI settings > Application), keeping other compatibility flags.
rem Windows com escala de tela (125 ou 150 por cento): sem isto o jogo em resolucao alta fica com zoom e cortado.
set "DPIKEY=HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"
set "DPIEXE=%~dp0DriftCity.exe"
set "DPILINE="
for /f "delims=" %%L in ('reg query "%DPIKEY%" /v "%DPIEXE%" 2^>nul ^| find /i "REG_SZ"') do set "DPILINE=%%L"
set "DPIFLAGS="
if defined DPILINE set "DPIFLAGS=%DPILINE:*REG_SZ=%"
if defined DPIFLAGS for /f "tokens=*" %%T in ("%DPIFLAGS%") do set "DPIFLAGS=%%T"
echo.%DPIFLAGS% | find /i "HIGHDPIAWARE" >nul && goto dpiok
if not defined DPIFLAGS set "DPIFLAGS=~"
reg add "%DPIKEY%" /v "%DPIEXE%" /t REG_SZ /d "%DPIFLAGS% HIGHDPIAWARE" /f >nul
:dpiok
set INI=Data\Init\goption.ini
set SAVE=Data\Init\goption_salvo.ini
if exist "%SAVE%" copy /Y "%SAVE%" "%INI%" >nul
rem saved fullscreen: opens without /ForceWindow; windowed: with /ForceWindow
rem tela cheia salva: abre sem /ForceWindow; em janela: com /ForceWindow
set WIN=/ForceWindow
rem ALWAYS FULLSCREEN: 1 = opens without /ForceWindow (fullscreen), 0 = follows the setting (Menu.bat > G > 2 Fullscreen)
rem TELA CHEIA SEMPRE: 1 = abre sem /ForceWindow (tela cheia), 0 = segue a configuracao (Menu.bat > G > 2 Tela cheia)
set TELACHEIA=0
findstr /I /R /C:"^Fullscreen *= *true" "%INI%" >nul && set WIN=
if "%TELACHEIA%"=="1" set WIN=
cd Rice
rem your racers' names and levels in the Battle Zone race (AI table of Init_us.agt) / nome e nivel dos seus corredores na corrida
Rice.exe ia
start Rice.exe
cd ..
start DriftCity.exe %WIN%
rem when the game closes, save the settings (runs hidden in the background)
rem quando o jogo fechar, guarda a configuracao (roda escondido em segundo plano)
rem it runs from the TEMP folder (with the full path of the files), so it does not keep the game folder "in use"
rem roda a partir da pasta TEMP (com o caminho completo dos arquivos), para nao deixar a pasta do jogo "em uso"
start "" /min /D "%TEMP%" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Start-Sleep -Seconds 20; Wait-Process -Name DriftCity -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2; Copy-Item -Force -LiteralPath '%~dp0%INI%' -Destination '%~dp0%SAVE%'"
