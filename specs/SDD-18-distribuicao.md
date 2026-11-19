# SDD-18: Distribuição da aplicação

**Status:** Rascunho para revisão · **Etapa:** E8 (o ZIP interno já é usável na E5 para playtest) · **Depende de:** SDD-01, 02, 07, 14, 16 · **Bloqueia:** release

## 1. Identificador e título

SDD-18: Versionamento, publicação self-contained, distribuição pela Microsoft Store em MSIX, ZIP para testes, instalação, remoção e requisitos mínimos.

## 2. Objetivo

Entregar o SideWar Desktop para Windows limpo, sem .NET instalado, pela Microsoft Store como canal principal para usuários finais e por ZIP para testes. O aplicativo não faz chamadas de rede; a Store pode usar rede para instalação e atualização. Todas as metas numéricas desta ficha são **não comprovadas** até medição (SDD-16).

## 3. Contexto técnico

- ADR-008: sem trimming e sem Native AOT (o WPF não suporta). A publicação é **self-contained com ReadyToRun**.
- `libSkiaSharp.dll` é uma biblioteca nativa. No modo *single-file*, ela só é incluída no executável com `IncludeNativeLibrariesForSelfExtract`, e é extraída em tempo de execução para `%TEMP%\.net\SideWar` (ou para `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, se definida).
- ADR-014: o aplicativo não acessa a rede e não possui atualizador próprio. Atualizações externas gerenciadas pela Store são compatíveis com essa decisão.
- D-04: canal principal Microsoft Store via MSIX conforme a decisão abaixo; identidade real e assinatura local de teste permanecem pendentes. D-03 (direitos dos assets) e D-09 (nome `SideWar.exe`, pasta `%LOCALAPPDATA%\SideWar`) alimentam o conteúdo do pacote.
- Pasta de dados e modo portátil: SDD-14 (RF-14.1). Ciclo de vida e "iniciar com o Windows": SDD-07. Metas P-00 e P-08 e o modo `--bench`: SDD-16.

## 4. Escopo e exclusões

**Escopo:** MSIX via Windows Application Packaging Project (WAP), manifesto, assets e distribuição pela Store; `Directory.Build.props` (versão), perfil de publicação, `scripts/windows/publish.ps1`, conteúdo do ZIP, `SHA256SUMS.txt`, verificação do pacote, assinatura opcional, instalação e remoção, análise do instalador v1 e requisitos mínimos.

**Exclusões:**

- Atualizador próprio e acesso à rede pelo aplicativo (ADR-014).
- ARM64 (pós-MVP).
- Migração automática de dados entre ZIP e MSIX e publicação sem autorização.

## 5. Requisitos funcionais

### RF-18.1: Versionamento

- `Version` = `0.1.0` em `Directory.Build.props`. SemVer **manual**: quem libera incrementa a versão.
- `InformationalVersion` = `Version` + hash curto do git, injetado na publicação por `-p:SourceRevisionId=<hash curto>` (ex.: `0.1.0+a1b2c3d`).
- `SideWar.exe --version` imprime a `InformationalVersion`.

### RF-18.2: Perfil de publicação

Arquivo `src/SideWar.App/Properties/PublishProfiles/win-x64.pubxml`:

```xml
<Project>
  <PropertyGroup>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishReadyToRun>true</PublishReadyToRun>
    <PublishTrimmed>false</PublishTrimmed>
    <DebugType>embedded</DebugType>
    <SatelliteResourceLanguages>pt-BR;en</SatelliteResourceLanguages>
  </PropertyGroup>
</Project>
```

- **Nota:** a compressão aumenta o tempo de início e a memória. Por isso, na primeira vez, `publish.ps1` gera as variantes `-Compressed` e `-Uncompressed`, mede **P-08** (início) e **P-00** (base de memória) nas duas, e a escolha vira uma **ADR** (CA-18.5).
- A extração nativa ocorre em `%TEMP%\.net\SideWar` ou em `DOTNET_BUNDLE_EXTRACT_BASE_DIR`.

### RF-18.3: `scripts/windows/publish.ps1`

1. Executar `build.ps1` em Release e `test.ps1` sem os testes interativos. Abortar em qualquer falha.
2. Executar `dotnet publish` com o perfil `win-x64` e `-p:SourceRevisionId`.
3. Gerar `artifacts/release/SideWar-<versão>-win-x64.zip` contendo: `SideWar.exe`, `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` (SkiaSharp MIT, .NET MIT, **Silkscreen** e **JetBrains Mono** (OFL, com os textos de licença) e os **assets do design** exportados do artefato (D-03); **sem** Pixelify Sans nem IBM Plex; ADR-031) e `README.txt` (requisitos, pasta de dados, atalhos e como desinstalar). **Sem** `portable.flag`.
4. Gerar `SHA256SUMS.txt`.
5. Verificar o pacote: extrair numa pasta temporária e executar `SideWar.exe --bench idle-static --seconds 10` (exit 0) e `SideWar.exe --version`.

### RF-18.4: Assinatura opcional

- Se `SIDEWAR_SIGN_CERT_THUMBPRINT` estiver definida: `signtool sign /fd SHA256 /tr <url de timestamp configurável> /td SHA256 /sha1 <thumbprint>`.
- Sem certificado, não assina, e o `README.txt` avisa sobre o SmartScreen.
- O parâmetro de timestamp é configuração do script. O app em si continua sem rede (ADR-014); a rede é usada só pelo `signtool` na máquina de build.

### RF-18.5: Instalação por ZIP e remoção

- **Instalar:** extrair o ZIP (sugestão: `%LOCALAPPDATA%\Programs\SideWar`).
- **Remover:** pela opção **Configurações > Geral > Remover dados** (E8), que desliga "iniciar com o Windows" e apaga `%LOCALAPPDATA%\SideWar` após confirmação; ou manualmente (apagar a pasta do app e a pasta de dados).
- O app usa `%LOCALAPPDATA%\SideWar` e `%TEMP%` (extração nativa). O instalador ZIP pode criar um atalho opcional na pasta Inicialização; a opção MSIX usa `StartupTask`.

### RF-18.6: ADR — canal Microsoft Store (D-04, 2026-11-19)

**Decisão:** a Microsoft Store é o canal principal para usuários finais. Gerar MSIX para Windows Desktop x64 com Windows Application Packaging Project (`.wapproj`), mínimo `10.0.19041.0` e aplicativo .NET self-contained, sem acrescentar Windows App SDK. O ZIP e seu fluxo existentes permanecem disponíveis para testes. O Windows gerencia instalação e remoção do MSIX; o instalador do ZIP não é o mecanismo de instalação do pacote.

**Identidade e entrega:** até existir identidade no Partner Center, usar identidade de **TESTE / PLACEHOLDER**, claramente identificada e substituível por propriedades ou argumentos de build. Antes de enviar à Store, obter os valores exatos de nome, publisher e publisher display name no Partner Center e rejeitar o placeholder. Usar versão de pacote de quatro componentes conforme as regras da Store, separada da SemVer do aplicativo, e manter identidade estável entre atualizações. A assinatura local de teste é separada da distribuição assinada pela Store; não incluir chaves no repositório.

**Atualizações:** a Store instala atualizações externamente e pode usar rede. O aplicativo continua sem acesso à rede e sem atualizador próprio, preservando ADR-014.

**Dados:** os saves do MSIX podem ficar no armazenamento virtualizado privado, separados do ZIP; não há migração automática. A virtualização depende da existência e do acesso a dados legados: leitura pode alcançar a localização real e arquivos existentes podem ter comportamento diferente. Validar caminhos e coexistência sem sobrescrever progresso; atualizações devem preservar dados do pacote e a desinstalação pode removê-los. O modo portátil via `portable.flag` ainda não está implementado; quando implementado para o ZIP, deve permanecer desativado no MSIX, pois o diretório de instalação é imutável.

**Inicialização:** declarar `StartupTask` inicialmente desabilitada e oferecer opção nas configurações para habilitar ou desabilitar por escolha explícita. Respeitar desativação pelo Windows ou usuário, sem tentar reativar quando o sistema impedir. Não remover automaticamente atalho legado do ZIP.

**Validação pendente:** comprovar empacotamento WAP com ferramentas Windows, dependências nativas e execução em VM limpa sem .NET; validar WACK, instalação, atualização, remoção, saves, coexistência ZIP/MSIX, inicialização, overlay, hook e atalhos. Aprovação no WACK não garante certificação. Não submeter nem publicar sem autorização.

### RF-18.7: Requisitos mínimos

- Windows 10 versão 2004 x64 (build mínimo 10.0.19041) ou Windows 11 x64; Windows 10 ainda não testado em máquina real.
- GPU com driver WDDM.
- 150 MB de disco.

## 6. Requisitos não funcionais

- **RNF-18.1:** ZIP ≤ 90 MB (meta, **não comprovada**; a medir).
- **RNF-18.2:** Zero chamadas de rede pelo aplicativo, verificado por 10 min com o Monitor de Recursos ou `netstat -b` (MTC-18.1).
- **RNF-18.3:** Build reprodutível a partir do hash git (mesmo commit gera o mesmo conteúdo funcional; versão identificada pelo hash).

## 7. Estratégia de implementação

1. E1 a E4: sem distribuição formal; usar `dotnet publish` manual quando necessário.
2. E5: `publish.ps1` mínimo (passos 1 a 4) para o ZIP interno de playtest.
3. E8: verificação automática (passo 5), assinatura opcional, opção "Remover dados", medição das variantes de compressão e ADR.
4. Implementar e validar o canal MSIX conforme a decisão D-04; manter o ZIP para testes. Submissão e publicação dependem de autorização.

## 8. Dependências

SDD-01 (projetos), SDD-02 (scripts de build e teste), SDD-07 (iniciar com o Windows), SDD-14 (pasta de dados e portátil) e SDD-16 (`--bench`, P-00 e P-08).

## 9. Critérios de aceitação

(Todos **não comprovados** até execução.)

- **CA-18.1:** `publish.ps1` gera o ZIP e o `SHA256SUMS.txt` e passa na verificação.
- **CA-18.2:** No Windows Sandbox ou numa VM sem .NET, o app inicia e permite jogar uma onda (MTC-18.2).
- **CA-18.3:** Remoção do ZIP conforme MTC-18.3; para MSIX, verificar remoção pelo Windows e documentar o ciclo de vida dos dados privados.
- **CA-18.4:** Zero chamadas de rede (MTC-18.1).
- **CA-18.5:** P-08 e P-00 medidos nas duas variantes (comprimida e não comprimida), com ADR registrando a escolha.
- **CA-18.6:** MSIX x64 self-contained instala e executa em Windows limpo; identidade, atualização, dados, StartupTask e coexistência são validados conforme RF-18.6, incluindo WACK.

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-18.1 | Verificação automática do pacote: extrair, `--bench idle-static --seconds 10` (exit 0), `--version`, e `--version --diag` checando a *awareness* de DPI (manifesto embutido) | WindowsInteractive |
| MTC-18.1 | Manual: 10 min de execução com Monitor de Recursos ou `netstat -b`, sem conexões | Manual (Windows) |
| MTC-18.2 | Manual: Windows Sandbox/VM sem .NET; iniciar e jogar uma onda | Manual (Windows) |
| MTC-18.3 | Manual: instalar, usar, remover; conferir `%LOCALAPPDATA%\SideWar` e `HKCU\...\Run` | Manual (Windows) |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| SmartScreen sem assinatura | Alta | Médio | Documentar no README; certificado via D-04 |
| Antivírus com *single-file* extraindo em `%TEMP%` | Média | Médio | Variante em pasta, não *single-file* |
| Início lento por descompressão | Média | Médio | Medir as variantes (P-08, CA-18.5) |
| DPI errado se o manifesto não for embutido | Baixa | Alto | T-18.1 checa a *awareness* com `--version --diag` |

## 12. Checklist resumido do Partner Center

- Verificar a conta de desenvolvedor, reservar o nome e obter identidade, publisher e display name exatos; definir versão de pacote aceita.
- Preparar URL de política de privacidade e contatos, declarar o comportamento real e verificar direitos de marca, fontes, áudio e arte.
- Preencher categoria, mercados, preço e disponibilidade, classificação etária, acessibilidade e demais propriedades exigidas.
- Preparar listagem nos idiomas suportados, descrição fiel, captura atual e logos exigidos; conferir requisitos vigentes do portal.
- Validar WACK e pacote, arquitetura, versão, dispositivos e dependências; preparar `.msixupload` ou outro pacote aceito e símbolos disponíveis.
- Nas notas de certificação, explicar overlay, hook de mouse para clique fora sem consumir eventos, atalhos, bandeja, opção de inicialização, passos de uso e justificativa de `runFullTrust`. Declarar apenas comportamentos comprovados.
- Revisar resultados, listagem e opções de lançamento; enviar ou publicar somente com autorização explícita.
