namespace MaterialLibrary.Domain.Database.Lookup

open System
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain

/// <summary>Hydrate native 2025 records into the existing material API without consulting the archived edition.</summary>
module Asme2025Repository =
    /// <summary>Convert a normalized size interval in mm, retaining open and closed boundaries.</summary>
    let private column (curve: Asme2025Curve) : TableColumn =
        let bound (v: Nullable<float>) included : SizeRangeBound option = if v.HasValue then Some {Value=v.Value;Inclusion=if included then Inclusive else Exclusive} else None
        { SizeRange={Lower=bound curve.SizeMinimum curve.MinimumIncluded;Upper=bound curve.SizeMaximum curve.MaximumIncluded;Label=Some $"{curve.ProductForm} / {curve.SourceTable} / curve {curve.Id}"}
          Entries=curve.Points |> Array.map (fun p -> PropertyTable.entry p.Temperature p.Value) |> Array.toList }

    /// <summary>Build a temperature table in MPa; empty source curves remain unavailable.</summary>
    let private table name (curves: Asme2025Curve list) : PropertyTable option =
        let columns = curves |> List.filter (fun c -> not (Array.isEmpty c.Points)) |> List.map column
        if columns.IsEmpty then None
        else
            // Overlapping source variants remain separate columns. Native Excel lookups require explicit selection.
            Some { Name=name;XAxisName="Temperature";XAxisUnit="degC";YAxisName=name;ValueUnit="MPa";DimensionType=Length;DimensionUnit="mm";
                   Columns=columns;XBoundaryPolicy=ReturnError }

    /// <summary>Read one MAIN identity with strengths, source-specific allowable stresses and physical data.</summary>
    let hydrate (db: SqliteConnection) (identity: Asme2025Material) : Material =
        let curves code = Asme2025Data.curves db identity.Id code
        let scalar column = Asme2025Data.query db $"SELECT {column} FROM Main WHERE id=$id" ["$id",box identity.Id] |> List.head |> Array.head |> fun value -> if isNull value then 0. else Convert.ToDouble value
        let points code =
            match curves code |> List.filter (fun c -> c.Points.Length>0) with
            | [one] -> one.Points |> Array.toList
            | _ -> []
        let constants = Asme2025Data.query db "SELECT ConstantDensity,ConstantPoissonRatio FROM Extension WHERE id_main=$id" ["$id",box identity.Id] |> List.tryHead
        let constant i = constants |> Option.bind (fun row -> if isNull row[i] then None else Some(Convert.ToDouble row[i]))
        let density = match points "density" with [] -> constant 0 |> Option.map (fun value -> [{Temperature=20.;Value=value}]) |> Option.defaultValue [] | ps -> ps
        let nu temperature = points "Nu" |> List.tryFind (fun p -> p.Temperature=temperature) |> Option.map (fun p -> p.Value) |> Option.orElseWith (fun () -> constant 1)
        let optionalPairs code = match points code with [] -> None | ps -> Some(ps |> List.map (fun p -> p.Temperature,p.Value))
        let physical = PhysicalPropertiesTable.create None
                            (points "Alpha" |> List.map (fun p -> { Temperature=p.Temperature;ExpansionCoefficient=p.Value }))
                            (points "E" |> List.map (fun p -> { Temperature=p.Temperature;ElasticModulus=p.Value;PoissonRatio=nu p.Temperature }))
                            (match points "c" with [] -> None | ps -> Some(ps |> List.map (fun p -> { Temperature=p.Temperature;SpecificHeat=p.Value })))
                            (density |> List.map (fun p -> { Temperature=p.Temperature;Density=p.Value }))
                            (optionalPairs "k") (optionalPairs "a")
        let basic = BasicProperties.create None None 0. (scalar "SMYS") (scalar "SMUS")
        let material = Material.create (string identity.Id) (Material.composeMaterialName identity.Specification identity.Grade identity.Condition identity.UNS) identity.Specification identity.Grade basic physical
                       |> Material.setIdentity "" identity.NominalComposition identity.Specification identity.Grade identity.Condition identity.UNS
        let source code = match code with "S1" -> Division1AllowableStress | "S2" -> Division2AllowableStress | "Sb1" -> BoltingAllowableStress | _ -> BoltingDivision2AllowableStress
        let references (curve: Asme2025Curve) : AsmeNoteReference list =
            let table = match curve.SourceTable with "1A" -> Some Table1A | "1B" -> Some Table1B | "5A" -> Some Table5A | "5B" -> Some Table5B | "3" -> Some TableSBolting | "4" -> Some TableSBolting2 | _ -> None
            table |> Option.map (fun table -> Asme2025Data.query db "SELECT DISTINCT Code FROM VW_Property_Notes_All WHERE id_property=$id AND Kind='Specific' ORDER BY Code" ["$id",box curve.Id] |> List.map (fun row -> ({Table=table;Code=string row[0]}: AsmeNoteReference))) |> Option.defaultValue []
        let allowable : AllowableStressDataset list = ["S1";"S2";"Sb1";"Sb2"] |> List.collect (fun code -> curves code |> List.choose (fun c ->
            table code [c] |> Option.map (fun t ->
                { DatabaseRowId=c.Id;Source=source code;Case=StandardStrengthAllowableStress;Table=t;
                  SizeMinimum=if c.SizeMinimum.HasValue then Some c.SizeMinimum.Value else None;
                  SizeMaximum=if c.SizeMaximum.HasValue then Some c.SizeMaximum.Value else None;
                  MaximumTemperature=None;CreepTemperature=None;AsmeNoteReferences=references c;Notes=Some(c.SourceNotes + "\n" + c.UserNotes) })))
        let regulatory = Asme2025Data.query db "SELECT P_Number,Group_Number,Max_Temp_Sect_I,Max_Temp_Sect_VIII_1,Max_Temp_Sect_VIII_2 FROM ASME_Data WHERE id_main=$id" ["$id",box identity.Id] |> List.tryHead
        let temperature index = regulatory |> Option.bind (fun r -> if isNull r[index] then None else Some(Convert.ToDouble r[index]))
        let pressure = Asme2025Data.pressureCurves db identity.Id |> List.choose (fun (_,name,temperature,points) ->
            if points.IsEmpty then None
            else PropertyTable.create1D name "Factor A" "dimensionless" "Factor B" "MPa" ReturnError (points |> List.map (fun (a,b) -> PropertyTable.entry a b))
                 |> Result.toOption |> Option.map (fun table -> ExternalPressureTable.create table temperature None MaterialDatabase None))
        { material with
            Family=AsmeMaterialFamilyClassification.classify identity.Specification identity.Grade identity.Condition identity.NominalComposition identity.UNS
            ApplicableAsmeCodes=allowable |> List.collect (fun d -> match d.Source with Division1AllowableStress | Division1HighAllowableStress | BoltingAllowableStress -> [AsmeSectionI;AsmeSectionVIII1] | _ -> [AsmeSectionVIII2]) |> List.distinct
            StrengthProperties={material.StrengthProperties with SyTable=table "Sy" (curves "Sy");SuTable=table "Su" (curves "Su");AllowableStressDatasets=allowable;ExternalPressureTables=pressure}
            MaximumAllowableTemperature={AsmeViiiI=temperature 2;AsmeViii1=temperature 3;AsmeViii2=temperature 4}
            WeldingInfo=regulatory |> Option.bind (fun r -> if isNull r[0] && isNull r[1] then None else Some {PNumber=string r[0];GNumber=string r[1]}) }
