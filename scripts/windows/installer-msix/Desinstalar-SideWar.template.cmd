@echo off
setlocal
chcp 65001 >nul
set "SW_CMD=%~f0"
set "SW_PKG=@@PKG@@"
set "SW_THUMB=@@THUMB@@"
if /i "%~1"=="/cert" goto :cert
echo.
echo === Desinstalar SideWar (versao de teste) ===
echo.
echo ATENCAO: os saves e configuracoes da versao de teste serao apagados junto com o app.
echo Se nao quiser continuar, feche esta janela agora.
echo.
pause
echo.
echo Removendo o aplicativo...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = Get-AppxPackage -Name $env:SW_PKG; if ($p) { $p | Remove-AppxPackage; Write-Host 'Aplicativo removido.' } else { Write-Host 'O aplicativo ja nao esta instalado.' } } catch { Write-Host ('Nao foi possivel remover o aplicativo: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 echo AVISO: houve erro ao remover o aplicativo. Voce pode remover em Configuracoes ^> Aplicativos.
echo.
echo Agora vamos remover o certificado de teste. O Windows vai pedir permissao de administrador.
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath $env:ComSpec -ArgumentList '/c', ('\"' + $env:SW_CMD + '\" /cert') -Verb RunAs -Wait; exit 0 } catch { Write-Host 'Permissao de administrador negada; o certificado foi mantido.'; exit 1 }"
echo.
pause
exit /b
:cert
echo.
echo === Removendo o certificado de teste ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $c = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $env:SW_THUMB }; if ($c) { $c | Remove-Item; Write-Host 'Certificado removido.' } else { Write-Host 'O certificado ja nao estava instalado.' } } catch { Write-Host ('ERRO: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 ( echo. & echo ERRO: nao foi possivel remover o certificado. ) else ( echo. & echo CONCLUIDO. )
echo.
pause
exit /b
