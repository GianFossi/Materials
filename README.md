# MaterialLibrary

Libreria F# per dati e calcoli sui materiali, con database SQLite ASME 2025,
applicazione desktop Windows per la manutenzione e componente aggiuntivo Excel.

## Da dove iniziare

La documentazione segue due percorsi distinti:

| Percorso | Prima lettura | Approfondimenti |
|---|---|---|
| **1. Come è strutturato il database** | [Database: file, identità, curve e provenienza](docs/asme-materials-db.md) | [Modelli della libreria](docs/material-models.md), [API](docs/library-api.md) |
| **2. Come usare e mantenere l'app** | [App: consultazione, modifiche e manutenzione](docs/desktop-app.md) | [Editor ASME 2025](docs/asme-2025-editor.md), [importazione XML](docs/xml-physical-import.md), [aggiornamento da export ASME](docs/database-updater.md) |

Se riprendi il progetto dopo una pausa, parti dalla [mappa del progetto](docs/overview.md).
Per installare: [requisiti](docs/requirements.md), [installazione](docs/installation.md)
e [Excel](docs/excel-addin.md).

## Tre cose da non confondere

- **Database di riferimento:** [database/data/materials_2025.db](database/data/materials_2025.db).
  Contiene identità, dati meccanici importati e provenienza.
- **Copia di lavoro:** l'app modifica un file `.working.db`, non il riferimento.
  La copia predefinita è `%LOCALAPPDATA%\MaterialLibrary\ASME2025\materials_2025.working.db`.
- **XML fisici e diagrammi:** [database/data](database/data/) contiene anche dati da
  verificare e associare prima dell'importazione. La presenza di un XML non implica che
  ogni materiale del database abbia già quella proprietà.

I documenti di libreria JSON/XML sono un percorso separato per materiali e modelli
personalizzati. Il database 2023 è un [archivio esplicito](database/data/archive/2023/README.md),
mai un'integrazione automatica dei dati 2025.

## Avvio dai sorgenti

Con il .NET SDK previsto da [global.json](global.json), dalla radice del repository:

```powershell
dotnet run --project .\src\MaterialLibrary.CrudApp\MaterialLibrary.CrudApp.csproj
```

Nella finestra principale scegliere **ASME 2025 Properties**; per backup, tabelle e SQL:
**Database → Manage database...**. La [guida dell'app](docs/desktop-app.md) spiega cosa
viene salvato e dove.

Per sviluppatori:

```powershell
dotnet build .\src\MaterialLibrary\MaterialLibrary.fsproj
dotnet test .\tests\MaterialLibrary.Tests\MaterialLibrary.Tests.fsproj
```

Non c'è una soluzione alla radice: specificare sempre il progetto.

## Componenti

| Componente | Funzione |
|---|---|
| [MaterialLibrary](src/MaterialLibrary/) | Modelli, interpolazioni, lettura nativa e serializzazione; [README NuGet](src/MaterialLibrary/README-NUGET.md) |
| [MaterialLibrary.Crud](src/MaterialLibrary.Crud/) | Persistenza, editor nativo e importazione XML; [README NuGet](src/MaterialLibrary.Crud/README-NUGET.md) |
| [MaterialLibrary.CrudApp](src/MaterialLibrary.CrudApp/) | Interfaccia WPF Windows |
| [MaterialLibrary.Excel](src/MaterialLibrary.Excel/) | Funzioni Excel e accesso all'editor condiviso |
| [DatabaseUpdater](tools/MaterialLibrary.DatabaseUpdater/) | Crea/confronta edizioni da export ASME; non importa gli XML fisici |

## Altre guide

- [Opzioni e percorsi](docs/crud-app-options.md)
- [Backup e verifiche prima del rilascio](docs/release-safety.md)
- [Contribuire](CONTRIBUTING.md)
- [Cronologia tecnica](AI_HISTORY.md)
- [Licenza](docs/license.md)

I dati non sostituiscono il codice normativo: verificare fonte, variante, condizioni,
limiti di temperatura e accettazione degli eventuali Code Cases prima dell'uso ingegneristico.
