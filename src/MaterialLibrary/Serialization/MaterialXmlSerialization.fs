namespace MaterialLibrary

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes
open System.Xml.Linq
open MaterialLibrary.Domain

/// <summary>
/// Lossless conversion between the JSON document model and an XML element tree.
/// </summary>
/// <remarks>
/// <para>
/// The XML representation of a material is defined as a faithful transform of its JSON
/// representation, rather than as a second hand-written mapping from the domain. That guarantees the
/// two file formats always carry exactly the same information: a field added to
/// <c>MaterialSerialization</c> appears in the XML automatically, and the two can never drift apart.
/// </para>
/// <para>
/// Every JSON value kind is preserved through a <c>t</c> attribute, so a round trip
/// JSON -&gt; XML -&gt; JSON is exact:
/// </para>
/// <list type="bullet">
/// <item><c>t="s"</c> string, <c>t="n"</c> number, <c>t="b"</c> boolean, <c>t="z"</c> null.</item>
/// <item><c>t="a"</c> array; its elements are repeated <c>&lt;item&gt;</c> children.</item>
/// <item>No <c>t</c> attribute means an object; its members are named child elements.</item>
/// </list>
/// <para>
/// The attribute is what makes an empty object distinguishable from an empty array and from an
/// empty string, which a naive "elements and text" mapping cannot express.
/// </para>
/// </remarks>
module JsonXmlBridge =

    /// <summary>Name used for the repeated children of a JSON array.</summary>
    [<Literal>]
    let ArrayItemName = "item"

    /// <summary>Name of the attribute carrying the JSON value kind.</summary>
    [<Literal>]
    let TypeAttributeName = "t"

    /// <summary>Escapes a JSON property name into a valid XML element name.</summary>
    /// <param name="name">JSON property name.</param>
    /// <returns>An XML-safe name; characters that are not letters, digits, or separators become '_'.</returns>
    /// <remarks>
    /// The library's JSON property names are camelCase identifiers, so this is a safety net rather
    /// than a routine transformation. It is deliberately not reversible; round-tripping relies on the
    /// names already being XML-safe.
    /// </remarks>
    let private toElementName (name: string) : string =
        if String.IsNullOrWhiteSpace name then
            "_"
        else
            let sanitized =
                name
                |> Seq.mapi (fun index character ->
                    let isValidStart = Char.IsLetter character || character = '_'
                    let isValidPart = isValidStart || Char.IsDigit character || character = '-' || character = '.'

                    if index = 0 then
                        (if isValidStart then character else '_')
                    else
                        (if isValidPart then character else '_'))
                |> Seq.toArray
                |> String

            sanitized

    /// <summary>Formats a JSON number using a round-trippable, culture-independent representation.</summary>
    /// <param name="element">JSON number element.</param>
    /// <returns>The raw text of the number as it appeared in the document.</returns>
    let private numberText (element: JsonElement) : string =
        // GetRawText preserves the original precision and avoids a double round trip.
        element.GetRawText()

    /// <summary>Converts a JSON node into an XML element with the given name.</summary>
    /// <param name="name">Element name to produce.</param>
    /// <param name="node">JSON node to convert; <c>null</c> is written as a null value.</param>
    /// <returns>An XML element carrying the node's value and kind.</returns>
    let rec private nodeToElement (name: string) (node: JsonNode) : XElement =
        let element = XElement(XName.Get(toElementName name))

        // A null reference is how JsonNode represents the JSON null literal.
        if isNull (box node) then
            element.SetAttributeValue(XName.Get TypeAttributeName, "z")
        else
            match node with
            | :? JsonObject as object ->
                // Objects carry no type attribute; their members become child elements.
                for pair in object do
                    element.Add(nodeToElement pair.Key pair.Value)

            | :? JsonArray as array ->
                element.SetAttributeValue(XName.Get TypeAttributeName, "a")

                for item in array do
                    element.Add(nodeToElement ArrayItemName item)

            | _ ->
                let value = node.GetValue<JsonElement>()

                match value.ValueKind with
                | JsonValueKind.String ->
                    element.SetAttributeValue(XName.Get TypeAttributeName, "s")
                    element.Value <- value.GetString()
                | JsonValueKind.Number ->
                    element.SetAttributeValue(XName.Get TypeAttributeName, "n")
                    element.Value <- numberText value
                | JsonValueKind.True ->
                    element.SetAttributeValue(XName.Get TypeAttributeName, "b")
                    element.Value <- "true"
                | JsonValueKind.False ->
                    element.SetAttributeValue(XName.Get TypeAttributeName, "b")
                    element.Value <- "false"
                | _ -> element.SetAttributeValue(XName.Get TypeAttributeName, "z")

        element

    /// <summary>Represents the JSON null value in a way F# can pass to the JsonNode API.</summary>
    /// <remarks>
    /// <c>JsonNode</c> uses a null reference for the JSON <c>null</c> literal, but F# will not let a
    /// class type be assigned <c>null</c> directly under the nullness rules this project compiles
    /// with. Routing through <c>Unchecked.defaultof</c> keeps the intent explicit and confined here.
    /// </remarks>
    let private jsonNull: JsonNode = Unchecked.defaultof<JsonNode>

    /// <summary>Converts an XML element produced by this module back into a JSON node.</summary>
    /// <param name="element">Element to convert.</param>
    /// <returns>The reconstructed JSON node; the JSON null literal for a <c>t="z"</c> element.</returns>
    let rec private elementToNode (element: XElement) : JsonNode =
        let typeAttribute = element.Attribute(XName.Get TypeAttributeName)
        let kind = if isNull typeAttribute then "o" else typeAttribute.Value

        match kind with
        | "z" -> jsonNull
        | "s" -> JsonValue.Create(element.Value) :> JsonNode
        | "b" -> JsonValue.Create(String.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase)) :> JsonNode
        | "n" ->
            // Parse back through the JSON reader so integers stay integers and precision is kept.
            JsonNode.Parse(element.Value.Trim())
        | "a" ->
            let array = JsonArray()

            for child in element.Elements() do
                array.Add(elementToNode child)

            array :> JsonNode
        | _ ->
            let object = JsonObject()

            for child in element.Elements() do
                object[child.Name.LocalName] <- elementToNode child

            object :> JsonNode

    /// <summary>Converts a JSON string into an XML element.</summary>
    /// <param name="rootName">Name of the XML root element.</param>
    /// <param name="json">JSON document text.</param>
    /// <returns><c>Ok element</c>, or an error when the JSON cannot be parsed.</returns>
    let jsonStringToXml (rootName: string) (json: string) : Result<XElement, MaterialError> =
        try
            let node = JsonNode.Parse(json)
            Ok(nodeToElement rootName node)
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "JSON to XML conversion failed: %s" ex.Message))

    /// <summary>Converts an XML element back into a JSON string.</summary>
    /// <param name="element">Element previously produced by <see cref="jsonStringToXml"/>.</param>
    /// <returns><c>Ok json</c>, or an error when the element cannot be converted.</returns>
    let xmlToJsonString (element: XElement) : Result<string, MaterialError> =
        try
            let node = elementToNode element

            if isNull (box node) then Ok "null" else Ok(node.ToJsonString())
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "XML to JSON conversion failed: %s" ex.Message))

/// <summary>
/// Reads and writes a single <see cref="Material"/> as an XML file.
/// </summary>
/// <remarks>
/// The XML carries exactly the same fields, and therefore the same fixed units, as the JSON format
/// documented on <c>MaterialSerialization</c>: temperature in degC, stress and strength in MPa,
/// density in kg/m^3, specific heat in J/(kg*K), thermal conductivity in W/(m*K), thermal expansion
/// coefficient in 1/degC, elongation and reduction of area in percent, time in hours.
/// </remarks>
module MaterialXmlSerialization =

    /// <summary>Root element name for a single material document.</summary>
    [<Literal>]
    let RootElementName = "material"

    /// <summary>Serializes a material to an XML element.</summary>
    /// <param name="material">Material to serialize.</param>
    /// <returns><c>Ok element</c>, or an error when serialization fails.</returns>
    let toXml (material: Material) : Result<XElement, MaterialError> =
        try
            MaterialSerialization.toJsonString material
            |> JsonXmlBridge.jsonStringToXml RootElementName
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "Material XML serialization failed: %s" ex.Message))

    /// <summary>Serializes a material to an indented XML string.</summary>
    /// <param name="material">Material to serialize.</param>
    /// <returns><c>Ok xml</c>, or an error when serialization fails.</returns>
    let toXmlString (material: Material) : Result<string, MaterialError> =
        toXml material
        |> Result.map (fun element -> XDocument(element).ToString())

    /// <summary>Rebuilds a material from an XML element, using the embedded physical properties.</summary>
    /// <param name="element">Element produced by <see cref="toXml"/>.</param>
    /// <returns><c>Ok material</c>, or an error when the document is invalid.</returns>
    let fromXml (element: XElement) : Result<Material, MaterialError> =
        JsonXmlBridge.xmlToJsonString element
        |> Result.bind MaterialSerialization.fromJsonStringComplete

    /// <summary>Rebuilds a material from an XML string.</summary>
    /// <param name="xml">XML document text.</param>
    /// <returns><c>Ok material</c>, or an error when the document is invalid.</returns>
    let fromXmlString (xml: string) : Result<Material, MaterialError> =
        try
            fromXml (XDocument.Parse(xml).Root)
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "XML parse failed: %s" ex.Message))

    /// <summary>Saves a material to an XML file.</summary>
    /// <param name="filePath">Destination file path.</param>
    /// <param name="material">Material to write.</param>
    /// <returns><c>Ok ()</c>, or an error when serialization or the write fails.</returns>
    let saveToFile (filePath: string) (material: Material) : Result<unit, MaterialError> =
        toXmlString material
        |> Result.bind (fun xml ->
            try
                IO.File.WriteAllText(filePath, xml)
                Ok()
            with ex ->
                Error(MaterialError.InvalidOperation(sprintf "File write failed: %s" ex.Message)))

    /// <summary>Loads a material from an XML file.</summary>
    /// <param name="filePath">Source file path.</param>
    /// <returns><c>Ok material</c>, or an error when the file is missing or invalid.</returns>
    let loadFromFile (filePath: string) : Result<Material, MaterialError> =
        try
            IO.File.ReadAllText(filePath) |> fromXmlString
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))

/// <summary>
/// Reads and writes a complete material library (metadata plus materials) as an XML file.
/// </summary>
/// <remarks>
/// Mirrors <c>MaterialLibrarySerialization</c> field for field, including the schema version, so an
/// XML library file and a JSON library file carry identical information and identical units.
/// </remarks>
module MaterialLibraryXmlSerialization =

    /// <summary>Root element name for a material library document.</summary>
    [<Literal>]
    let RootElementName = "materialLibrary"

    /// <summary>Serializes a material library to an XML element.</summary>
    /// <param name="version">Library version string stored in the document.</param>
    /// <param name="description">Optional library description.</param>
    /// <param name="materials">Materials to include.</param>
    /// <returns><c>Ok element</c>, or an error when serialization fails.</returns>
    let toXml
        (version: string)
        (description: string option)
        (materials: Material list)
        : Result<XElement, MaterialError> =
        try
            MaterialLibrarySerialization.toJsonString version description materials
            |> JsonXmlBridge.jsonStringToXml RootElementName
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "Library XML serialization failed: %s" ex.Message))

    /// <summary>Serializes a material library to an indented XML string.</summary>
    /// <param name="version">Library version string stored in the document.</param>
    /// <param name="description">Optional library description.</param>
    /// <param name="materials">Materials to include.</param>
    /// <returns><c>Ok xml</c>, or an error when serialization fails.</returns>
    let toXmlString
        (version: string)
        (description: string option)
        (materials: Material list)
        : Result<string, MaterialError> =
        toXml version description materials
        |> Result.map (fun element -> XDocument(element).ToString())

    /// <summary>Rebuilds the materials of a library from an XML element.</summary>
    /// <param name="element">Element produced by <see cref="toXml"/>.</param>
    /// <returns><c>Ok materials</c>, or an error when the document is invalid.</returns>
    let fromXml (element: XElement) : Result<Material list, MaterialError> =
        JsonXmlBridge.xmlToJsonString element
        |> Result.bind MaterialLibrarySerialization.fromJsonStringComplete

    /// <summary>Rebuilds the materials of a library from an XML string.</summary>
    /// <param name="xml">XML document text.</param>
    /// <returns><c>Ok materials</c>, or an error when the document is invalid.</returns>
    let fromXmlString (xml: string) : Result<Material list, MaterialError> =
        try
            fromXml (XDocument.Parse(xml).Root)
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "XML parse failed: %s" ex.Message))

    /// <summary>Saves a material library to an XML file.</summary>
    /// <param name="filePath">Destination file path.</param>
    /// <param name="version">Library version string stored in the document.</param>
    /// <param name="description">Optional library description.</param>
    /// <param name="materials">Materials to include.</param>
    /// <returns><c>Ok ()</c>, or an error when serialization or the write fails.</returns>
    let saveToFile
        (filePath: string)
        (version: string)
        (description: string option)
        (materials: Material list)
        : Result<unit, MaterialError> =
        toXmlString version description materials
        |> Result.bind (fun xml ->
            try
                IO.File.WriteAllText(filePath, xml)
                Ok()
            with ex ->
                Error(MaterialError.InvalidOperation(sprintf "File write failed: %s" ex.Message)))

    /// <summary>Loads a material library from an XML file.</summary>
    /// <param name="filePath">Source file path.</param>
    /// <returns><c>Ok materials</c>, or an error when the file is missing or invalid.</returns>
    let loadFromFile (filePath: string) : Result<Material list, MaterialError> =
        try
            IO.File.ReadAllText(filePath) |> fromXmlString
        with ex ->
            Error(MaterialError.InvalidOperation(sprintf "File read failed: %s" ex.Message))
