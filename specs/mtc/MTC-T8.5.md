# MTC-T8.5: distribuição (pacote, zero rede, Sandbox sem .NET, remoção limpa)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Fichas: PLANO T8.4 (verificação do pacote e zero rede, CA-18.1, CA-18.4) e T8.5 (instalação por ZIP e remoção limpa; Windows Sandbox/VM sem .NET; CA-18.2, CA-18.3). Evidência em `specs/evidencias/E8.md`, que exige "MTC-18.1 (zero rede), MTC-18.2 (Windows Sandbox/VM sem .NET) e MTC-18.3 (remoção limpa), P-08 e P-00 nas duas variantes e ADR da escolha" (PLANO E8).
> Pacote: `artifacts\release\SideWar-<versão>-win-x64.zip` + `SHA256SUMS.txt`, gerado por `scripts/windows/publish.ps1` (REPORT T8.2: ZIP de 88.073.561 bytes na 0.1.0, dentro da meta de 90 MB; conteúdo `SideWar.exe`, `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt`, `README.txt`, sem `.pdb`).
> **Bloqueante de release (REPORT T8.2):** `LICENSE.txt` ainda é marcador PENDENTE (T8.3). Os checklists podem rodar, mas o release não fecha sem a licença.

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| T8.4-1 | Conferir `SHA256SUMS.txt` do ZIP e dos arquivos extraídos | Hashes batem (REPORT T8.2: conferido com `sha256sum -c`; no Windows usar `Get-FileHash -Algorithm SHA256`) | | | | |
| T8.4-2 | `SideWar.exe --version` | Imprime a `InformationalVersion` (ex.: `0.1.0+3943e12`), exit 0 (SDD-18 RF-18.1; REPORT T8.2) | | | | |
| T8.4-3 | `SideWar.exe --version --diag` | JSON com build do SO, *awareness* de DPI (manifesto embutido), monitores, pasta de dados e versão; exit 0 (SDD-16 RF-16.2; SDD-18 T-18.1) | | | | |
| T8.4-4 | `SideWar.exe --bench idle-static --seconds 10` | exit 0 (SDD-18 RF-18.3 passo 5; T-18.1). A verificação foi deixada para T8.4 (REPORT T8.2) | | | | |
| MTC-18.1 | Executar o jogo por **10 min** com o Monitor de Recursos (aba Rede) ou `netstat -b` | **Zero conexões** do `SideWar.exe` (SDD-18 RNF-18.2, CA-18.4, tabela MTC-18.1). *Executar com a rede do ambiente ligada; com rede desabilitada a ausência de conexões não prova nada* | | | | |
| MTC-18.2 | Num Windows Sandbox ou VM **sem .NET**: extrair o ZIP, iniciar e jogar uma onda | O app inicia e permite jogar uma onda (SDD-18 CA-18.2, tabela MTC-18.2). Registrar `winver`: o mínimo é Windows 11 22H2, build 22621, x64 (RF-18.7) | | | | |
| MTC-18.3-a | Extrair (sugestão `%LOCALAPPDATA%\Programs\SideWar`), usar, ligar "iniciar com o Windows" | O app só grava em `%LOCALAPPDATA%\SideWar`, em `%TEMP%` (extração nativa) e em `HKCU\...\Run\SideWar` se a opção estiver ligada (SDD-18 RF-18.5) | | | | |
| MTC-18.3-b | Remover (Configurações > Geral > Remover dados, se existir na build; ou apagar a pasta do app e a pasta de dados) | Sem resto em `%LOCALAPPDATA%\SideWar` nem em `HKCU\...\Run` (SDD-18 CA-18.3, tabela MTC-18.3). A opção "Remover dados" é de E8 (RF-18.5) e pode não existir ainda | | | | |
| P-08 | Início (exe → primeiro frame visível), frio e morno | ≤ 2,5 s frio / ≤ 1,2 s morno (SDD-16 P-08). Registrar nas duas variantes pedidas pela E8 | | | | |
| P-00 | `baseline-wpf` nas duas variantes | Registrar Private bytes / Working set (SDD-16 P-00) | | | | |
