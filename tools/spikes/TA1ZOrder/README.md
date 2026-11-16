# Spike TA.1 (descartável)

Fora do produto: **não** está na `SideWar.slnx` nem no build. Evidência e recomendação em `specs/evidencias/TA.1.md`.

Compilar no Windows (csc do .NET Framework 4, C# 5, sem dependências de NuGet):

```text
csc.exe /nologo /platform:x64 /out:TA1.exe ^
  /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll Spike.cs
```

(As três DLLs estão no GAC: `C:\Windows\Microsoft.NET\assembly\GAC_MSIL\...`.)

Uso (**move o mouse e clica de verdade**; só em máquina que pode ser usada pelo spike durante ~5 min por variante):

```text
TA1.exe discover                       # lista a árvore UIA da barra, sem entrada
TA1.exe run <A|B|C|P|N> <reps> [casos] --towers x1,x2,x3 --empty x
casos: empty,icon,start,search,tray,thumbhover,thumbclick,burst,pass,inter,diag
```

Variantes: A = gatilhos da spec (foreground em `Shell_TrayWnd`, REORDER/LOCATIONCHANGE da barra) com debounce *trailing* de 100 ms;
B = idem com reafirmação imediata (borda de subida) + trailing de 100 ms; C = B + qualquer mudança de foreground;
P = C + checagem de z-order a cada 50 ms; N = sem reafirmação (controle).
Coordenadas `--towers`/`--empty` foram escolhidas a partir do `discover` (zona livre entre o último ícone e a bandeja).
Cada tentativa começa com clique numa janela auxiliar própria (outro app em primeiro plano) e termina restaurando foco, cursor e o app usado.
