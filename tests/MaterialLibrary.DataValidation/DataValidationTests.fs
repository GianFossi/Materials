/// Validation of the data stored in ASME_Materials.db for the materials of <c>Catalog</c>.
///
/// Run with: dotnet test tests/MaterialLibrary.DataValidation --logger "console;verbosity=detailed"
/// Failures point at the database content, not at the code: a failing test means a material is
/// missing from the database, a required value is absent, or a value contradicts the Code.
module MaterialLibrary.DataValidation.DataValidationTests

open System
open Xunit
open Xunit.Abstractions
open MaterialReportCore
open MaterialLibrary.DataValidation
open MaterialLibrary.DataValidation.Checks

/// Theory data: one row per requested catalog entry.
type CatalogCases() =
    static member Entries: seq<obj[]> =
        Catalog.all |> Seq.map (fun e -> [| box e.Label |])

/// Theory data: one row per database material selected by the catalog.
type MaterialCases() =
    static member Materials: seq<obj[]> =
        Catalog.all
        |> Seq.collect Database.select
        |> Seq.distinctBy (fun d -> d.Id)
        |> Seq.map (fun d -> [| box d.Id; box (Database.describe d) |])

/// Theory data: one row per reference strength of the Code.
type ReferenceCases() =
    static member Strength: seq<obj[]> =
        References.strength |> Seq.map (fun r -> [| box r.Specification; box r.Grade |])

    static member CarbonSteels: seq<obj[]> =
        References.carbonSteels |> Seq.map (fun (spec, grade) -> [| box spec; box grade |])

let private material (id: int) =
    Database.materials.Value |> List.find (fun d -> d.Id = id)

let private specGrade (spec: string) (grade: string) =
    Database.materials.Value
    |> List.filter (fun d -> d.Specification = spec && (grade = "" || String.Equals(d.Grade, grade, StringComparison.OrdinalIgnoreCase)))

/// Writes every non-passing check to the test output and fails on the Fail ones.
let private assertChecks (output: ITestOutputHelper) (label: string) (checks: Check list) =
    for c in checks do
        match c.Outcome with
        | Info -> output.WriteLine $"INFO  {c.Name}: {c.Detail}"
        | Fail -> output.WriteLine $"FAIL  {c.Name}: {c.Detail}"
        | Pass -> ()

    let failures = checks |> List.filter (fun c -> c.Outcome = Fail)

    if not failures.IsEmpty then
        let lines = failures |> List.map (fun c -> $"  - {c.Name}: {c.Detail}")
        Assert.Fail($"{label}:{Environment.NewLine}{String.Join(Environment.NewLine, lines)}")

[<Trait("Category", "DataValidation")>]
type CatalogTests(output: ITestOutputHelper) =

    [<Theory>]
    [<MemberData("Entries", MemberType = typeof<CatalogCases>)>]
    member _.``Requested material is in the database``(label: string) =
        let entry = Catalog.all |> List.find (fun e -> e.Label = label)
        let found = Database.select entry
        found |> List.iter (fun d -> output.WriteLine(Database.describe d))
        Assert.True(not found.IsEmpty, $"{label} is not in the database")

[<Trait("Category", "DataValidation")>]
type MaterialDataTests(output: ITestOutputHelper) =

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    member _.``Strength tables are present and consistent``(id: int, label: string) =
        assertChecks output label (strengthChecks (material id))

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    member _.``ASME IX welding numbers are present``(id: int, label: string) =
        assertChecks output label (weldingChecks (material id))

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    member _.``Allowable stress tables are present``(id: int, label: string) =
        assertChecks output label (allowableChecks (material id))

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    member _.``Physical properties are present and plausible``(id: int, label: string) =
        assertChecks output label (physicalChecks (material id))

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    member _.``Stress-strain curve inputs are complete``(id: int, label: string) =
        assertChecks output label (curveChecks (material id))

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    [<Trait("Category", "CodeCriteriaScreen")>]
    member _.``Allowable stress respects the Code criteria``(id: int, label: string) =
        assertChecks output label (criteriaChecks (material id))

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<MaterialCases>)>]
    member _.``Known database gaps are reported``(id: int, label: string) =
        // Never fails: it only documents what the database does not hold for this material.
        assertChecks output label (knownGaps (material id))

/// Exact values: the database must reproduce the Code for these grades.
[<Trait("Category", "DataValidation")>]
type ReferenceValueTests(output: ITestOutputHelper) =

    [<Theory>]
    [<MemberData("Strength", MemberType = typeof<ReferenceCases>)>]
    member _.``SMYS, SMTS and Division 1 allowable at 40 degC match the Code``(spec: string, grade: string) =
        let reference =
            References.strength |> List.find (fun r -> r.Specification = spec && r.Grade = grade)

        let rows = specGrade spec grade
        Assert.True(not rows.IsEmpty, $"{spec} {grade} is not in the database")

        for d in rows do
            let atFortyDegrees =
                d.Div1 |> List.choose (fun b -> valueAt 40.0 b.Curve)

            assertChecks
                output
                (Database.describe d)
                [ check "SMYS" (d.Smys = Some reference.Smys) $"database {d.Smys}, Code {reference.Smys}"
                  check "SMTS" (d.Smts = Some reference.Smts) $"database {d.Smts}, Code {reference.Smts}"
                  check
                      "Division 1 allowable at 40 degC"
                      (atFortyDegrees |> List.exists (fun s -> abs (s - reference.Division1At40) < 0.05))
                      $"database {atFortyDegrees}, Code {reference.Division1At40}" ]

    [<Theory>]
    [<MemberData("CarbonSteels", MemberType = typeof<ReferenceCases>)>]
    member _.``Carbon steel thermal expansion equals Table TE-1 Group 1``(spec: string, grade: string) =
        let rows = specGrade spec grade
        Assert.True(not rows.IsEmpty, $"{spec} {grade} is not in the database")

        for d in rows do
            // The Code lists one decimal, so a correctly derived value is within half a unit of the last digit.
            let deviations (reference: (float * float) list) (curve: Curve) =
                [ for t, expected in reference do
                      match valueAt t curve with
                      | Some actual when abs (actual - expected) > 0.06 -> yield $"{fmt t} degC: database {fmt actual}, Code {fmt expected}"
                      | None -> yield $"{fmt t} degC: missing"
                      | _ -> () ]

            let instantaneous = deviations References.te1Group1Instantaneous d.AlphaInstantaneous
            let mean = deviations References.te1Group1Mean d.AlphaMean

            assertChecks
                output
                (Database.describe d)
                [ check "Instantaneous coefficient = column A" instantaneous.IsEmpty (String.Join("; ", instantaneous))
                  check "Derived mean coefficient = column B (tol 0.06)" mean.IsEmpty (String.Join("; ", mean)) ]

    [<Theory>]
    [<MemberData("CarbonSteels", MemberType = typeof<ReferenceCases>)>]
    member _.``Carbon steel elastic modulus, density and Poisson ratio match the Code``(spec: string, grade: string) =
        let rows = specGrade spec grade
        Assert.True(not rows.IsEmpty, $"{spec} {grade} is not in the database")

        for d in rows do
            assertChecks
                output
                (Database.describe d)
                [ check
                      "E(25 degC) = 202 GPa"
                      (valueAt 25.0 d.Elastic = Some References.carbonSteelElasticModulusAt25)
                      $"database {valueAt 25.0 d.Elastic}"
                  check "Density = 7750 kg/m3" (d.Density = Some References.carbonSteelDensity) $"database {d.Density}"
                  check "Poisson ratio = 0.30" (d.Poisson = Some References.carbonSteelPoisson) $"database {d.Poisson}" ]

/// Theory data of the technical handbook list.
type ProntuarioCases() =
    static member Rows: seq<obj[]> =
        Prontuario.rows.Value |> List.mapi (fun i r -> [| box i; box (Prontuario.label r) |]) |> Seq.ofList

    /// One row per (handbook row, matching database material).
    static member Materials: seq<obj[]> =
        [ for i, row in Prontuario.rows.Value |> List.indexed do
              for d in Database.materials.Value |> List.filter (Prontuario.matches row) do
                  yield [| box i; box d.Id; box (Database.describe d) |] ]
        |> Seq.ofList

    /// One row per distinct database material of the handbook.
    static member DistinctMaterials: seq<obj[]> =
        [ for row in Prontuario.rows.Value do
              for d in Database.materials.Value |> List.filter (Prontuario.matches row) do
                  yield d ]
        |> List.distinctBy (fun d -> d.Id)
        |> List.map (fun d -> [| box d.Id; box (Database.describe d) |])
        |> Seq.ofList

/// Materials of the handbook with the Tmax of their (first) row.
let private prontuarioMaterials () : (MaterialData * Prontuario.Row) list =
    [ for row in Prontuario.rows.Value do
          for d in Database.materials.Value |> List.filter (Prontuario.matches row) do
              yield d, row ]
    |> List.distinctBy (fun (d, _) -> d.Id)

/// The physical-property tables of a material compared with the staged Code text. The Code column
/// is chosen from the UNS number or from the group the Code notes assign to the composition, never
/// from the database link between material and group.
let private codeChecks (d: MaterialData) : Check list =
    let reference = Database.codeReference.Value
    let expected = Database.physical d

    let reproduces (name: string) (r: PhysicalReference.Reference) (curve: Curve) =
        match r.Column with
        | None when curve.IsEmpty -> [ check $"{name} reproduces the Code" false "no data in the database" ]
        | None -> [ infoIf $"{name} reproduces the Code" true "group not determined from the Code notes: not verified" ]
        | Some column ->
            match CodeText.diagnoseAgainst 0.0005 column curve with
            | CodeText.Exact _ -> [ check $"{name} reproduces the Code" true "" ]
            | other ->
                [ check $"{name} reproduces the Code" false $"{CodeText.describe name other} [expected {r.Source}]" ]

    // The mean coefficient derived from the stored instantaneous one against column B of the Code.
    let meanAgainstCode =
        match expected.AlphaMean.Column with
        | Some mean ->
            let lookup = mean.Values |> dict

            let deviations =
                [ for t, derived in d.AlphaMean do
                      match lookup.TryGetValue t with
                      | true, code when abs (derived - code) > 0.1 -> yield $"{fmt t} degC: derived {fmt derived}, Code {fmt code}"
                      | _ -> () ]

            [ check "Derived mean expansion equals the Code column B (tol 0.1)" deviations.IsEmpty (String.Join("; ", deviations |> List.truncate 3)) ]
        | None -> []

    let scalar (name: string) (r: PhysicalReference.Reference) (database: float option) =
        match r.Curve, database with
        | (_, code) :: _, Some value when r.FromCode -> [ check $"{name} equals the Code" (abs (code - value) < 1e-9) $"database {value}, Code {code} ({r.Source})" ]
        | _ -> [ infoIf $"{name} equals the Code" true "not verified" ]

    reproduces "Elastic modulus" expected.Elastic d.Elastic
    @ reproduces "Instantaneous expansion" expected.AlphaInstantaneous d.AlphaInstantaneous
    @ meanAgainstCode
    @ reproduces "Thermal conductivity" expected.Conductivity d.Conductivity
    @ reproduces "Thermal diffusivity" expected.Diffusivity d.Diffusivity
    @ scalar "Density" expected.Density d.Density
    @ scalar "Poisson ratio" expected.Poisson d.Poisson

/// The materials of the technical handbook: values, temperature range and completeness.
[<Trait("Category", "DataValidation")>]
type ProntuarioTests(output: ITestOutputHelper) =

    [<Theory>]
    [<MemberData("Rows", MemberType = typeof<ProntuarioCases>)>]
    member _.``Handbook material is in the database``(index: int, label: string) =
        let row = Prontuario.rows.Value.[index]
        let found = Database.materials.Value |> List.filter (Prontuario.matches row)
        found |> List.iter (fun d -> output.WriteLine(Database.describe d))
        Assert.True(not found.IsEmpty, $"{label} is not in the database")

    [<Theory>]
    [<MemberData("Materials", MemberType = typeof<ProntuarioCases>)>]
    member _.``Handbook values, temperature range and completeness``(index: int, id: int, label: string) =
        let row = Prontuario.rows.Value.[index]
        assertChecks output label (Prontuario.checksFor row (material id))

/// The physical-property tables against the staged text of the Code.
[<Trait("Category", "DataValidation")>]
type CodeReferenceTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``Code text is parsed``() =
        let reference = Database.codeReference.Value
        Assert.True(reference.Columns.Length > 200, $"only {reference.Columns.Length} Code columns were read")
        Assert.True(reference.PoissonDensity.Length > 20, "Table PRD was not read")

        let carbonSteel = reference.Columns |> List.find (fun c -> c.Table = "TM-1" && c.Quantity = "E")
        Assert.Equal(Some 216.0, valueAt -200.0 carbonSteel.Values)
        Assert.Equal(Some 202.0, valueAt 25.0 carbonSteel.Values)

        let te1 = reference.Columns |> List.filter (fun c -> c.Table = "TE-1" && c.Quantity = "AlphaInstantaneous") |> List.head
        Assert.Equal(Some 11.5, valueAt 20.0 te1.Values)
        Assert.Equal(Some 14.9, valueAt 300.0 te1.Values)

    [<Theory>]
    [<MemberData("DistinctMaterials", MemberType = typeof<ProntuarioCases>)>]
    member _.``Physical properties reproduce the Code tables``(id: int, label: string) =
        assertChecks output label (codeChecks (material id))

/// Regression lock: the values at Tmin, Tmax and 100..600 degC of every table of the handbook
/// materials must stay equal to the verified baseline.
[<Trait("Category", "DataValidation")>]
type BaselineTests(output: ITestOutputHelper) =

    /// The values of the database as they are now.
    let current () =
        prontuarioMaterials ()
        |> List.collect (fun (d, row) -> Snapshot.take d (Some row.TmaxC) None)
        |> List.sortBy (fun p -> p.MaterialId, p.Table, p.Point)

    /// The values the baseline must hold: Code values for the physical properties, database values
    /// (not yet verified against the Code text) for the other tables.
    let reference () =
        prontuarioMaterials ()
        |> List.collect (fun (d, row) -> Snapshot.take d (Some row.TmaxC) (Some(Database.physical d)))
        |> List.sortBy (fun p -> p.MaterialId, p.Table, p.Point)

    [<Fact>]
    member _.``Handbook materials equal the verified baseline``() =
        let now = current ()

        if Environment.GetEnvironmentVariable "UPDATE_BASELINE" = "1" then
            let points = reference ()
            Snapshot.write Database.baselinePath points
            output.WriteLine $"Baseline rewritten: {Database.baselinePath} ({points.Length} points)"
        else
            Assert.True(IO.File.Exists Database.baselinePath, $"baseline not found: {Database.baselinePath} (run once with UPDATE_BASELINE=1)")
            let differences = Snapshot.differences (Snapshot.read Database.baselinePath) now
            let reportPath = IO.Path.Combine(AppContext.BaseDirectory, "baseline-differences.txt")
            IO.File.WriteAllLines(reportPath, differences)
            differences |> List.truncate 200 |> List.iter output.WriteLine

            if not differences.IsEmpty then
                let head = String.Join(Environment.NewLine, differences |> List.truncate 20)
                Assert.Fail($"{differences.Length} values differ from the baseline (all of them in {reportPath}); review them, and if the database is right run with UPDATE_BASELINE=1:{Environment.NewLine}{head}")

/// How every physical property of the handbook materials was resolved against the Code text.
[<Trait("Category", "DataValidation")>]
type PhysicalReferenceSummaryTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``Physical reference resolution``() =
        let say (s: string) = output.WriteLine s
        let materials = prontuarioMaterials () |> List.map fst

        let properties =
            [ "E", (fun (p: PhysicalReference.PhysicalProperties) -> p.Elastic)
              "Alpha instantaneous", (fun p -> p.AlphaInstantaneous)
              "Alpha mean", (fun p -> p.AlphaMean)
              "Conductivity", (fun p -> p.Conductivity)
              "Diffusivity", (fun p -> p.Diffusivity)
              "Specific heat", (fun p -> p.SpecificHeat)
              "Density", (fun p -> p.Density)
              "Poisson", (fun p -> p.Poisson) ]

        say $"Handbook materials: {materials.Length}"

        for name, pick in properties do
            let unresolved =
                materials |> List.filter (fun d -> not (pick (Database.physical d)).FromCode)

            say $"{name}: {materials.Length - unresolved.Length} from the Code, {unresolved.Length} database values kept"

            for d in unresolved |> List.truncate 12 do
                say $"    not verified: {Database.describe d}"

/// Pure checks of the derivation used for the mean thermal expansion (no database).
type MeanExpansionTests() =

    [<Fact>]
    member _.``Constant instantaneous coefficient gives the same mean``() =
        let mean = meanFromInstantaneous [ 20.0, 12.0; 100.0, 12.0; 300.0, 12.0 ]
        Assert.All(mean, fun (_, m) -> Assert.Equal(12.0, m, 9))

    [<Fact>]
    member _.``Linear instantaneous coefficient gives the trapezoidal mean``() =
        // alpha(T) = 10 + 0.01 (T - 20): mean over [20, T] is 10 + 0.005 (T - 20).
        let inst = [ for t in [ 20.0; 120.0; 220.0 ] -> t, 10.0 + 0.01 * (t - 20.0) ]
        let mean = meanFromInstantaneous inst |> dict
        Assert.Equal(10.0, mean.[20.0], 9)
        Assert.Equal(10.5, mean.[120.0], 9)
        Assert.Equal(11.0, mean.[220.0], 9)

    [<Fact>]
    member _.``Empty input gives an empty result``() =
        Assert.Empty(meanFromInstantaneous [])

/// Prints the full overview of the catalog: what was found, what is missing, what failed.
[<Trait("Category", "DataValidation")>]
type SummaryTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``Validation summary``() =
        let say (s: string) = output.WriteLine s
        let mutable notFound = []
        let mutable failing = []
        let mutable total = 0

        for entry in Catalog.all do
            match Database.select entry with
            | [] -> notFound <- entry.Label :: notFound
            | rows ->
                for d in rows do
                    total <- total + 1
                    let fails =
                        allChecks d
                        |> List.collect (fun (_, checks) -> checks)
                        |> List.filter (fun c -> c.Outcome = Fail)

                    if not fails.IsEmpty then
                        failing <- (Database.describe d, fails |> List.map (fun c -> c.Name)) :: failing

        say "================ DATA VALIDATION SUMMARY ================"
        say $"Database: {Database.path}"
        say $"Catalog entries: {Catalog.all.Length}   materials found: {total}   with failing checks: {failing.Length}"
        let missingNames = String.Join(", ", List.rev notFound)
        say $"Entries not in the database ({notFound.Length}): {missingNames}"

        for description, names in List.rev failing do
            let failed = String.Join("; ", names)
            say $"  {description}: {failed}"

        say "========================================================="
