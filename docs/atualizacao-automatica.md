# Atualização automática (App Installer + Azure Blob)

`Gerar-MSIX.cmd` gera em `artifacts\msix\`:

- `SideWar-<versao>.msix`
- `SideWar.appinstaller` (nome fixo; aponta para o msix daquela versão e é validado pelo schema do Windows)
- `SideWar-<versao>-teste.zip` (kit para testadores; somente com `SIDEWAR_ZIP=1`)

## Pasta de distribuição

Ao fim de `publish-msix.ps1 -Mode SideLoad` (chamado pelo `Gerar-MSIX.cmd`), a pasta
`C:\dev\SideWar-Pacotes` (parâmetro `-DistributionPath`) fica com a versão atual:
`SideWar-<versao>.msix`, `SideWar.appinstaller`, `SideWar-Certificado.cer` (somente o certificado público) e `LEIA-ME.txt`.
Msix antigos, zips de kit, outros `.cer` e o antigo `Cert-Chave-Acesso.cmd` (bloqueado por
antivírus) são removidos dela. Envie essa pasta ao testador. O `.cer` precisa ir para
**Máquina Local > Pessoas Confiáveis**; "Usuário Atual" ou "Selecionar automaticamente" fazem o
MSIX não instalar (passo a passo no `LEIA-ME.txt`).

O `SideWar.appinstaller` baixa o msix do Blob: só funciona depois de a versão ser publicada.

Hospedagem: <https://taskbarwaraz.blob.core.windows.net/downloads/> (leitura anônima).
Link de instalação: <https://taskbarwaraz.blob.core.windows.net/downloads/SideWar.appinstaller>

Com a variável de ambiente `SIDEWAR_BLOB_SAS` definida, o próprio `Gerar-MSIX.cmd` publica os
dois arquivos (msix primeiro, appinstaller por último). Sem ela, o envio é pulado e o script
imprime o passo a passo manual.

**Atenção:** com a variável definida, toda geração é publicada e chega a todos os instalados.
Para gerar sem publicar, rode antes `set "SIDEWAR_BLOB_SAS="` no mesmo Prompt de Comando.

## Gerar o SAS (uma vez)

1. Portal do Azure > conta de armazenamento `taskbarwaraz` > Containers > `downloads`.
2. **Tokens de acesso compartilhado** (Shared access tokens). Gere no container, não na conta.
3. Permissões: somente **Create** e **Write**.
4. Protocolo: somente HTTPS. Validade longa (por exemplo, 2 anos).
5. **Gerar token SAS** e copie o campo **token** (começa com `sv=`), não a URL.

Se o token vazar, troque a chave da conta usada para assiná-lo (Chaves de acesso > Girar).
Isso revoga o SAS.

## Definir `SIDEWAR_BLOB_SAS` (uma vez, fora do repositório)

No Windows: Iniciar > "Editar as variáveis de ambiente para sua conta" > Novo >
nome `SIDEWAR_BLOB_SAS`, valor o token. Abra um novo terminal (ou novo Explorer) depois.

Prefira a janela acima a `setx`: o token contém `&` e ficaria no histórico do terminal.
Nunca grave o token em arquivo do repositório, log ou commit.

Quando o SAS vencer, o envio falha com `HTTP 403`: gere outro e atualize a variável.

## Instalação e atualização

- Quem instala precisa confiar no certificado de teste (`SideWar-Certificado.cer` em Máquina Local > Pessoas
  Confiáveis, uma vez).
- Só instalações feitas pelo `.appinstaller` se atualizam sozinhas. Quem instalou direto pelo
  `.msix` abre o `.appinstaller` uma vez, sem desinstalar antes (desinstalar apaga os saves).
- O Windows verifica ao abrir o jogo e em segundo plano; a versão nova vale na próxima abertura.
- `ForceUpdateFromAnyVersion` permite voltar versão: publique um `.appinstaller` apontando para
  um msix anterior que ainda esteja no container (mantenha os antigos lá).
