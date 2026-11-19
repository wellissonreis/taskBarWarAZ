@echo off
setlocal
chcp 65001 >nul
set "DIR=%~dp0"
set "CER=%DIR%sidewar-test.cer"
set "MSIX=%DIR%SideWar.msix"
if /i "%~1"=="cert" goto :cert

echo.
echo === Instalador de teste do SideWar ===
echo.
if not exist "%MSIX%" ( echo ERRO: SideWar.msix nao encontrado ao lado deste arquivo. & goto :fim )
if not exist "%CER%" ( echo ERRO: sidewar-test.cer nao encontrado ao lado deste arquivo. & goto :fim )

echo Passo 1/2: confiar no certificado de teste (o Windows vai pedir permissao de administrador).
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = Start-Process -FilePath $env:ComSpec -ArgumentList '/c','\"%~f0\" cert' -Verb RunAs -Wait -PassThru; exit $p.ExitCode } catch { exit 1 }"
if errorlevel 1 ( echo ERRO: nao foi possivel importar o certificado ^(permissao negada?^). & goto :fim )

echo.
echo Passo 2/2: instalando o SideWar...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Add-AppxPackage -Path '%MSIX%'; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 ( echo. & echo ERRO: a instalacao falhou. Veja a mensagem acima. & goto :fim )
echo.
echo SUCESSO: SideWar instalado. Procure "SideWar" no menu Iniciar.
goto :fim

:cert
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Import-Certificate -FilePath '%CER%' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
exit /b %errorlevel%

:fim
echo.
pause
