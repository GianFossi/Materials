# Installation, Build, and Removal

## Quick Start | Avvio Rapido

Build:

```bash
dotnet build .\src\MaterialLibrary\MaterialLibrary.fsproj
```

Run tests:

```bash
dotnet test .\tests\MaterialLibrary.Tests\MaterialLibrary.Tests.fsproj
```

Compile examples:

```bash
dotnet build .\tests\MaterialLibrary.Examples\MaterialLibrary.Examples.fsproj
```

Pack NuGet:

```bash
dotnet pack .\src\MaterialLibrary\MaterialLibrary.fsproj -c Release -o .\publish\nuget
```

Generated package:

- `publish/nuget/MaterialLibrary.1.0.2.nupkg`

The `MaterialLibrary` package includes `ASME_Materials.db` as `contentFiles/any/any/data/ASME_Materials.db`.

Publish to NuGet:

```bash
dotnet nuget push .\publish\nuget\MaterialLibrary.1.0.2.nupkg --api-key <NUGET_API_KEY> --source https://api.nuget.org/v3/index.json
```

## Removal | Rimozione

- NuGet package: remove the PackageReference from the consuming project, or run dotnet remove package MaterialLibrary / dotnet remove package MaterialLibrary.Crud.
- Excel add-in: remove the loaded .xll from Excel's add-in list, close Excel, then delete the published publish/excel folder if it is no longer needed.
- Desktop app: delete the published publish/crud-app folder, or remove any shortcut that points to MaterialLibrary.CrudApp.exe.
- Database working copies: delete *.working.db files only after saving any changes that must be kept.

