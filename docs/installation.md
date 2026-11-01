# Installazione, compilazione e rimozione

Prima leggere i [requisiti](requirements.md). I comandi seguenti sono per PowerShell,
eseguiti dalla radice del repository.

## App desktop dai sorgenti

```powershell
dotnet run --project .\src\MaterialLibrary.CrudApp\MaterialLibrary.CrudApp.csproj
```

Per distribuire l'app framework-dependent su Windows x64:

```powershell
dotnet publish .\src\MaterialLibrary.CrudApp\MaterialLibrary.CrudApp.csproj -c Release -r win-x64 --self-contained false -o .\publish\crud-app
```

Distribuire **tutta la cartella**, inclusi configurazione e `data/`, non solo l'EXE.
Sul computer destinatario occorre .NET 10 Desktop Runtime x64.
Per l'uso vedere [app desktop](desktop-app.md).

## Librerie NuGet

In un progetto consumatore compatibile con .NET 10:

```powershell
dotnet add package MaterialLibrary --version 1.0.2
dotnet add package MaterialLibrary.Crud --version 1.0.2
```

CRUD serve solo se occorrono servizi di persistenza/modifica/importazione.
I pacchetti non installano l'app desktop o Excel. Vedere i README
[core](../src/MaterialLibrary/README-NUGET.md) e [CRUD](../src/MaterialLibrary.Crud/README-NUGET.md).

Per creare i pacchetti dai sorgenti:

```powershell
dotnet pack .\src\MaterialLibrary\MaterialLibrary.fsproj -c Release -o .\publish\nuget
dotnet pack .\src\MaterialLibrary.Crud\MaterialLibrary.Crud.fsproj -c Release -o .\publish\nuget
```

Il core include il riferimento come
`contentFiles/any/any/data/materials_2025.db` e gli XML previsti dal progetto.
Controllare che il consumatore conservi i dati nel proprio output.

## Excel e DatabaseUpdater

- [Excel](excel-addin.md): compilazione x86/x64, caricamento XLL e funzioni.
- [DatabaseUpdater](database-updater.md): comandi per creare/confrontare un'edizione;
  è separato dall'app desktop e dall'importatore XML.

## Verifica per sviluppatori

```powershell
dotnet build .\src\MaterialLibrary\MaterialLibrary.fsproj
dotnet test .\tests\MaterialLibrary.Tests\MaterialLibrary.Tests.fsproj
dotnet build .\tests\MaterialLibrary.Examples\MaterialLibrary.Examples.fsproj
```

`dotnet build/run/test/pack` ripristinano normalmente le dipendenze del progetto.
Non usare `dotnet restore` senza progetto alla radice: non c'è una soluzione root.
Per la verifica di una distribuzione vedere [rilascio e backup](release-safety.md).

## Rimozione senza perdere i dati

- NuGet: rimuovere il PackageReference dal progetto consumatore.
- Excel: disabilitare l'add-in nell'elenco di Excel e chiudere Excel prima di rimuoverne i file.
- Desktop: rimuovere cartella distribuita e collegamenti, dopo aver chiuso l'app.
- **Dati locali:** prima conservare copia `.working.db`, librerie JSON/XML e opzioni.
  La copia di lavoro sotto LocalAppData non coincide con la cartella dell'eseguibile.

Rimuovere il programma non deve comportare automaticamente la cancellazione dei materiali
modificati dall'utente.
