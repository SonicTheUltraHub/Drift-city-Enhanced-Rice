@echo off
rem Offline Aprimorado menu: saves (accounts and racers), bots, crews, settings, events, mods, language and backup.
rem Menu do Offline Aprimorado: saves (contas e corredores), bots, crews, configuracoes e backup.
rem It stays in the game folder (next to DriftCity.exe and the Rice folder).
rem Fica na pasta do jogo (do lado do DriftCity.exe e da pasta Rice).
rem Close the server (Rice.exe) before changing anything.
rem Feche o servidor (Rice.exe) antes de mudar alguma coisa.
if exist "%~dp0Rice.exe" (cd /d "%~dp0") else (cd /d "%~dp0Rice")
if not exist Rice.exe (echo Rice.exe not found. Put Menu.bat in the game folder. / Nao achei o Rice.exe - o Menu.bat fica na pasta do jogo. & pause & exit /b)
Rice.exe menu
