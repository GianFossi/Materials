using MaterialLibrary.Domain;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>One selectable entry in the material-family dropdown.</summary>
/// <remarks>
/// WPF cannot bind a <c>ComboBox</c> directly to an F# discriminated union: the union has no
/// enumerable case list, its cases surface to C# as static properties, and <c>ToString()</c> yields
/// the F# identifier (<c>LAS1_00</c>) rather than the ASME code (<c>LAS1.00</c>). This wrapper
/// supplies both the display text and the underlying case, plus a "not assigned" entry standing in
/// for the F# <c>None</c>.
/// </remarks>
public sealed class MaterialFamilyChoice
{
    /// <summary>Creates a choice.</summary>
    /// <param name="display">Text shown in the dropdown.</param>
    /// <param name="value">Underlying union case, or <c>null</c> for the "not assigned" entry.</param>
    private MaterialFamilyChoice(string display, AsmeMaterialFamily? value)
    {
        Display = display;
        Value = value;
    }

    /// <summary>Text shown in the dropdown (the ASME code, or a placeholder when unassigned).</summary>
    public string Display { get; }

    /// <summary>Underlying family case, or <c>null</c> when the material has no family assigned.</summary>
    public AsmeMaterialFamily? Value { get; }

    /// <summary>
    /// Every selectable family, in ASME classification order, preceded by the "not assigned" entry.
    /// </summary>
    /// <remarks>
    /// The case list is written out explicitly because an F# union exposes no runtime enumeration of
    /// its cases that preserves declaration order. Adding a case to <c>AsmeMaterialFamily</c>
    /// therefore requires adding it here too - the trade-off for a stable, ordered dropdown.
    /// Display text always comes from the domain's own <c>AsmeMaterialFamily.code</c>, so the codes
    /// cannot drift from the library.
    /// </remarks>
    public static IReadOnlyList<MaterialFamilyChoice> All { get; } =
    [
        new MaterialFamilyChoice("(not assigned)", null),
        Of(AsmeMaterialFamily.CS),
        Of(AsmeMaterialFamily.QT),
        Of(AsmeMaterialFamily.LTCS),
        Of(AsmeMaterialFamily.LAS1_00),
        Of(AsmeMaterialFamily.LAS1_25),
        Of(AsmeMaterialFamily.LAS2_25),
        Of(AsmeMaterialFamily.LAS5_00),
        Of(AsmeMaterialFamily.LAS9_00),
        Of(AsmeMaterialFamily.SSA),
        Of(AsmeMaterialFamily.SSF),
        Of(AsmeMaterialFamily.SSM),
        Of(AsmeMaterialFamily.SSD),
        Of(AsmeMaterialFamily.SSDPlus),
    ];

    /// <summary>Finds the choice matching a family value.</summary>
    /// <param name="value">Family case to look up, or <c>null</c> for the "not assigned" entry.</param>
    /// <returns>The matching choice; never <c>null</c>, falling back to "not assigned".</returns>
    public static MaterialFamilyChoice For(AsmeMaterialFamily? value)
    {
        // Union cases are singletons with structural equality, so reference/Equals matching is safe.
        foreach (var choice in All)
        {
            if (Equals(choice.Value, value))
            {
                return choice;
            }
        }

        return All[0];
    }

    /// <summary>Builds a choice whose display text is the domain's ASME code for the case.</summary>
    /// <param name="value">Family case to wrap.</param>
    /// <returns>A choice labelled with the ASME code (e.g. <c>LAS1.00</c>, <c>SSD+</c>).</returns>
    private static MaterialFamilyChoice Of(AsmeMaterialFamily value) =>
        new(AsmeMaterialFamilyModule.code(value), value);

    /// <inheritdoc />
    public override string ToString() => Display;
}
