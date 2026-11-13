SideWar Desktop 0.1.0 (build interno de playtest)

SideWar é um jogo de defesa que roda como uma faixa sobre a barra de tarefas do Windows.
Este pacote contém o executável SideWar.exe e estes arquivos de texto.


REQUISITOS (RF-18.7)

- Windows 11 22H2 (build 22621) ou superior, x64.
- Placa de vídeo com driver WDDM.
- 150 MB de espaço em disco.
- Windows 10 não é suportado oficialmente.
- Não é preciso instalar o .NET: o runtime vem dentro do pacote (publicação self-contained).


INSTALAÇÃO (RF-18.5)

1. Extraia o ZIP numa pasta sua. Sugestão: %LOCALAPPDATA%\Programs\SideWar
2. Execute SideWar.exe.

Não há instalador. Não há atualização automática: para trocar de versão, substitua a pasta do app.

Aviso do SmartScreen: esta versão não é assinada digitalmente. Ao executar pela primeira vez, o Windows
SmartScreen pode mostrar um aviso. Confira se o arquivo veio do canal esperado antes de continuar.
Você pode verificar a integridade com o arquivo SHA256SUMS.txt, quando ele acompanhar o pacote.


PRIVACIDADE E REDE

Por projeto (ADR-014), o SideWar não usa a rede: não há atualização automática no MVP. A ausência de conexões
ainda está sendo verificada em testes manuais.


PASTA DE DADOS

Os dados ficam em %LOCALAPPDATA%\SideWar:
- settings.json (configurações), profile.json (progresso) e session.json (sessão), com cópias .bak.
- logs\ (registros do aplicativo, com rotação).

O app também usa %TEMP% durante a execução, para extrair suas bibliotecas nativas.


ATALHOS GLOBAIS (funcionam com outros programas em foco)

- Ctrl+Alt+G: alterna o modo interativo da faixa (passivo e interativo).
- Ctrl+Alt+H: mostra ou oculta a faixa.
- Ctrl+Alt+P: pausa ou retoma a partida.

Os atalhos podem ser alterados em Configurações. Se algum estiver em uso por outro programa, o app avisa.
O menu da bandeja do sistema e o clique direito no widget também dão acesso às ações principais.


DESINSTALAÇÃO (RF-18.5)

Para remover o SideWar sem deixar resíduos:

1. Se a opção "Iniciar com o Windows" estiver ligada, desligue-a antes em Configurações > Geral.
   Se você não puder abrir o app, apague a chave do registro HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SideWar.
2. Apague a pasta onde você extraiu o app (por exemplo, %LOCALAPPDATA%\Programs\SideWar).
3. Apague a pasta de dados %LOCALAPPDATA%\SideWar.

Quando disponível nesta versão, a opção Configurações > Geral > Remover dados faz os passos 1 e 3 após confirmação.


LICENÇAS

- LICENSE.txt: situação da licença do projeto.
- THIRD-PARTY-NOTICES.txt: licenças e avisos dos componentes de terceiros (SkiaSharp, .NET, fontes Silkscreen e JetBrains Mono) e dos assets.
