<div align="center">

<img src="src/GitCross.App/Assets/icon-128.png" alt="git-cross" width="96" height="96">

# git-cross

**Un client Git veloce e nativo per Linux, macOS e Windows.**
Grafo della cronologia, staging per file / blocco / riga, rebase interattivo, blame, stash, sottomoduli e risoluzione dei conflitti — in un'unica finestra pulita.

[![CI](https://github.com/giamplai/git-cross/actions/workflows/ci.yml/badge.svg)](https://github.com/giamplai/git-cross/actions/workflows/ci.yml)
[![Licenza: MIT](https://img.shields.io/badge/licenza-MIT-blue.svg)](LICENSE.md)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4)
![Piattaforme](https://img.shields.io/badge/piattaforme-Linux%20%7C%20macOS%20%7C%20Windows-lightgrey)

🇬🇧 [English](README.md) · [Guida utente](docs/HOWTO.md) · [Installazione](#installazione) · [Compilare dai sorgenti](#compilare-dai-sorgenti) · [Contribuire](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

</div>

<p align="center">
  <img src="docs/images/history.png" alt="Cronologia con grafo dei commit, etichette dei rami e un diff" width="900">
</p>

> La guida utente completa (in inglese) è in [docs/HOWTO.md](docs/HOWTO.md); l'interfaccia dell'applicazione è in inglese.

---

## Perché

I buoni client Git grafici non mancano, ma su una piattaforma o sull'altra; uno completo e veloce che abbia lo stesso aspetto e
comportamento ovunque è raro. git-cross è costruito su [.NET](https://dotnet.microsoft.com) e [Avalonia](https://avaloniaui.net)
(accelerazione GPU tramite Skia) e usa il tuo vero eseguibile `git`: hook, credential helper, configurazione SSH e `.gitconfig`
si comportano esattamente come nel terminale.

È **ispirato a [Git Extensions](https://github.com/gitextensions/gitextensions)**, il client Git per Windows — vedi i
[Crediti](#crediti).

## Funzioni

| | |
|---|---|
| **Cronologia** | Grafo a corsie, etichette di rami / tag / remoti, avatar degli autori, date relative, ricerca istantanea (messaggio, autore, hash, riferimento), cronologia per file che segue i rinomini. Una cronologia di 10 000 commit si apre in circa 0,2 s su una macchina di sviluppo. |
| **Staging** | Aggiungi, rimuovi e scarta per **file**, per **blocco (hunk)** o per **singole righe selezionate**. Amend. Indicatore della lunghezza del messaggio. |
| **Diff** | Vista unificata e **affiancata**, con **evidenziazione dei caratteri** modificati dentro una riga e numeri di riga. |
| **Rami e tag** | Crea, checkout, elimina, tagga da qualunque commit. Merge (normale / senza fast-forward / squash). Rebase e pull con rebase. |
| **Riscrivere la storia** | **Rebase interattivo** con riordino, reword, squash, fixup e drop, con validazione in tempo reale. Cherry-pick, revert, reset (soft / mixed / hard, con conferma). |
| **Conflitti** | Un banner mostra l'operazione sospesa; *nostro / loro* per file, continua o annulla per merge, rebase, cherry-pick e revert. Passa i file a un **editor di merge a tre vie esterno** (Collaterale, Meld, KDiff3, VS Code o un tuo comando). |
| **Blame** | Autore per riga a qualunque commit o nella copia di lavoro; doppio clic per saltare al commit. |
| **Stash** | Crea, elenca, ispeziona, applica, pop, elimina. |
| **Sottomoduli** | Icone di stato, inizializza / aggiorna, apri come repository e torna indietro, clone ricorsivo. |
| **Uso quotidiano** | Dialoghi di clone / init, repository recenti, fetch / pull / push con contatori avanti-indietro, temi chiaro / scuro / di sistema, Impostazioni (percorso di git, identità, modalità di pull, dimensione della cronologia). |
| **Sicuro per impostazione predefinita** | Le azioni distruttive chiedono conferma; i file non tracciati non vengono mai eliminati con un solo clic; gli strumenti esterni scrivono su un file temporaneo e sostituiscono il tuo file solo se riportano successo. |

<table>
  <tr>
    <td><img src="docs/images/commit-tab.png" alt="Scheda Commit con i pulsanti per blocco"></td>
    <td><img src="docs/images/diff-split.png" alt="Diff affiancato con caratteri evidenziati"></td>
  </tr>
  <tr>
    <td><img src="docs/images/interactive-rebase.png" alt="Dialogo del rebase interattivo"></td>
    <td><img src="docs/images/conflict.png" alt="Banner dei conflitti e vista dei marcatori"></td>
  </tr>
  <tr>
    <td><img src="docs/images/history-dark.png" alt="Tema scuro"></td>
    <td><img src="docs/images/blame.png" alt="Vista blame"></td>
  </tr>
</table>

## Installazione

> Richiede **git** installato ([git-scm.com](https://git-scm.com)). I pacchetti sono autonomi e **non** richiedono .NET.

Scarica il pacchetto per il tuo sistema dalla pagina **[Releases](https://github.com/giamplai/git-cross/releases)**:

| Sistema | Pacchetto | Come |
|---|---|---|
| Debian, Ubuntu, Mint… | `git-cross_<versione>_amd64.deb` (o `arm64`) | `sudo apt install ./git-cross_*.deb` — aggiunge una voce di menu e il comando `git-cross` |
| Qualunque Linux | `git-cross-<versione>-linux-x64.tar.gz` (o `linux-arm64`) | estrai, poi `./install.sh` per un'installazione per utente, oppure esegui direttamente `./git-cross` |
| Windows (x64) | `git-cross-<versione>-win-x64.zip` | estrai ed esegui `git-cross.exe` |
| macOS 11+ | `git-cross-<versione>-osx-arm64.zip` (Apple silicon) oppure `osx-x64` (Intel) | decomprimi e sposta *git-cross.app* in Applicazioni |

Verifica i download con `SHA256SUMS`.

> **Build non firmate.** I pacchetti Windows e macOS non sono ancora firmati, quindi SmartScreen / Gatekeeper mostrano un avviso
> la prima volta (Windows: *Ulteriori informazioni → Esegui comunque*; macOS: clic destro sull'app → *Apri*).

## Avvio rapido

```bash
git-cross                 # riapre l'ultimo repository, oppure mostra la schermata iniziale
git-cross /percorso/repo  # apre direttamente un repository
git cross /percorso/repo  # equivalente: git esegue come `git cross` ogni eseguibile `git-cross` nel PATH
```

| Scorciatoia | Azione |
|---|---|
| `Ctrl+O` | Apri un repository |
| `F5` | Aggiorna |
| `Ctrl+F` | Cerca nei commit |
| `Ctrl+Invio` | Esegui il commit (nella casella del messaggio) |

## Compilare dai sorgenti

Servono l'[SDK .NET 10](https://dotnet.microsoft.com/download) e `git`.

```bash
git clone https://github.com/giamplai/git-cross.git
cd git-cross
git config core.hooksPath scripts/hooks   # facoltativo: i controlli pre-commit del progetto

dotnet run --project src/GitCross.App -- /percorso/di/un/repo
scripts/check.sh                          # compilazione (gli avvisi sono errori) + tutti i test
scripts/package.sh linux-x64              # pacchetti di rilascio in artifacts/packages
```

Ogni pacchetto si può compilare da qualunque sistema (`scripts/package.sh` senza argomenti produce Linux, Windows e macOS).
Vedi [docs/RELEASING.md](docs/RELEASING.md) e [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Come funziona

```
GitCross.App  (Avalonia · MVVM · binding compilati)
    │  i view-model chiamano
GitCross.Core (nessun codice UI)  ──►  esegue `git` (senza shell, output leggibile da macchina)
    ├─ GitRepository   un metodo per operazione (log, status, stage, rebase, …)
    ├─ Parsers         funzioni pure per i formati porcelain / -z / personalizzati di git
    ├─ Diff / Graph    modello del diff unificato, modifiche intra-riga, righe affiancate, layout a corsie
    └─ Tools           modelli di comando per programmi esterni di merge / diff
```

* **Testato su repository reali.** I test del Core creano repository temporanei (conflitti, sottomoduli, cronologie grandi);
  i test UI renderizzano la vera finestra senza display e generano anche gli screenshot della guida.
* **Mai una shell.** Gli argomenti sono passati come liste, quindi nomi insoliti di file o rami non possono essere reinterpretati.
* Approfondimenti: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · mappa delle funzioni [docs/PORTING.md](docs/PORTING.md) · piani [ROADMAP.md](ROADMAP.md).

## Stato

Versione **0.1** — utilizzabile per il lavoro quotidiano, ma giovane. Limiti noti (vedi *Known limitations* nella guida): nessun
editor di merge a tre vie integrato (usa uno strumento esterno), nessuna colorazione della sintassi nei diff, nessun confronto di
cartelle tra due commit, nessun sistema di plugin, nessuna localizzazione, pacchetti Windows / macOS non firmati. Le build Windows
e macOS sono prodotte dalla CI ma hanno avuto molti meno test manuali di quella Linux. Segnalazioni e idee sono benvenute.

## Contribuire

I contributi sono benvenuti — segnalazioni, idee, documentazione, codice. Leggi prima **[CONTRIBUTING.md](CONTRIBUTING.md)** e il
**[Codice di condotta](CODE_OF_CONDUCT.md)**. Problemi di sicurezza: vedi **[SECURITY.md](SECURITY.md)**.

## Crediti

* **Autore e manutentore:** [Giampiero Lai](https://github.com/giamplai).
* **Ispirazione:** [Git Extensions](https://github.com/gitextensions/gitextensions) — grazie ai suoi autori e contributori per aver
  mostrato com'è un Git GUI pratico e completo. Questa è un'implementazione indipendente che non contiene nulla del suo codice,
  delle sue traduzioni o della sua grafica, e **non è affiliata né approvata** da quel progetto
  ([come lo verifichiamo](docs/PROVENANCE.md)).
* **Costruito con** [Avalonia](https://avaloniaui.net), [SkiaSharp](https://github.com/mono/SkiaSharp), HarfBuzzSharp,
  il carattere [Inter](https://rsms.me/inter/) e il runtime [.NET](https://dotnet.microsoft.com) — avvisi completi in
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
* **Compagno:** [Collaterale](https://github.com/giamplai/Collaterale), strumento di confronto e merge dello stesso autore,
  supportato come strumento esterno di merge/diff preferito.

## Licenza

[MIT](LICENSE.md) © 2026 Giampiero Lai. Sei libero di usare, copiare, modificare, unire, pubblicare, distribuire e vendere questo
software, a condizione di mantenere la nota di copyright e il testo della licenza — è questo il credito all'autore. Gli altri
componenti mantengono le proprie licenze ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), [NOTICE.md](NOTICE.md)).
