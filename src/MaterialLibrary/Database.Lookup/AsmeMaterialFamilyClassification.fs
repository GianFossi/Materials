namespace MaterialLibrary.Domain.Database.Lookup

open System
open MaterialLibrary.Domain

module AsmeMaterialFamilyClassification =
    let private normalized (value: string) =
        if String.IsNullOrWhiteSpace value then "" else value.Trim().ToUpperInvariant()

    let private containsAny (values: string list) (text: string) =
        values |> List.exists (fun value -> text.Contains(value, StringComparison.Ordinal))

    let classify specification grade classConditionTemper nominalComposition uns =
        let spec = normalized specification
        let materialGrade = normalized grade
        let condition = normalized classConditionTemper
        let composition = normalized nominalComposition
        let alloy = normalized uns

        if
            containsAny
                [ "S32750"; "S32760"; "S32550"; "S32950"; "S39274"; "S39277"; "S39526" ]
                alloy
        then
            Some SSDPlus
        elif containsAny [ "S31803"; "S32001"; "S32101"; "S32202"; "S32205"; "S32304"; "S32404" ] alloy then
            Some SSD
        elif
            containsAny
                [ "TP304"; "TP304L"; "TP304H"; "TP304N"; "TP316"; "TP316L"; "TP316H"; "TP321"; "TP321H"
                  "TP347"; "TP347H"; "TP348"; "TP309"; "TP310"; "XM-19" ]
                materialGrade
            || containsAny [ "S304"; "S308"; "S309"; "S310"; "S316"; "S317"; "S321"; "S347"; "S348" ] alloy
        then
            Some SSA
        elif
            containsAny [ "403"; "410"; "414"; "416"; "420"; "422"; "431"; "440" ] materialGrade
            || containsAny [ "S403"; "S410"; "S414"; "S416"; "S420"; "S422"; "S431"; "S440" ] alloy
        then
            Some SSM
        elif
            containsAny [ "405"; "409"; "429"; "430"; "434"; "436"; "439"; "444"; "446" ] materialGrade
            || containsAny [ "S405"; "S409"; "S429"; "S430"; "S434"; "S436"; "S439"; "S444"; "S446" ] alloy
        then
            Some SSF
        elif
            containsAny [ "QUENCHED AND TEMPERED"; "QUENCHED & TEMPERED"; "QUENCHED-AND-TEMPERED"; "Q&T" ] condition
            || spec = "SA-517"
        then
            Some QT
        elif containsAny [ "9CR"; "9 CR"; "9.00CR"; "9.00 CR" ] composition then
            Some LAS9_00
        elif containsAny [ "2¼CR"; "2 1/4CR"; "2.25CR" ] composition then
            Some LAS2_25
        elif containsAny [ "1¼CR"; "1 1/4CR"; "1.25CR" ] composition then
            Some LAS1_25
        elif containsAny [ "5CR"; "5.00CR" ] composition then
            Some LAS5_00
        elif containsAny [ "1CR"; "1.00CR" ] composition then
            Some LAS1_00
        elif containsAny [ "SA-333"; "SA-334"; "SA-350"; "SA-352"; "SA-420" ] spec then
            Some LTCS
        elif composition.Contains("CARBON STEEL", StringComparison.Ordinal) then
            Some CS
        else
            None
