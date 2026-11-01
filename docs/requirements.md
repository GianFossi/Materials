# Requisiti

## Sviluppo dai sorgenti

Usare il SDK indicato da [global.json](../global.json): **10.0.100**, con
`rollForward: latestMinor` nella linea .NET 10. I comandi vanno eseguiti dalla radice
specificando il progetto; vedere [installazione](installation.md).

| Componente | Framework effettivo | Requisiti di esecuzione |
|---|---|---|
| MaterialLibrary / MaterialLibrary.Crud | `net10.0` | Runtime .NET 10 nell'applicazione consumatrice |
| App WPF | `net10.0-windows` | Windows e .NET 10 **Desktop Runtime** |
| Excel-DNA | `net10.0-windows` | Windows, Excel desktop e .NET 10 **Desktop Runtime** dell'architettura di Excel |
| DatabaseUpdater | `net8.0` | Runtime .NET 8 per la versione framework-dependent |

Il SDK del repository e il framework del singolo progetto sono cose diverse:
il DatabaseUpdater resta un tool .NET 8, mentre i pacchetti core/CRUD sono .NET 10.
Per le dipendenze esatte consultare i rispettivi file di progetto, non conteggi o
resoconti di compilazioni passate.

## Windows ed Excel

- WPF ed Excel richiedono Windows per l'esecuzione.
- Excel a 32 bit richiede XLL e Desktop Runtime x86; Excel a 64 bit richiede x64.
  L'architettura da seguire è quella di **Excel**, non quella di Windows.
- Le XLL packed includono dipendenze dell'add-in e provider SQLite, non il runtime .NET.
- La libreria di calcolo non richiede Excel né WPF.

## Dati e permessi

- Il riferimento distribuito e gli XML devono essere disponibili sotto `data/` a runtime.
- Le copie locali e le opzioni richiedono scrittura nelle cartelle dell'utente.
- Gli export ASME usati dal DatabaseUpdater devono essere forniti separatamente.
- I PDF normativi citati nei dataset non sono sostituiti dalla documentazione del progetto.

## Dipendenze

Il core usa FSharp.Core, Ganfoss.ROP e Microsoft.Data.Sqlite. CRUD usa il core e SQLite;
Excel aggiunge Excel-DNA e l'app WPF. System.Text.Json è fornito dal framework.
Le versioni vincolanti sono in [MaterialLibrary.fsproj](../src/MaterialLibrary/MaterialLibrary.fsproj),
[CRUD](../src/MaterialLibrary.Crud/MaterialLibrary.Crud.fsproj),
[WPF](../src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj),
[Excel](../src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj) e
[DatabaseUpdater](../tools/MaterialLibrary.DatabaseUpdater/MaterialLibrary.DatabaseUpdater.fsproj).

Tenere i runtime aggiornati. Una distribuzione self-contained va ripubblicata per
includere gli aggiornamenti del runtime.
