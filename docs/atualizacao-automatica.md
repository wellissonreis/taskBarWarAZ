# Atualização automática (App Installer + Azure Blob)

`Gerar-MSIX.cmd` gera em `artifacts\msix\`:

- `SideWar-<versao>.msix`
- `SideWar.appinstaller` (nome fixo; aponta para o msix daquela versão e é validado pelo schema do Windows)
- `SideWar-teste.zip` (kit para testadores)

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

- Quem instala precisa confiar no certificado de teste (`Cert-Chave-Acesso.cmd`, uma vez).
- Só instalações feitas pelo `.appinstaller` se atualizam sozinhas. Quem instalou direto pelo
  `.msix` abre o `.appinstaller` uma vez, sem desinstalar antes (desinstalar apaga os saves).
- O Windows verifica ao abrir o jogo e em segundo plano; a versão nova vale na próxima abertura.
- `ForceUpdateFromAnyVersion` permite voltar versão: publique um `.appinstaller` apontando para
  um msix anterior que ainda esteja no container (mantenha os antigos lá).
