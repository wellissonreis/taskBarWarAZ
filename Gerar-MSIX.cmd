@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
if "%SIDEWAR_CERT%"=="" set "SIDEWAR_CERT=C:\dev\sidewar-cert\sidewar-test.cer"
set "OUT=%~dp0artifacts\msix"
set "DIST=C:\dev\SideWar-Pacotes"
rem Zip para testadores desligado por padrao. Defina SIDEWAR_ZIP=1 para gerar.
rem Pacote de teste: pula build/testes por padrao (rapido). Defina SIDEWAR_CHECKS=1 para rodar build.ps1/test.ps1 antes.
set "CHECKARG=-SkipChecks"
if "%SIDEWAR_CHECKS%"=="1" set "CHECKARG="
echo.
echo === Gerando MSIX de teste do SideWar ===
echo Certificado: %SIDEWAR_CERT%
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\publish-msix.ps1" -Mode SideLoad -CertificatePath "%SIDEWAR_CERT%" %CHECKARG%
if errorlevel 1 goto :erro
if not "%SIDEWAR_ZIP%"=="1" goto :publicar
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\installer-msix\New-TesterKit.ps1" -MsixDir "%OUT%" -CertificatePath "%SIDEWAR_CERT%"
if errorlevel 1 goto :erro
:publicar
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\installer-msix\Publish-Blob.ps1" -MsixDir "%OUT%"
if errorlevel 1 goto :erro
echo.
echo SUCESSO. Pasta de distribuicao: %DIST%
if "%SIDEWAR_ZIP%"=="1" echo Zip para testadores: %OUT%\SideWar-*-teste.zip
if "%SIDEWAR_NOPAUSE%"=="" start "" explorer "%DIST%"
set "RC=0"
goto :fim
:erro
echo.
echo ERRO: a geracao falhou. Veja as mensagens acima.
set "RC=1"
:fim
if "%SIDEWAR_NOPAUSE%"=="" pause
exit /b %RC%
