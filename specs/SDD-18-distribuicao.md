# SDD-18: Distribuição da aplicação

**Status:** Rascunho para revisão · **Etapa:** E8 (o ZIP interno já é usável na E5 para playtest) · **Depende de:** SDD-01, 02, 07, 14, 16 · **Bloqueia:** release

## 1. Identificador e título

SDD-18: Versionamento, publicação self-contained, empacotamento em ZIP, assinatura opcional, instalação e remoção, caminho do instalador v1 e requisitos mínimos.

## 2. Objetivo

Entregar o SideWar Desktop como um pacote que roda numa máquina Windows limpa, sem .NET instalado, com verificação automática, instalação e remoção que não deixam resíduo e sem nenhuma chamada de rede. Todas as metas numéricas desta ficha são **não comprovadas** até medição (SDD-16).

## 3. Contexto técnico

- ADR-008: sem trimming e sem Native AOT (o WPF não suporta). A publicação é **self-contained com ReadyToRun**.
- `libSkiaSharp.dll` é uma biblioteca nativa. No modo *single-file*, ela só é incluída no executável com `IncludeNativeLibrariesForSelfExtract`, e é extraída em tempo de execução para `%TEMP%\.net\SideWar` (ou para `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, se definida).
- ADR-014: nenhum acesso à rede, portanto **sem atualização automática no MVP**.
- D-04 (canal de distribuição e certificado de assinatura) está **pendente**. D-03 (direitos dos assets) e D-09 (nome `SideWar.exe`, pasta `%LOCALAPPDATA%\SideWar`) alimentam o conteúdo do pacote.
- Pasta de dados e modo portátil: SDD-14 (RF-14.1). Ciclo de vida e "iniciar com o Windows": SDD-07. Metas P-00 e P-08 e o modo `--bench`: SDD-16.

## 4. Escopo e exclusões

**Escopo:** `Directory.Build.props` (versão), perfil de publicação, `scripts/windows/publish.ps1`, conteúdo do ZIP, `SHA256SUMS.txt`, verificação do pacote, assinatura opcional, instalação e remoção, análise do instalador v1 e requisitos mínimos.

**Exclusões:**

- Microsoft Store.
- Atualização automática no MVP (ADR-014, sem rede).
- ARM64 (pós-MVP).
- Escolha final do instalador v1 e do certificado: dependem da decisão D-04, por ADR.

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
- O app só grava em `%LOCALAPPDATA%\SideWar`, em `%TEMP%` (extração nativa) e em `HKCU\...\Run\SideWar` (se a opção estiver ligada).

### RF-18.6: Caminho da v1 (decisão D-04, por ADR antes de implementar)

| Opção | Prós | Contras |
|---|---|---|
| MSIX | Instalação e remoção limpas, `StartupTask`, App Installer | Exige certificado; virtualização do `LocalAppData` |
| Velopack | Instalador e atualização delta | Nova dependência; a atualização exige rede (conflito com ADR-014) |
| Inno Setup | Maduro | Ferramenta externa; sem atualização |

**Recomendação:** MSIX se houver certificado; caso contrário, Inno Setup sem atualização. Nenhuma implementação começa antes da ADR.

### RF-18.7: Requisitos mínimos

- Windows 11 22H2 (build 22621) ou superior, x64.
- GPU com driver WDDM.
- 150 MB de disco.
- Windows 10 **não** é suportado oficialmente.

## 6. Requisitos não funcionais

- **RNF-18.1:** ZIP ≤ 90 MB (meta, **não comprovada**; a medir).
- **RNF-18.2:** Zero chamadas de rede, verificado por 10 min com o Monitor de Recursos ou `netstat -b` (MTC-18.1).
- **RNF-18.3:** Build reprodutível a partir do hash git (mesmo commit gera o mesmo conteúdo funcional; versão identificada pelo hash).

## 7. Estratégia de implementação

1. E1 a E4: sem distribuição formal; usar `dotnet publish` manual quando necessário.
2. E5: `publish.ps1` mínimo (passos 1 a 4) para o ZIP interno de playtest.
3. E8: verificação automática (passo 5), assinatura opcional, opção "Remover dados", medição das variantes de compressão e ADR.
4. Após a decisão D-04: ADR do instalador v1 e implementação.

## 8. Dependências

SDD-01 (projetos), SDD-02 (scripts de build e teste), SDD-07 (iniciar com o Windows), SDD-14 (pasta de dados e portátil) e SDD-16 (`--bench`, P-00 e P-08).

## 9. Critérios de aceitação

(Todos **não comprovados** até execução.)

- **CA-18.1:** `publish.ps1` gera o ZIP e o `SHA256SUMS.txt` e passa na verificação.
- **CA-18.2:** No Windows Sandbox ou numa VM sem .NET, o app inicia e permite jogar uma onda (MTC-18.2).
- **CA-18.3:** Remoção limpa, sem resto em `%LOCALAPPDATA%` nem em `Run` (MTC-18.3).
- **CA-18.4:** Zero chamadas de rede (MTC-18.1).
- **CA-18.5:** P-08 e P-00 medidos nas duas variantes (comprimida e não comprimida), com ADR registrando a escolha.

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
