module MaterialLibrary.Tests

open Xunit
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq
open MaterialLibrary
open MaterialLibrary.Crud
open MaterialLibrary.Domain
open MaterialLibrary.Domain.Database.Lookup
open MaterialLibrary.Interpolation
open Microsoft.Data.Sqlite

let private createTestMaterial () =
    let basicProps = BasicProperties.create (Some 21.0) (Some 19.0) 55.0 240.0 420.0

    let thermalExpansionTable =
        [ { Temperature = 20.0
            ExpansionCoefficient = 1.20e-5 }
          { Temperature = 400.0
            ExpansionCoefficient = 1.55e-5 } ]

    let elasticTable =
        [ ElasticModulusTablePoint.create 20.0 210000.0 None
          ElasticModulusTablePoint.create 400.0 170000.0 (Some 0.29) ]

    let densityTable =
        [ { Temperature = 20.0
            Density = 7850.0 }
          { Temperature = 400.0
            Density = 7700.0 } ]

    let physicalTable =
        PhysicalPropertiesTable.create None thermalExpansionTable elasticTable None densityTable None None

    Material.create "SA-516-70" "Carbon Steel Plate" "ASME SA-516" "70" basicProps physicalTable

let private expectOk result =
    match result with
    | Ok value -> value
    | Error error -> failwithf "Expected Ok, got %A" error

[<Fact>]
let ``material filtering combines criteria and rejects ambiguous unique matches`` () =
    let plate =
        createTestMaterial ()
        |> Material.setIdentity "Plate" "Carbon steel" "SA-516" "70" "" "K02700"

    let tube =
        { plate with Id = "SA-213-TP304" }
        |> Material.setIdentity "Smls. tube" "18Cr-8Ni" "SA-213" "TP304" "" "S30400"

    let criteria = MaterialSearchCriteria.identity "Plate" "SA-5116" "70" None
    let matches = MaterialFiltering.findMany criteria [ tube; plate ]

    Assert.Single(matches) |> ignore
    Assert.Equal(plate.Id, matches.Head.Id)

    let duplicate = { plate with Id = "duplicate" }
    Assert.True(MaterialFiltering.findUnique criteria [ plate; duplicate ] |> Result.isError)

[<Fact>]
let ``material filtering orders numeric database IDs numerically`` () =
    let materialWithId id =
        { createTestMaterial () with Id = id }
        |> Material.setIdentity "Plate" "Carbon steel" "SA-516" "70" "" "K02700"

    let criteria = MaterialSearchCriteria.identity "Plate" "SA-516" "70" None
    let matches = MaterialFiltering.findMany criteria [ materialWithId "10"; materialWithId "2"; materialWithId "1" ]

    Assert.Equal<string list>([ "1"; "2"; "10" ], matches |> List.map (fun material -> material.Id))

[<Fact>]
let ``ASME family classification covers supported steel families`` () =
    let classify specification grade condition composition uns =
        AsmeMaterialFamilyClassification.classify specification grade condition composition uns

    Assert.Equal(Some QT, classify "SA-517" "A" "" "Carbon steel" "K11856")
    Assert.Equal(Some QT, classify "SA-508" "3" "Quenched and tempered" "Ni-Cr-Mo" "K12042")
    Assert.Equal(Some LTCS, classify "SA-333" "6" "" "Carbon steel" "K03006")
    Assert.Equal(Some LAS1_00, classify "SA-193" "B7" "" "1Cr-1/5Mo" "G41400")
    Assert.Equal(Some LAS1_25, classify "SA-387" "11" "" "1¼Cr-½Mo-Si" "K11789")
    Assert.Equal(Some LAS2_25, classify "SA-387" "22" "" "2¼Cr-1Mo" "K21590")
    Assert.Equal(Some LAS5_00, classify "SA-387" "5" "" "5Cr-½Mo" "S50200")
    Assert.Equal(Some LAS9_00, classify "SA-387" "91" "" "9Cr-1Mo-V" "K90901")
    Assert.Equal(Some SSA, classify "SA-213" "TP304" "" "18Cr-8Ni" "S30400")
    Assert.Equal(Some SSF, classify "SA-240" "430" "" "17Cr" "S43000")
    Assert.Equal(Some SSM, classify "SA-240" "410" "" "12Cr" "S41000")
    Assert.Equal(Some SSD, classify "SA-240" "S32205" "" "22Cr-5Ni-3Mo" "S32205")
    Assert.Equal(Some SSDPlus, classify "SA-240" "S32750" "" "25Cr-7Ni-4Mo" "S32750")
    Assert.Equal(None, classify "SB-564" "N06625" "" "Ni-Cr-Mo" "N06625")

[<Fact>]
let ``material filtering can select an ASME family`` () =
    let carbon = { createTestMaterial () with Family = Some CS }
    let stainless = { carbon with Id = "stainless"; Family = Some SSA }
    let criteria = { MaterialSearchCriteria.empty with Family = Some SSA }

    let matchResult = Assert.Single(MaterialFiltering.findMany criteria [ carbon; stainless ])
    Assert.Equal<Material>(stainless, matchResult)

[<Fact>]
let ``material library search uses ASME identity criteria`` () =
    let target =
        createTestMaterial ()
        |> Material.setIdentity "Plate" "Carbon steel" "SA-516" "70" "Normalized" "K02700"
        |> fun material -> { material with Family = Some CS }

    let other =
        { target with Id = "other" }
        |> Material.setIdentity "Smls. tube" "18Cr-8Ni" "SA-213" "TP304" "" "S30400"
        |> fun material -> { material with Family = Some SSA }

    let library = MaterialLibrary.create [ other; target ] |> expectOk

    let matches =
        library.SearchMaterials(
            Some "SA-516",
            Some "70",
            Some "Norm",
            Some "K02700",
            Some "Carbon",
            Some "Plate",
            Some CS
        )

    let criteriaMatches =
        library.Search
            { MaterialSearchCriteria.empty with
                Specification = Some(Contains "SA-516")
                Grade = Some(Contains "70")
                ClassConditionTemper = Some(Contains "Norm")
                Uns = Some(Contains "K02700")
                NominalComposition = Some(Contains "Carbon")
                ProductForm = Some(Contains "Plate")
                Family = Some CS }

    Assert.Equal<Material list>([ target ], matches)
    Assert.Equal<Material list>(matches, criteriaMatches)

[<Fact>]
let ``requested database library loads six materials and classifies allowable stress sources`` () =
    let databasePath =
        Configuration.createDefault ()
        |> Configuration.getAsmeDatabasePath

    let materials = RequestedMaterialLibrary.loadMaterials databasePath |> expectOk

    Assert.Equal(6, materials.Length)
    Assert.All(materials, fun material -> Assert.False(material.Id.StartsWith("ASME-")))
    Assert.Contains(materials, fun material -> material.Specification = "SA-516" && material.Grade = "70")
    Assert.Contains(
        materials,
        fun material ->
            material.Specification = "SA-387"
            && material.Grade = "11"
            && material.Class_Condition_Tempering = "2"
    )
    Assert.Contains(materials, fun material -> material.Specification = "SA-213" && material.Grade = "T11")
    Assert.Contains(materials, fun material -> material.Specification = "SA-193" && material.Grade = "B7")
    Assert.Contains(materials, fun material -> material.Specification = "SA-516" && material.Family = Some CS)
    Assert.Contains(materials, fun material -> material.Specification = "SA-387" && material.Family = Some LAS1_25)
    Assert.Contains(materials, fun material -> material.Specification = "SA-213" && material.Grade = "TP304" && material.Family = Some SSA)
    Assert.Contains(materials, fun material -> material.Specification = "SA-213" && material.Grade = "T11" && material.Family = Some LAS1_25)
    Assert.Contains(materials, fun material -> material.Specification = "SA-193" && material.Family = Some LAS1_00)

    let tp304Standard =
        materials
        |> List.find (fun material ->
            material.Specification = "SA-213"
            && material.Grade = "TP304"
            && material.AllowableStressLevel = StandardAllowableStress)

    let tp304High =
        materials
        |> List.find (fun material ->
            material.Specification = "SA-213"
            && material.Grade = "TP304"
            && material.AllowableStressLevel = HighAllowableStress)

    let standard =
        tp304Standard.StrengthProperties.AllowableStressDatasets
        |> List.find (fun dataset ->
            dataset.Source = Division1AllowableStress
            && dataset.Case = StandardStrengthAllowableStress)

    let high =
        tp304High.StrengthProperties.AllowableStressDatasets
        |> List.find (fun dataset ->
            dataset.Source = Division1HighAllowableStress
            && dataset.Case = HighStrengthAllowableStress)

    let standardAt200 = PropertyTable.lookup1D 200.0 standard.Table |> expectOk
    let highAt200 = PropertyTable.lookup1D 200.0 high.Table |> expectOk

    Assert.True(standardAt200.Value < highAt200.Value)
    Assert.Contains({ Table = Table1A; Code = "G5" }, standard.AsmeNoteReferences)
    Assert.Contains({ Table = Table1A; Code = "G5" }, high.AsmeNoteReferences)
    Assert.Equal(None, standard.Notes)
    Assert.Equal(None, high.Notes)
    Assert.DoesNotContain(
        tp304Standard.StrengthProperties.AllowableStressDatasets,
        fun dataset -> dataset.Source = Division1HighAllowableStress
    )
    Assert.DoesNotContain(
        tp304High.StrengthProperties.AllowableStressDatasets,
        fun dataset -> dataset.Source = Division1AllowableStress
    )
    Assert.Contains(AsmeSectionI, tp304Standard.ApplicableAsmeCodes)
    Assert.Contains(AsmeSectionVIII1, tp304Standard.ApplicableAsmeCodes)
    Assert.Contains(AsmeSectionVIII2, tp304Standard.ApplicableAsmeCodes)

    let division2Standard =
        tp304Standard.StrengthProperties.AllowableStressDatasets
        |> List.filter (fun dataset -> dataset.Source = Division2AllowableStress)

    let division2High =
        tp304High.StrengthProperties.AllowableStressDatasets
        |> List.filter (fun dataset -> dataset.Source = Division2AllowableStress)

    Assert.NotEmpty(division2Standard)
    Assert.Equal<AllowableStressDataset list>(division2Standard, division2High)
    Assert.All(division2High, fun dataset -> Assert.Equal(StandardStrengthAllowableStress, dataset.Case))

    let b7 =
        materials
        |> List.find (fun material -> material.Specification = "SA-193" && material.Grade = "B7")

    let b7TensileAt400 =
        b7.StrengthProperties.TensileProperties
        |> List.find (fun properties -> properties.Temperature = 400.0)

    Assert.Equal(381.0, b7TensileAt400.YieldStrength, 12)
    Assert.Equal(629.0, b7TensileAt400.TensileStrength, 12)
    Assert.Equal(3, b7.StrengthProperties.AllowableStressDatasets.Length)
    Assert.Equal<float option list>(
        [ Some 64.0; Some 100.0; Some 180.0 ],
        b7.StrengthProperties.AllowableStressDatasets
        |> List.map (fun dataset -> dataset.SizeRange.Maximum)
    )
    Assert.All(
        b7.StrengthProperties.AllowableStressDatasets,
        fun dataset -> Assert.Equal(BoltingAllowableStress, dataset.Source)
    )

    let roundTrip =
        tp304High
        |> MaterialSerialization.toJsonString
        |> MaterialSerialization.fromJsonStringComplete
        |> expectOk

    Assert.Equal(HighAllowableStress, roundTrip.AllowableStressLevel)
    Assert.True(
        List.forall2
            (=)
            tp304High.StrengthProperties.AllowableStressDatasets
            roundTrip.StrengthProperties.AllowableStressDatasets
    )

[<Fact>]
let ``specific heat interpolation returns expected linear result`` () =
    let table =
        [ { Temperature = 20.0
            SpecificHeat = 477.0 }
          { Temperature = 100.0
            SpecificHeat = 500.0 }
          { Temperature = 200.0
            SpecificHeat = 520.0 } ]

    match SpecificHeatInterpolation.interpolate Linear 150.0 table with
    | Ok cp -> Assert.Equal(510.0, float cp, 6)
    | Error err -> failwithf "Interpolation failed: %A" err

[<Fact>]
let ``material builder preserves identity and properties`` () =
    let material =
        createTestMaterial ()
        |> Material.setIdentity "Plate" "C-Mn steel" "ASME SA-516" "70" "Normalized" "UNS K12345"

    Assert.Equal("Plate", material.ProductForm)
    Assert.Equal("UNS K12345", material.AlloyIdentification_UNS)
    Assert.Contains("ASME SA-516", material.Name)
    Assert.Contains("70", material.Name)

[<Fact>]
let ``Kachanov integration returns one value per time boundary`` () =
    let timeSteps = 10
    let damage = KachanovOmega.omegaEvolution 1.0e-8 2.0 1.0 100.0 timeSteps 1000.0 |> expectOk

    let strain =
        KachanovOmega.creepStrainWithDamage 1.0e-10 3.0 1.0 1.0e-8 2.0 1.0 100.0 timeSteps 1000.0
        |> expectOk

    Assert.Equal(timeSteps + 1, damage.Length)
    Assert.Equal(timeSteps + 1, strain.Length)
    Assert.True(KachanovOmega.omegaEvolution 1.0 1.0 1.0 1.0 0 1.0 |> Result.isError)

    Assert.True(
        KachanovOmega.creepStrainWithDamage 1.0 1.0 1.0 1.0 1.0 1.0 1.0 0 1.0
        |> Result.isError
    )

[<Fact>]
let ``Kachanov damage starts undamaged and grows monotonically`` () =
    let damage = KachanovOmega.omegaEvolution 1.0e-9 2.0 1.0 100.0 100 1000.0 |> expectOk

    Assert.Equal(0.0, damage.Head)
    Assert.True(damage |> List.pairwise |> List.forall (fun (left, right) -> right >= left))
    Assert.True(damage |> List.forall (fun value -> value >= 0.0 && value <= 1.0))

[<Fact>]
let ``Norton creep rate is the time derivative of creep strain`` () =
    let a, n, m, stress, time = 2.5e-8, 4.0, 0.35, 80.0, 1200.0
    let expected = a * m * stress ** n * time ** (m - 1.0)

    Assert.Equal(expected, NortonPowerLaw.creepRate a n m stress time |> expectOk, 12)
    Assert.True(NortonPowerLaw.creepRate a n m stress 0.0 |> Result.isError)
    Assert.True(GarofaloModel.creepStrain 1.0 1.0 1.0 1.0 System.Double.MaxValue 1.0 |> Result.isError)

[<Fact>]
let ``builder rejects malformed creep point input`` () =
    match CreepTableBuilder.create 500.0 100.0 "Malformed" [] with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Expected malformed creep points to return an error")

[<Fact>]
let ``library handles null values at public boundaries`` () =
    let library = MaterialLibrary(Unchecked.defaultof<Material list>)

    Assert.Equal(0, library.Count)
    Assert.Empty(library.SearchByName null)

[<Fact>]
let ``material JSON round trip preserves advanced properties`` () =
    let material = createTestMaterial ()

    let tensile =
        { Temperature = 400.0
          YieldStrength = 180.0
          TensileStrength = 390.0 }

    let compression =
        { Temperature = 400.0
          CompressiveStrength = 410.0
          CompressiveYield = 190.0 }

    let externalPressureTable =
        ExternalPressureTableBuilder.createFromDatabase
            400.0
            (Some 100000.0)
            "Database external-pressure table"
            [ { FactorA = 1.0e-4
                CompressiveStress = 100.0
                TangentModulus = 1.0e6 }
              { FactorA = 1.0e-3
                CompressiveStress = 150.0
                TangentModulus = 1.5e5 } ]
        |> expectOk

    let strengthProperties =
        { material.StrengthProperties with
            AllowableStresses =
                [ { Temperature = 400.0
                    Section_I_ServiceLevel_A = Some 120.0
                    Section_I_ServiceLevel_B = None
                    Section_I_ServiceLevel_C = None
                    Section_I_ServiceLevel_D = None
                    Section_II_Weld = Some 100.0 } ]
            TensileProperties = [ tensile ]
            CompressionProperties = Some [ compression ]
            ExternalPressureTables = [ externalPressureTable ]
            NortonModels = [ { Temperature = 400.0; A = 1.0e-8; N = 4.0; M = 0.3 } ]
            GarofaloModels =
                [ { Temperature = 400.0
                    A = 2.0e-9
                    N = 3.0
                    M = 0.4
                    Alpha = 0.01
                    Q = 200000.0 } ]
            KachanovOmegaModels =
                [ { Temperature = 400.0
                    A1 = 1.0e-10
                    N1 = 3.0
                    M1 = 1.0
                    A2 = 1.0e-9
                    N2 = 2.0
                    M2 = 1.0
                    Description = "Damage model" } ]
            AverageCreepStrainRateStress =
                [ CreepStrainRateTableBuilder.create 0.01 "Average SC" [ 400.0, 120.0; 450.0, 100.0 ]
                  |> expectOk ]
            MinimumCreepStrainRateStress =
                [ CreepStrainRateTableBuilder.create 0.01 "Minimum SC" [ 400.0, 100.0; 450.0, 80.0 ]
                  |> expectOk ]
            AverageCreepRuptureStress =
                [ CreepStressRuptureTableBuilder.create 100000.0 "Average SRavg" [ 400.0, 140.0; 450.0, 110.0 ]
                  |> expectOk ]
            MinimumCreepRuptureStress =
                [ CreepStressRuptureTableBuilder.create 100000.0 "Minimum SRmin" [ 400.0, 110.0; 450.0, 90.0 ]
                  |> expectOk ]
            LarsonMillerCurves =
                [ { Material = material.Id
                    Description = "LMP"
                    Points = [ { LarsonMillerParameter = 20000.0; Stress = 150.0 } ] } ] }

    let expected =
        { material with
            StrengthProperties = strengthProperties
            SpecialProperties =
                { AppendixIIIConstants =
                    [ { Temperature = 400.0
                        A0 = 1.0
                        A1 = 2.0
                        A2 = 3.0
                        A3 = 4.0
                        A4 = 5.0
                        B0 = 6.0
                        B1 = 7.0
                        B2 = 8.0
                        B3 = 9.0
                        B4 = 10.0
                        Notes = Some "Source" } ]
                  AppendixIIIFactorRule =
                    Some
                        { MaterialFamily = FerrousSteel
                          TemperatureLimitF = 1000.0
                          M2Coefficient = 0.6
                          EpsPrimeP = 0.2
                          Notes = None } } }

    let json = MaterialSerialization.toJsonString expected

    match MaterialSerialization.fromJsonStringComplete json with
    | Error error -> Assert.Fail(sprintf "Round trip failed: %A" error)
    | Ok actual ->
        Assert.Equal(expected.PhysicalProperties, actual.PhysicalProperties)
        Assert.Equal(expected.StrengthProperties, actual.StrengthProperties)
        Assert.Equal(expected.SpecialProperties, actual.SpecialProperties)

[<Fact>]
let ``database document store preserves nested table structures`` () =
    let stressStrain =
        StressStrainTableBuilder.createIsochronous
            500.0
            100000.0
            Engineering
            Engineering
            "Isochronous stress-strain"
            [ { Strain = 0.1; Stress = 110.0 }
              { Strain = 0.2; Stress = 160.0 } ]
            (Some 200.0)
            (Some 450.0)
        |> expectOk

    let creep =
        CreepTableBuilder.create
            500.0
            120.0
            "Creep strain"
            [ { Time = 0.0; Strain = 0.0 }
              { Time = 1000.0; Strain = 0.05 } ]
        |> expectOk

    let fatigueTable =
        PropertyTable.create1D
            "Fatigue"
            "Cycles"
            ""
            "Stress Amplitude"
            "MPa"
            XBoundaryPolicy.FlatExtrapolate
            [ { X = 1000.0; Value = 250.0 }
              { X = 10000.0; Value = 180.0 } ]
        |> expectOk
        |> fun table -> FatigueTable.create table 425.0 (Some 100000.0)

    let material =
        let baseMaterial = createTestMaterial ()
        { baseMaterial with
            StrengthProperties =
                { baseMaterial.StrengthProperties with
                    StressStrainTables = [ stressStrain ]
                    CreepTables = [ creep ]
                    FatigueCurves = [ fatigueTable ]
                    LarsonMillerCurves =
                        [ { Material = baseMaterial.Id
                            Description = "LMP"
                            Points =
                                [ { LarsonMillerParameter = 20000.0; Stress = 150.0 }
                                  { LarsonMillerParameter = 21000.0; Stress = 120.0 } ] } ] } }

    let databasePath = Path.Combine(Path.GetTempPath(), $"material-library-document-store-{System.Guid.NewGuid():N}.db")

    try
        do
            use connection = new SqliteConnection($"Data Source={databasePath}")
            connection.Open()
            use command = connection.CreateCommand()
            command.CommandText <-
                """
                CREATE TABLE Materials (
                    ID INTEGER PRIMARY KEY,
                    NominalComposition TEXT,
                    ProductForm TEXT,
                    Specification TEXT,
                    TypeGrade TEXT,
                    ClassConditionTemper TEXT,
                    AlloyDesignationNumber TEXT,
                    SMTS REAL,
                    SMYS REAL,
                    RuptureElongationLong REAL,
                    Notes TEXT
                )
                """
            command.ExecuteNonQuery() |> ignore

        MaterialDatabaseCrud.ensureSchema databasePath |> expectOk |> ignore
        MaterialDatabaseCrud.upsertMaterial databasePath material |> expectOk |> ignore

        let actual = MaterialDatabaseCrud.readMaterial databasePath material.Id |> expectOk

        Assert.Equal<StressStrainTable list>(
            material.StrengthProperties.StressStrainTables,
            actual.StrengthProperties.StressStrainTables
        )

        Assert.Equal<CreepTable list>(material.StrengthProperties.CreepTables, actual.StrengthProperties.CreepTables)
        Assert.Equal<FatigueTable list>(material.StrengthProperties.FatigueCurves, actual.StrengthProperties.FatigueCurves)

        Assert.Equal<LarsonMillerCurve list>(
            material.StrengthProperties.LarsonMillerCurves,
            actual.StrengthProperties.LarsonMillerCurves
        )
    finally
        if File.Exists(databasePath) then
            SqliteConnection.ClearAllPools()
            File.Delete(databasePath)

[<Fact>]
let ``stress strain replacement key includes time dependence and duration`` () =
    let points =
        [ { Strain = 0.1; Stress = 100.0 }
          { Strain = 0.2; Stress = 150.0 } ]

    let independent =
        StressStrainTableBuilder.createTimeIndependent 500.0 Engineering Engineering "Independent" points None None
        |> expectOk

    let dependent =
        StressStrainTableBuilder.createIsochronous
            500.0
            100000.0
            Engineering
            Engineering
            "Dependent"
            points
            None
            None
        |> expectOk

    let material =
        createTestMaterial ()
        |> StressStrainTableBuilder.addOrReplace independent
        |> expectOk
        |> StressStrainTableBuilder.addOrReplace dependent
        |> expectOk

    Assert.Equal(2, material.StrengthProperties.StressStrainTables.Length)
    Assert.All(
        material.StrengthProperties.StressStrainTables,
        fun table -> Assert.Equal(StressStrainDatabase, table.Source)
    )

[<Fact>]
let ``time-independent and isochronous stress-strain lookups share one table collection`` () =
    let independent =
        StressStrainTableBuilder.createTimeIndependent
            500.0
            Engineering
            Engineering
            "Independent"
            [ { Strain = 0.1; Stress = 100.0 }; { Strain = 0.2; Stress = 150.0 } ]
            None
            None
        |> expectOk

    let isochronous =
        StressStrainTableBuilder.createIsochronous
            500.0
            100000.0
            Engineering
            Engineering
            "Isochronous"
            [ { Strain = 0.1; Stress = 80.0 }; { Strain = 0.2; Stress = 120.0 } ]
            None
            None
        |> expectOk

    let material =
        createTestMaterial ()
        |> StressStrainTableBuilder.addOrReplace independent
        |> expectOk
        |> StressStrainTableBuilder.addOrReplace isochronous
        |> expectOk

    let library = MaterialLibrary.create [ material ] |> expectOk

    Assert.Equal(100.0, library.GetStressFromStrain(material.Id, 500.0, 0.1) |> expectOk, 12)
    Assert.Equal(
        80.0,
        library.GetStressFromStrainAtDuration(material.Id, 500.0, 100000.0, 0.1)
        |> expectOk,
        12
    )

[<Fact>]
let ``stress-strain serialization preserves isochronous duration and provenance`` () =
    let table =
        StressStrainTableBuilder.createIsochronous
            550.0
            200000.0
            Engineering
            Engineering
            "Generated isochronous table"
            [ { Strain = 0.1; Stress = 70.0 }; { Strain = 0.2; Stress = 100.0 } ]
            None
            None
        |> expectOk

    let table =
        { table with Source = GeneratedAsmeVIII2Annex3D }
        |> StressStrainTable.validate
        |> expectOk

    let json = SpecializedTableSerialization.stressStrainTableToJsonString table
    let actual = SpecializedTableSerialization.stressStrainTableFromJsonString json |> expectOk

    Assert.Equal(Some 200000.0, actual.ReferenceDurationHours)
    Assert.Equal(GeneratedAsmeVIII2Annex3D, actual.Source)

[<Fact>]
let ``creep replacement key uses exact structured applied stress`` () =
    let points =
        [ { Time = 1.0; Strain = 0.01 }
          { Time = 10.0; Strain = 0.05 } ]

    let create stress =
        CreepTableBuilder.create 500.0 stress "Creep" points |> expectOk

    let material =
        createTestMaterial ()
        |> CreepTableBuilder.addOrReplace (create 100.40)
        |> expectOk
        |> CreepTableBuilder.addOrReplace (create 100.49)
        |> expectOk

    Assert.Equal(2, material.StrengthProperties.CreepTables.Length)

[<Fact>]
let ``cyclic table serialization preserves amplitude and hysteresis range data`` () =
    let table =
        CyclicStrainTableBuilder.create
            400.0
            700.0
            0.12
            "Carbon steel"
            "Cyclic dataset"
            [ { StressAmplitude = 100.0; StrainAmplitude = 0.001 }
              { StressAmplitude = 200.0; StrainAmplitude = 0.003 } ]
            [ { StressRange = 200.0; StrainRange = 0.002 }
              { StressRange = 400.0; StrainRange = 0.006 } ]
        |> expectOk

    let json = SpecializedTableSerialization.cyclicStrainTableToJsonString table
    let actual = SpecializedTableSerialization.cyclicStrainTableFromJsonString json |> expectOk

    Assert.Equal(2, actual.Table.Columns.Head.Entries.Length)
    Assert.Equal(2, actual.HysteresisRangeTable.Columns.Head.Entries.Length)
    Assert.Equal(700.0, actual.Kcss, 12)
    Assert.Equal(0.12, actual.Ncss, 12)
    Assert.Equal("Carbon steel", actual.MaterialDescription)

[<Fact>]
let ``property table lookups reject malformed public records`` () =
    let malformed =
        { Name = "Malformed"
          XAxisName = "Temperature"
          XAxisUnit = "degC"
          YAxisName = "Stress"
          ValueUnit = "MPa"
          DimensionType = NoDimension
          DimensionUnit = ""
          XBoundaryPolicy = ReturnError
          Columns = [] }

    let duplicateEntries =
        { malformed with
            Columns =
                [ { SizeRange = { Lower = None; Upper = None; Label = None }
                    Entries = [ { X = 100.0; Value = 10.0 }; { X = 100.0; Value = 20.0 } ] } ] }

    Assert.True(PropertyTable.lookup1D 100.0 malformed |> Result.isError)
    Assert.True(PropertyTable.lookup1D 100.0 duplicateEntries |> Result.isError)
    Assert.True(PropertyTable.lookup1D System.Double.NaN duplicateEntries |> Result.isError)

[<Fact>]
let ``duplicate material IDs use last value consistently`` () =
    let first = { createTestMaterial () with Name = "First" }
    let second = { first with Name = "Replacement" }
    let library = MaterialLibrary([ first; second ])

    Assert.Equal(1, library.Count)
    Assert.Single(library.ListAllMaterials()) |> ignore
    Assert.Equal(Some second, library.GetMaterialById first.Id)

[<Fact>]
let ``checked library construction rejects duplicate IDs and supports replacement`` () =
    let first = createTestMaterial ()
    let replacement = { first with Name = "Replacement" }

    Assert.True(MaterialLibrary.create [ first; replacement ] |> Result.isError)

    let library = MaterialLibrary.create [ first ] |> expectOk
    let updated = MaterialLibrary.addMaterial replacement library |> expectOk

    Assert.Equal(1, updated.Count)
    Assert.Equal(Some replacement, updated.GetMaterialById first.Id)

[<Fact>]
let ``configuration validation rejects unsafe numerical defaults`` () =
    let valid = Configuration.createDefault ()
    let invalid = { valid with Creep = { valid.Creep with KachanovTimeSteps = 0 } }

    Assert.True(Configuration.validate valid |> Result.isOk)
    Assert.True(Configuration.validate invalid |> Result.isError)

[<Fact>]
let ``ASME database fallback file name is asme materials db`` () =
    let baseDirectory = Path.Combine(Path.GetTempPath(), $"material-library-empty-{System.Guid.NewGuid():N}")

    try
        Directory.CreateDirectory(baseDirectory) |> ignore
        let resolved = Configuration.resolveAsmeDatabasePath (Some baseDirectory)

        Assert.Equal(Path.Combine(baseDirectory, "ASME_Materials.db"), resolved)
    finally
        if Directory.Exists(baseDirectory) then
            Directory.Delete(baseDirectory, true)

[<Fact>]
let ``material JSON strictly enforces current schema`` () =
    let material =
        { createTestMaterial () with
            Family = Some LAS2_25
            AsmeNoteReferences =
                [ { Table = TableSy; Code = "Y1" }
                  { Table = TableSu; Code = "U2" } ]
            Notes = Some "User-defined material note" }

    let json = material |> MaterialSerialization.toJsonString
    // Matches whatever version the serializer currently writes, so a version bump cannot silently
    // turn this into a no-op substitution that then asserts against unmodified JSON.
    let replaceVersion version =
        Regex("\"schemaVersion\"\\s*:\\s*\\d+").Replace(json, $"\"schemaVersion\": {version}", 1)

    Assert.Contains("\"schemaVersion\"", json)
    Assert.Contains("\"family\":\"LAS2.25\"", json)
    let roundTrip = MaterialSerialization.fromJsonStringComplete json |> expectOk
    Assert.Equal(Some LAS2_25, roundTrip.Family)
    Assert.Equal<AsmeNoteReference list>(material.AsmeNoteReferences, roundTrip.AsmeNoteReferences)
    Assert.Equal(material.Notes, roundTrip.Notes)
    Assert.True(replaceVersion 0 |> MaterialSerialization.fromJsonStringComplete |> Result.isError)
    Assert.True(replaceVersion 99 |> MaterialSerialization.fromJsonStringComplete |> Result.isError)

    // Versions inside the supported window are accepted; anything below it is not.
    Assert.True(
        replaceVersion MaterialSerialization.MinimumReadableSchemaVersion
        |> MaterialSerialization.fromJsonStringComplete
        |> Result.isOk)

    Assert.True(
        replaceVersion (MaterialSerialization.MinimumReadableSchemaVersion - 1)
        |> MaterialSerialization.fromJsonStringComplete
        |> Result.isError)

[<Fact>]
let ``adaptive Kachanov integration reports accepted grid`` () =
    let history =
        KachanovOmega.omegaEvolutionConverged 1.0e-6 1.0 0.0 10.0 4 100.0 1.0e-12 3
        |> expectOk

    Assert.Equal(12.5, history.TimeStep, 12)
    Assert.Equal(9, history.Values.Length)
    Assert.Equal(None, history.RuptureTime)

[<Fact>]
let ``database and Code Case create the same external pressure table type`` () =
    let databaseTable =
        ExternalPressureTableBuilder.createFromDatabase
            400.0
            None
            "Database table"
            [ { FactorA = 1.0e-4
                CompressiveStress = 80.0
                TangentModulus = 8.0e5 }
              { FactorA = 1.0e-3
                CompressiveStress = 120.0
                TangentModulus = 1.2e5 } ]
        |> expectOk

    let timeIndependentTable =
        StressStrainTableBuilder.createTimeIndependent
            400.0
            Engineering
            Engineering
            "Time-independent stress-strain table"
            [ { Strain = 0.0; Stress = 10.0 }
              { Strain = 0.1; Stress = 100.0 }
              { Strain = 0.3; Stress = 150.0 } ]
            None
            None
        |> expectOk

    let generatedTable =
        ExternalPressureTableBuilder.createCodeCase2964FromStressStrainTable
            "Code Case 2964 time-independent table"
            timeIndependentTable
        |> expectOk

    let tables: ExternalPressureTable list = [ databaseTable; generatedTable ]

    Assert.Equal(2, tables.Length)
    Assert.Equal(MaterialDatabase, databaseTable.Source)
    Assert.Equal(CodeCase2964, generatedTable.Source)
    Assert.Equal(None, generatedTable.ReferenceDurationHours)

[<Fact>]
let ``external-pressure tables distinguish time-independent and isochronous data by duration`` () =
    let points stress =
        [ { FactorA = 1.0e-4
            CompressiveStress = stress
            TangentModulus = 1.0e6 }
          { FactorA = 1.0e-3
            CompressiveStress = stress * 1.5
            TangentModulus = 1.5e5 } ]

    let timeIndependent =
        ExternalPressureTableBuilder.createFromDatabase
            500.0
            None
            "Time-independent external-pressure table"
            (points 100.0)
        |> expectOk

    let isochronous =
        ExternalPressureTableBuilder.createFromDatabase
            500.0
            (Some 100000.0)
            "Isochronous external-pressure table"
            (points 80.0)
        |> expectOk

    let material =
        createTestMaterial ()
        |> ExternalPressureTableBuilder.addOrReplaceExternalPressureTable timeIndependent
        |> expectOk
        |> ExternalPressureTableBuilder.addOrReplaceExternalPressureTable isochronous
        |> expectOk

    let library = MaterialLibrary.create [ material ] |> expectOk

    let independentValue =
        library.GetExternalPressureAllowableCompressiveStress(material.Id, 500.0, None, 1.0e-4, Linear)
        |> expectOk

    let isochronousValue =
        library.GetExternalPressureAllowableCompressiveStress(
            material.Id,
            500.0,
            Some 100000.0,
            1.0e-4,
            Linear
        )
        |> expectOk

    Assert.Equal(2, material.StrengthProperties.ExternalPressureTables.Length)
    Assert.True(ExternalPressureTable.isTimeIndependent timeIndependent)
    Assert.False(ExternalPressureTable.isIsochronous timeIndependent)
    Assert.True(ExternalPressureTable.isIsochronous isochronous)
    Assert.Equal(100.0, independentValue.Value, 12)
    Assert.Equal(80.0, isochronousValue.Value, 12)

[<Fact>]
let ``API 579 Annex 10B5 guard prevents unimplemented calculations`` () =
    match Api579Annex10B5.ensureImplemented () with
    | Error(MaterialError.InvalidOperation message) ->
        Assert.Contains("not implemented", message)
        Assert.Contains("Do not use", message)
    | result -> Assert.Fail($"Expected an explicit not-implemented warning, got {result}")

[<Fact>]
let ``creep generation requires explicit model and preserves applicability warning`` () =
    let norton =
        CreepTableBuilder.generateWithNorton
            500.0
            100.0
            "Norton selected by user"
            [ 0.0; 10.0; 100.0 ]
            1.0e-10
            3.0
            1.0
        |> expectOk

    let kachanov =
        CreepTableBuilder.generateWithKachanovOmega
            500.0
            100.0
            "Kachanov selected by user"
            10
            100.0
            1.0e-10
            3.0
            1.0
            1.0e-12
            2.0
            1.0
        |> expectOk

    Assert.Equal(GeneratedNortonPowerLaw, norton.Source)
    Assert.Contains("does not model a complete", norton.ApplicabilityWarning)
    Assert.Equal(GeneratedKachanovOmega, kachanov.Source)
    Assert.Contains("neglects primary creep", kachanov.ApplicabilityWarning)

    let stored =
        createTestMaterial ()
        |> CreepTableBuilder.addOrReplaceTable norton
        |> expectOk
        |> CreepTableBuilder.addOrReplaceTable kachanov
        |> expectOk

    Assert.Single(stored.StrengthProperties.CreepTables) |> ignore
    Assert.Equal(GeneratedKachanovOmega, stored.StrengthProperties.CreepTables.Head.Source)

[<Fact>]
let ``numerical generators reject unsafe allocation and invalid ranges`` () =
    Assert.True(
        KachanovOmega.omegaEvolution 1.0e-9 2.0 1.0 100.0 1_000_001 1000.0
        |> Result.isError
    )

    Assert.Empty(TemperatureGrid.toList (CustomRange(0.0, 1.0e12, 1.0e-9)))
    Assert.Empty(TemperatureGrid.toList (CustomRange(System.Double.NaN, 100.0, 1.0)))

[<Fact>]
let ``Garofalo activation-energy form applies temperature correction`` () =
    let A, n, m, alpha, stress, time = 1.0e-8, 2.0, 1.0, 0.01, 100.0, 10.0
    let q, temperature = 100000.0, 500.0
    let calibrated = GarofaloModel.creepStrain A n m alpha stress time |> expectOk

    let corrected =
        GarofaloModel.creepStrainWithActivationEnergy A n m alpha q temperature stress time
        |> expectOk

    let expected = calibrated * exp (-q / (8.31446261815324 * (temperature + 273.15)))
    Assert.Equal(expected, corrected, 15)
    Assert.True(
        GarofaloModel.creepStrainWithActivationEnergy A n m alpha q -273.15 stress time
        |> Result.isError
    )

[<Fact>]
let ``specialized table validators reject invalid domain values`` () =
    let invalidExternalBase =
        PropertyTable.create1D
            "Invalid external pressure"
            "Factor A"
            ""
            "Allowable Compressive Stress"
            "MPa"
            ReturnError
            [ { X = 0.0; Value = 100.0 }; { X = 0.1; Value = 200.0 } ]
        |> expectOk

    let invalidExternal =
        ExternalPressureTable.create invalidExternalBase 500.0 None MaterialDatabase None

    Assert.True(ExternalPressureTable.validate invalidExternal |> Result.isError)

    let creepBase =
        PropertyTable.create1D
            "Creep"
            "Time"
            "h"
            "Strain"
            "%"
            ReturnError
            [ { X = 0.0; Value = 0.0 }; { X = 1.0; Value = 0.1 } ]
        |> expectOk

    let missingStress =
        CreepTable.createWithAppliedStress creepBase 500.0 None CreepDatabase None

    Assert.True(CreepTable.validate missingStress |> Result.isError)

[<Fact>]
let ``configuration save validates before touching destination`` () =
    let path = Path.Combine(Path.GetTempPath(), $"material-library-{System.Guid.NewGuid():N}.xml")
    let valid = Configuration.createDefault ()
    let invalid = { valid with Creep = { valid.Creep with KachanovTimeSteps = 0 } }

    try
        Assert.True(Configuration.save path invalid |> Result.isError)
        Assert.False(File.Exists(path))
        Assert.True(Configuration.save path valid |> Result.isOk)
        Assert.True(Configuration.load path |> Result.isOk)
    finally
        if File.Exists(path) then
            File.Delete(path)

[<Fact>]
let ``XML data helpers read and write staged files`` () =
    let dataRoot = Path.Combine(Path.GetTempPath(), $"material-library-data-{System.Guid.NewGuid():N}")
    let document = XDocument(XElement("PhysicalPropertyTableImport", XAttribute("targetTable", "DensityTable")))

    try
        let written =
            MaterialLibraryDataXml.writeFile dataRoot "physical-properties-xml/Density/PRD-Density.xml" document
            |> expectOk

        Assert.True(File.Exists(written))

        let file =
            MaterialLibraryDataXml.readFile dataRoot "physical-properties-xml/Density/PRD-Density.xml"
            |> expectOk

        Assert.Equal("physical-properties-xml/Density/PRD-Density.xml", file.RelativePath)
        Assert.Equal("Density", file.Folder)
        Assert.Equal("PRD-Density.xml", file.FileName)
        Assert.Equal("PhysicalPropertyTableImport", file.RootName)
        Assert.Equal("DensityTable", file.RootAttributes["targetTable"])

        let library = MaterialLibrary.empty ()

        let fileFromLibrary =
            library.ReadXmlDataFile(dataRoot, "physical-properties-xml/Density/PRD-Density.xml")
            |> expectOk

        Assert.Equal(file.RelativePath, fileFromLibrary.RelativePath)

        let folder =
            MaterialLibraryDataXml.readFolder dataRoot "physical-properties-xml"
            |> expectOk

        Assert.Single(folder) |> ignore
    finally
        if Directory.Exists(dataRoot) then
            Directory.Delete(dataRoot, true)

[<Fact>]
let ``XML data helpers read repository physical-property staging folder`` () =
    match MaterialLibraryDataXml.tryFindDefaultDataRoot () with
    | None -> Assert.True(true)
    | Some dataRoot ->
        let files =
            MaterialLibraryDataXml.readFolder dataRoot "physical-properties-xml"
            |> expectOk

        Assert.Contains(files, fun file -> file.RelativePath = "physical-properties-xml/ThermalExpansion/TE-1.xml")
        Assert.Contains(files, fun file -> file.RelativePath = "physical-properties-xml/ThermalDiffusivity/TCD-ThermalDiffusivity.xml")
        Assert.Contains(files, fun file -> file.RelativePath = "physical-properties-xml/PoissonRatio/PRD-PoissonRatio.xml")

[<Fact>]
let ``CRUD repository creates reads updates deletes and persists materials`` () =
    let path = Path.Combine(Path.GetTempPath(), $"material-library-crud-{System.Guid.NewGuid():N}.json")
    let repo = MaterialCrudRepository()
    let material = createTestMaterial ()

    try
        Assert.True(repo.Create(material) |> Result.isOk)
        Assert.Equal(material.Id, (repo.Read(material.Id) |> expectOk).Id)

        Assert.True(
            repo.Update(material.Id, fun current -> Ok { current with Grade = "71" })
            |> Result.isOk
        )

        Assert.Equal("71", (repo.Read(material.Id) |> expectOk).Grade)
        Assert.True(repo.SaveToFile(path, "test", None) |> Result.isOk)

        let loaded = MaterialCrudRepository.LoadFromFile(path) |> expectOk
        Assert.Equal("71", (loaded.Read(material.Id) |> expectOk).Grade)

        Assert.True(repo.Delete(material.Id) |> Result.isOk)
        Assert.True(repo.Read(material.Id) |> Result.isError)
    finally
        if File.Exists(path) then
            File.Delete(path)

[<Fact>]
let ``CRUD configuration can save read and delete XML config`` () =
    let path = Path.Combine(Path.GetTempPath(), $"material-library-crud-{System.Guid.NewGuid():N}.xml")

    try
        let config = ConfigurationCrud.createDefault ()
        Assert.True(ConfigurationCrud.save path config |> Result.isOk)
        Assert.Equal("ASME_Materials.db", (ConfigurationCrud.read path |> expectOk).Io.AsmeMaterialDatabaseFile)
        Assert.True(ConfigurationCrud.delete path |> Result.isOk)
        Assert.False(File.Exists(path))
    finally
        if File.Exists(path) then
            File.Delete(path)

[<Fact>]
let ``CRUD XML import stores staged XML inside specific material`` () =
    let dataRoot = Path.Combine(Path.GetTempPath(), $"material-library-crud-data-{System.Guid.NewGuid():N}")
    let sourceRelativePath = "physical-properties-xml/Density/PRD-Density.xml"
    let source = XDocument(XElement("PhysicalPropertyTableImport", XAttribute("targetTable", "DensityTable")))
    let repo = MaterialCrudRepository([ createTestMaterial () ])

    try
        MaterialLibraryDataXml.writeFile dataRoot sourceRelativePath source |> expectOk |> ignore

        Assert.True(
            repo.ImportXmlDataIntoMaterial(dataRoot, "SA-516-70", sourceRelativePath)
            |> Result.isOk
        )

        let updated = repo.Read("SA-516-70") |> expectOk
        Assert.Contains("materials/SA-516-70/PRD-Density.xml", defaultArg updated.Notes "")

        let exportFolder = Path.Combine(dataRoot, "export")
        let exported = repo.ExportMaterialXmlData(dataRoot, "SA-516-70", exportFolder) |> expectOk

        Assert.Single(exported) |> ignore
        Assert.True(File.Exists(exported.Head))
    finally
        if Directory.Exists(dataRoot) then
            Directory.Delete(dataRoot, true)

[<Fact>]
let ``XML data helpers reject paths outside data root`` () =
    let dataRoot = Path.Combine(Path.GetTempPath(), $"material-library-safe-data-{System.Guid.NewGuid():N}")

    try
        Directory.CreateDirectory(dataRoot) |> ignore
        let document = XDocument(XElement("PhysicalPropertyTableImport"))
        Assert.True(MaterialLibraryDataXml.writeFile dataRoot "../escape.xml" document |> Result.isError)
        Assert.True(MaterialLibraryDataXml.readFile dataRoot "../escape.xml" |> Result.isError)
    finally
        if Directory.Exists(dataRoot) then
            Directory.Delete(dataRoot, true)

[<Fact>]
let ``CRUD XML batch import preserves source order`` () =
    let dataRoot = Path.Combine(Path.GetTempPath(), $"material-library-crud-batch-{System.Guid.NewGuid():N}")
    let material = createTestMaterial ()

    try
        [ "physical-properties-xml/Density/PRD-Density.xml"; "physical-properties-xml/PoissonRatio/PRD-PoissonRatio.xml" ]
        |> List.iter (fun path ->
            MaterialLibraryDataXml.writeFile dataRoot path (XDocument(XElement("PhysicalPropertyTableImport")))
            |> expectOk
            |> ignore)

        let _, imported =
            XmlDataCrud.importFilesIntoMaterial
                dataRoot
                material
                [ "physical-properties-xml/Density/PRD-Density.xml"
                  "physical-properties-xml/PoissonRatio/PRD-PoissonRatio.xml" ]
            |> expectOk

        Assert.Equal("materials/SA-516-70/PRD-Density.xml", imported[0].RelativePath)
        Assert.Equal("materials/SA-516-70/PRD-PoissonRatio.xml", imported[1].RelativePath)
    finally
        if Directory.Exists(dataRoot) then
            Directory.Delete(dataRoot, true)

[<Fact>]
let ``Schema reads every version in its supported window and refuses older ones`` () =
    // The physical-properties record is serialized directly, so a new optional table appears in
    // JSON automatically. A document written before one existed must still load, with the field None.
    let material = createTestMaterial ()
    let current = MaterialSerialization.toJsonString material
    let currentVersion = MaterialSerialization.CurrentSchemaVersion
    let oldestReadable = MaterialSerialization.MinimumReadableSchemaVersion

    Assert.Contains("ThermalDiffusivityTable", current)
    Assert.True(oldestReadable <= currentVersion, "the read window must not be empty")

    // Written with the current version; substituting is how each older version is exercised.
    let stamp version =
        current.Replace($"\"schemaVersion\":{currentVersion}", $"\"schemaVersion\":{version}")

    Assert.NotEqual<string>(current, stamp (currentVersion - 1))

    for version in oldestReadable .. currentVersion do
        match MaterialSerialization.fromJsonStringComplete (stamp version) with
        | Ok reloaded -> Assert.Equal(material.Id, reloaded.Id)
        | Error error -> failwithf "version %d is inside the read window but failed: %A" version error

    // Anything genuinely older than the supported window is still refused.
    Assert.True(
        MaterialSerialization.fromJsonStringComplete (stamp (oldestReadable - 1))
        |> Result.isError
    )

[<Fact>]
let ``Thermal diffusivity round-trips through JSON`` () =
    let material = createTestMaterial ()

    let withDiffusivity =
        PhysicalPropertyCrud.setThermalDiffusivity (Some [ 20.0, 1.81e-5; 300.0, 1.42e-5 ]) material

    match MaterialSerialization.fromJsonStringComplete (MaterialSerialization.toJsonString withDiffusivity) with
    | Ok reloaded ->
        match reloaded.PhysicalProperties.ThermalDiffusivityTable with
        | Some rows ->
            Assert.Equal(2, rows.Length)
            Assert.Equal(1.81e-5, snd rows.Head, 12)
        | None -> failwith "thermal diffusivity was lost in the round trip"
    | Error error -> failwithf "reload failed: %A" error

// ── Room-temperature tensile test, and the size-grouped strength curves ──────

[<Fact>]
let ``size band honours its inclusive flags at the boundary`` () =
    // "up to 5 incl." and "over 5" are adjacent ASME bands. If both ends were treated as inclusive,
    // a 5 mm section would match both and the caller would silently get whichever came first.
    let upTo5 = SizeThicknessRange.create None true (Some 5.0) true
    let over5 = SizeThicknessRange.create (Some 5.0) false None true

    Assert.True(SizeThicknessRange.contains 5.0 upTo5)
    Assert.False(SizeThicknessRange.contains 5.0 over5)
    Assert.False(SizeThicknessRange.contains 5.1 upTo5)
    Assert.True(SizeThicknessRange.contains 5.1 over5)

    Assert.True(SizeThicknessRange.isUnbounded SizeThicknessRange.all)
    Assert.Equal("All sizes", SizeThicknessRange.describe SizeThicknessRange.all)
    Assert.Equal("up to 5 mm incl.", SizeThicknessRange.describe upTo5)
    Assert.Equal("over 5 mm", SizeThicknessRange.describe over5)

[<Fact>]
let ``reference materials keep every published Sy and Su size group`` () =
    let databasePath =
        Configuration.createDefault ()
        |> Configuration.getAsmeDatabasePath

    // SA-325 bolting publishes two diameter bands, and derates the heavier one.
    let material = AsmeMaterialRepository.findById databasePath 260L |> expectOk
    let datasets = material.StrengthProperties.TensileStrengthDatasets

    Assert.Equal(4, datasets.Length)

    let yieldBands =
        datasets
        |> List.filter (fun dataset -> dataset.Kind = YieldStrengthSy)
        |> List.map (fun dataset -> SizeThicknessRange.describe dataset.SizeRange)

    Assert.Equal<string list>([ "13 to 25 mm incl."; "29 to 38 mm incl." ], yieldBands)

    let strengthAt40 kind band =
        datasets
        |> List.find (fun dataset ->
            dataset.Kind = kind && SizeThicknessRange.contains band dataset.SizeRange)
        |> fun dataset -> (PropertyTable.lookup1D 40.0 dataset.Table |> expectOk).Value

    // The heavier band is the weaker one; that is the whole reason the grouping has to survive.
    Assert.True(strengthAt40 YieldStrengthSy 30.0 < strengthAt40 YieldStrengthSy 20.0)
    Assert.True(strengthAt40 UltimateTensileStrengthSu 30.0 < strengthAt40 UltimateTensileStrengthSu 20.0)

    // The governing flat curve stays available for callers that name no size.
    Assert.NotEmpty(material.StrengthProperties.TensileProperties)

[<Fact>]
let ``allowable stresses carry their division, case, and size band`` () =
    let databasePath =
        Configuration.createDefault ()
        |> Configuration.getAsmeDatabasePath

    // SA-516 70 is tabulated under both Division 1 and Division 2.
    let plate = AsmeMaterialRepository.findById databasePath 177L |> expectOk

    let sources =
        plate.StrengthProperties.AllowableStressDatasets
        |> List.map (fun dataset -> dataset.Source)
        |> List.distinct

    Assert.Contains(Division1AllowableStress, sources)
    Assert.Contains(Division2AllowableStress, sources)

    let stressAt40 source =
        plate.StrengthProperties.AllowableStressDatasets
        |> List.find (fun dataset -> dataset.Source = source)
        |> fun dataset -> (PropertyTable.lookup1D 40.0 dataset.Table |> expectOk).Value

    // Division 2 divides the ultimate strength by a smaller factor, so it always allows more.
    Assert.True(stressAt40 Division1AllowableStress < stressAt40 Division2AllowableStress)

    // Bolting is banded by diameter, and each band keeps its own curve.
    let bolt = AsmeMaterialRepository.findById databasePath 260L |> expectOk
    let boltDatasets = bolt.StrengthProperties.AllowableStressDatasets

    Assert.Equal(2, boltDatasets.Length)
    Assert.All(boltDatasets, fun dataset -> Assert.Equal(BoltingAllowableStress, dataset.Source))
    Assert.All(boltDatasets, fun dataset -> Assert.False(SizeThicknessRange.isUnbounded dataset.SizeRange))

[<Fact>]
let ``division 1 publishes both the normal and the higher alternative allowable stress`` () =
    let databasePath =
        Configuration.createDefault ()
        |> Configuration.getAsmeDatabasePath

    // SA-334 7 carries note G5, which is what marks the higher alternative stress values.
    let material = AsmeMaterialRepository.findById databasePath 736L |> expectOk

    let atSource source =
        material.StrengthProperties.AllowableStressDatasets
        |> List.find (fun dataset -> dataset.Source = source)

    let normal = atSource Division1AllowableStress
    let higher = atSource Division1HighAllowableStress

    Assert.Equal(StandardStrengthAllowableStress, normal.Case)
    Assert.Equal(HighStrengthAllowableStress, higher.Case)
    Assert.Equal("Normal", AllowableStressDataset.caseLabel normal.Case)
    Assert.Equal("High", AllowableStressDataset.caseLabel higher.Case)
    Assert.Equal("VIII-1", AllowableStressDataset.divisionLabel higher.Source)

    let stressAt200 dataset =
        (PropertyTable.lookup1D 200.0 dataset.Table |> expectOk).Value

    Assert.True(stressAt200 normal < stressAt200 higher)

[<Fact>]
let ``room-temperature elongation is stored per rolling direction and stays optional`` () =
    let material = createTestMaterial ()
    let basic = material.BasicProperties

    Assert.Equal(Some 21.0, basic.ElongationLongitudinalPercent)
    Assert.Equal(Some 19.0, basic.ElongationTransversePercent)

    // The governing value is the weaker direction, without assuming which one was measured.
    Assert.Equal(Some 19.0, BasicProperties.governingElongationPercent basic)

    let onlyLongitudinal = BasicProperties.create (Some 21.0) None 55.0 240.0 420.0
    Assert.Equal(Some 21.0, BasicProperties.governingElongationPercent onlyLongitudinal)

    let neither = BasicProperties.create None None 55.0 240.0 420.0
    Assert.Equal(None, BasicProperties.governingElongationPercent neither)

    // The ASME reference tables do not report elongation, so None has to survive a round trip
    // rather than collapsing to zero.
    let reloaded =
        { material with BasicProperties = neither }
        |> MaterialSerialization.toJsonString
        |> MaterialSerialization.fromJsonStringComplete
        |> expectOk

    Assert.Equal(None, reloaded.BasicProperties.ElongationLongitudinalPercent)
    Assert.Equal(None, reloaded.BasicProperties.ElongationTransversePercent)

[<Fact>]
let ``size-grouped strength and allowable datasets survive a JSON round trip`` () =
    let databasePath =
        Configuration.createDefault ()
        |> Configuration.getAsmeDatabasePath

    let material = AsmeMaterialRepository.findById databasePath 260L |> expectOk

    let reloaded =
        material
        |> MaterialSerialization.toJsonString
        |> MaterialSerialization.fromJsonStringComplete
        |> expectOk

    Assert.Equal<TensileStrengthDataset list>(
        material.StrengthProperties.TensileStrengthDatasets,
        reloaded.StrengthProperties.TensileStrengthDatasets
    )

    Assert.Equal<AllowableStressDataset list>(
        material.StrengthProperties.AllowableStressDatasets,
        reloaded.StrengthProperties.AllowableStressDatasets
    )

    // Exclusive bounds are the fragile part: a lost flag would silently widen a band.
    let banded =
        { SizeThicknessRange.all with
            Minimum = Some 5.0
            MinimumIncluded = false }

    let withExclusiveBand =
        { material with
            StrengthProperties =
                { material.StrengthProperties with
                    TensileStrengthDatasets =
                        material.StrengthProperties.TensileStrengthDatasets
                        |> List.map (fun dataset -> { dataset with SizeRange = banded }) } }

    let reloadedBands =
        withExclusiveBand
        |> MaterialSerialization.toJsonString
        |> MaterialSerialization.fromJsonStringComplete
        |> expectOk
        |> fun m -> m.StrengthProperties.TensileStrengthDatasets

    Assert.NotEmpty(reloadedBands)
    Assert.All(reloadedBands, fun dataset -> Assert.False(dataset.SizeRange.MinimumIncluded))
