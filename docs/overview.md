# Overview

## Features | Funzionalita

- EN: Material domain model with fixed (a priori) standard units
- IT: Modello dominio materiali con unita standard fissate a priori
- EN: Extended material identity metadata (ProductForm, NominalComposition, Specification, Grade, Class/Condition/Tempering, AlloyIdentification_UNS)
- IT: Metadati identita materiale estesi (ProductForm, NominalComposition, Specification, Grade, Class/Condition/Tempering, AlloyIdentification_UNS)
- EN: Canonical MaterialName composition: Specification + Grade + Class/Condition/Tempering + AlloyIdentification_UNS (non-empty parts only)
- IT: Composizione canonica MaterialName: Specification + Grade + Class/Condition/Tempering + AlloyIdentification_UNS (solo parti non vuote)
- EN: Code-design metadata: maximum allowable temperature by ASME VIII-I / VIII-1 / VIII-2, time-dependent start temperature, and welding P/G numbers
- IT: Metadati di progetto codice: temperatura massima ammissibile per ASME VIII-I / VIII-1 / VIII-2, temperatura di inizio campo dipendente dal tempo, e numeri di saldatura P/G
- EN: Interpolation for specific heat, unified stress-strain, creep, and stress-rupture tables
- IT: Interpolazione per calore specifico, stress-strain, creep, isocrone e stress-rupture
- EN: Creep models (Norton, Garofalo, Kachanov Omega)
- IT: Modelli creep (Norton, Garofalo, Kachanov Omega)
- EN: One creep-table type for database data or explicitly selected model generation
- IT: Un solo tipo di tabella creep per dati da database o generazione con modello selezionato
- EN: Unified external-pressure material tables from a database or Code Case 2964 generation
- IT: Tabelle materiale unificate per pressione esterna, lette da database o generate con Code Case 2964
- EN: Time-independent and isochronous data in one stress-strain table type
- IT: Curve stress-strain indipendenti dal tempo e curve isocrone dipendenti dal tempo
- EN: Dedicated builders for stress-strain, creep, and external-pressure tables
- IT: Builder dedicati per curve stress-strain, isocrone, creep e tabelle di pressione esterna
- EN: Explicit Engineering/True basis metadata on stress/strain-driven datasets
- IT: Metadati espliciti Engineering/True sui dataset basati su stress/deformazione
- EN: Console test suite with end-to-end validations
- IT: Suite test console con validazioni end-to-end
- EN: Excel-DNA add-in exposing material search and property lookups as worksheet functions (see [Excel Add-In](#excel-add-in--componente-aggiuntivo-excel))
- IT: Componente aggiuntivo Excel-DNA che espone ricerca materiali e proprieta come funzioni foglio di calcolo (vedi [Excel Add-In](#excel-add-in--componente-aggiuntivo-excel))
- EN: Windows desktop (WPF) CRUD application for browsing and editing a material library JSON file (see [Desktop CRUD App](#desktop-crud-app--applicazione-desktop-crud))
- IT: Applicazione desktop Windows (WPF) CRUD per sfogliare e modificare un file JSON di libreria materiali (vedi [Desktop CRUD App](#desktop-crud-app--applicazione-desktop-crud))

## Project Layout | Struttura Progetto

- `src/MaterialLibrary` - main DLL project
- `src/MaterialLibrary/data/ASME_Materials.db` - packaged ASME SQLite material database
- `src/MaterialLibrary/builders/StressStrainTableBuilder.fs` - time-independent and isochronous stress-strain builders
- `src/MaterialLibrary/builders/CreepTableBuilder.fs` - validated creep-table construction and model generation
- `src/MaterialLibrary/builders/ExternalPressureTableBuilder.fs` - database and Code Case 2964 table construction
- `src/MaterialLibrary.Crud` - CRUD helpers (repository, table, configuration, and staged-XML-data operations) built on top of `src/MaterialLibrary`
- `src/MaterialLibrary.CrudApp` - WPF desktop application (`MaterialLibrary.CrudApp.exe`) exposing `MaterialLibrary.Crud` through a UI (see [Desktop CRUD App](#desktop-crud-app--applicazione-desktop-crud) below)
- `src/MaterialLibrary.Excel` - Excel-DNA add-in exposing material search and property lookups as worksheet functions (see [Excel Add-In](#excel-add-in--componente-aggiuntivo-excel) below)
- `tests/MaterialLibrary.Tests` - xUnit test project
- `tests/MaterialLibrary.Examples` - compiled usage examples
- `.vscode/tasks.json` - build/test/pack tasks
- `publish/nuget` - generated NuGet packages

