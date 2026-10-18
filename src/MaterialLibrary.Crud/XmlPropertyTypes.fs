namespace MaterialLibrary.Crud

open System

/// <summary>One physical or external-pressure point: temperature Celsius, X=temperature or dimensionless A, Y=fixed SI value or B in MPa.</summary>
[<CLIMutable>]
type XmlPropertyPoint = { Temperature: float; X: float; Y: float }

/// <summary>A source record with independent identity, units, full XML and mandatory extraction-review status.</summary>
[<CLIMutable>]
type XmlPropertyRecord = {
    Key: string; File: string; SHA256: string; Xml: string; Description: string
    PropertyCode: string; Unit: string; ChartName: string
    Specification: string; Grade: string; Condition: string; UNS: string; Composition: string
    Notes: string; RawText: string; RequiresReview: bool; Diagnostic: string
    IsConstant: bool; Points: XmlPropertyPoint array
}

/// <summary>One reviewable material/property association; no database writes occur while proposing candidates.</summary>
[<CLIMutable>]
type XmlPropertyProposal = {
    MaterialId: int64; Specification: string; Grade: string; Condition: string; UNS: string; Composition: string
    RecordKey: string; PropertyCode: string; Score: int; Reason: string; Selected: bool
}

/// <summary>Approved record and target; replacement is explicit and the preview fingerprint guards against concurrent changes.</summary>
[<CLIMutable>]
type XmlPropertyAssignment = {
    MaterialId: int64; Record: XmlPropertyRecord; Reviewed: bool; ReplaceExisting: bool; ExpectedVersion: string
}
