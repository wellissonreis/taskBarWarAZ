@echo off
rem Remove o SideWar (pasta de instalacao, atalhos; pergunta sobre os dados).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Uninstall
