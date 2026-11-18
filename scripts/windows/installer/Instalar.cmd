@echo off
rem Instala o SideWar para o usuario atual (sem admin).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
