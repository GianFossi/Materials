# MaterialLibrary.Crud

CRUD helpers for `MaterialLibrary` on .NET 8.

## Highlights

- Create, read, update, and delete complete `Material` records
- In-memory `MaterialCrudRepository` with fast ID lookup
- Save and load complete material libraries through the core JSON serialization API
- Read, save, create-default, and delete `MaterialLibrary.config.xml`
- Update stored physical-property, strength-property, and special-property tables
- Import/export staged XML files from `MaterialLibrary/data` into a specific material reference
- Uses the core `MaterialLibrary` domain model and validation types
- XML data writes use temp-file replacement and path checks to reduce side effects

## Install

```bash
dotnet add package MaterialLibrary.Crud --version 1.0.1
```

## Quick Example (F#)

```fsharp
open MaterialLibrary.Crud
open MaterialLibrary.Domain

let repo = MaterialCrudRepository()

// Create/update/delete complete materials.
// let material: Material = ...
// repo.Create(material)
// repo.Read(material.Id)
// repo.Upsert(updatedMaterial)
// repo.Delete(material.Id)

// Read or create the XML configuration file.
let config =
    ConfigurationCrud.readOrCreateDefault "MaterialLibrary.config.xml"

// Import staged XML data into a specific material reference.
// repo.ImportXmlDataIntoMaterial(
//     "./data",
//     material.Id,
//     "physical-properties-xml/Density/PRD-Density.xml")
```

## Notes

`MaterialLibrary.Crud` does not duplicate the engineering domain model. It wraps
the core `MaterialLibrary` types and serialization APIs so CRUD workflows remain
consistent with the main library.

