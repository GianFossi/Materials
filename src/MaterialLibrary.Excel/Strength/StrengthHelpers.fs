namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

/// <summary>Helpers shared by the strength worksheet-function modules.</summary>
/// <remarks>
/// Auto-opened and internal so every module in the split can use it. A <c>private</c> helper is
/// scoped to its own module, so splitting the original single module made these unreachable.
/// </remarks>
[<AutoOpen>]
module internal StrengthHelpers =
    let selectByTemperature
        (label: string)
        (temperature: float)
        (rows: (float * 'a) list)
        : Result<'a, MaterialError> =
        match rows |> List.tryFind (fun (t, _) -> t = temperature) with
        | Some(_, row) -> Ok row
        | None ->
            let available =
                rows |> List.map fst |> List.sort |> List.map (sprintf "%.4g") |> String.concat ", "

            Error(
                MaterialError.InvalidOperation(
                    sprintf "No stored %s data at %.4g degC; available: %s" label temperature available
                )
            )

    // ── Tensile / compression properties vs temperature ──────────────────────
