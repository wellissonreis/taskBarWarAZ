@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
if "%SIDEWAR_CERT%"=="" set "SIDEWAR_CERT=C:\dev\sidewar-cert\sidewar-test.cer"
set "OUT=%~dp0artifacts\msix"
rem Pacote de teste: pula build/testes por padrao (rapido). Defina SIDEWAR_CHECKS=1 para rodar build.ps1/test.ps1 antes.
set "CHECKARG=-SkipChecks"
if "%SIDEWAR_CHECKS%"=="1" set "CHECKARG="
echo.
echo === Gerando MSIX de teste do SideWar ===
echo Certificado: %SIDEWAR_CERT%
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\publish-msix.ps1" -Mode SideLoad -CertificatePath "%SIDEWAR_CERT%" %CHECKARG%
if errorlevel 1 goto :erro
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\installer-msix\New-TesterKit.ps1" -MsixDir "%OUT%" -CertificatePath "%SIDEWAR_CERT%"
if errorlevel 1 goto :erro
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\installer-msix\Publish-Blob.ps1" -MsixDir "%OUT%"
if errorlevel 1 goto :erro
echo.
echo SUCESSO. Kit para testadores: %OUT%\SideWar-teste.zip
if "%SIDEWAR_NOPAUSE%"=="" start "" explorer "%OUT%"
set "RC=0"
goto :fim
:erro
echo.
echo ERRO: a geracao falhou. Veja as mensagens acima.
set "RC=1"
:fim
if "%SIDEWAR_NOPAUSE%"=="" pause
exit /b %RC%
