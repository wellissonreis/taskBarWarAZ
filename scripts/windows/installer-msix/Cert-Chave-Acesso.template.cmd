@echo off
setlocal
chcp 65001 >nul
echo.
echo === SideWar de teste: confiar no certificado ===
echo.
net session >nul 2>&1
if errorlevel 1 (
  echo O Windows vai pedir permissao de administrador. Aceite para continuar.
  set "SW_CMD=%~f0"
  powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath $env:ComSpec -ArgumentList '/c', ('\"' + $env:SW_CMD + '\"') -Verb RunAs; exit 0 } catch { Write-Host 'Permissao negada.'; exit 1 }"
  if errorlevel 1 ( echo ERRO: sem permissao de administrador. & echo. & pause )
  exit /b
)
set "SW_CMD=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $b = (Get-Content -LiteralPath $env:SW_CMD | Where-Object { $_.StartsWith('::CERT:') } | ForEach-Object { $_.Substring(7).Trim() }) -join ''; $c = [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($b)); $s = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople','LocalMachine'); $s.Open('ReadWrite'); $s.Add($c); $s.Close(); Write-Host ('Certificado ' + $c.Subject + ' importado.'); exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 (
  echo.
  echo ERRO: nao foi possivel importar o certificado.
) else (
  echo.
  echo SUCESSO: certificado confiavel. Agora de duplo clique em SideWar.msix para instalar.
)
echo.
pause
exit /b
::CERT:@@CERT@@
