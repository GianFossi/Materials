namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Editor for the Sy (yield strength, MPa) size-ranged table.</summary>
public sealed class SyTableEditorViewModel : SizeRangedTableEditorViewModel
{
    /// <inheritdoc/>
    public override string Title => "Sy — Yield Strength";

    /// <inheritdoc/>
    public override string ValueUnit => "MPa";
}

/// <summary>Editor for the Su (ultimate tensile strength, MPa) size-ranged table.</summary>
public sealed class SuTableEditorViewModel : SizeRangedTableEditorViewModel
{
    /// <inheritdoc/>
    public override string Title => "Su — Ultimate Strength";

    /// <inheritdoc/>
    public override string ValueUnit => "MPa";
}

/// <summary>Editor for the Allowable Stress Div. 1 Normal (MPa) size-ranged table.</summary>
public sealed class AllowableDiv1EditorViewModel : SizeRangedTableEditorViewModel
{
    /// <inheritdoc/>
    public override string Title => "Allowable — Div. 1 Normal";

    /// <inheritdoc/>
    public override string ValueUnit => "MPa";
}

/// <summary>Editor for the Allowable Stress Div. 1 High (MPa) size-ranged table.</summary>
public sealed class AllowableDiv1HighEditorViewModel : SizeRangedTableEditorViewModel
{
    /// <inheritdoc/>
    public override string Title => "Allowable — Div. 1 High";

    /// <inheritdoc/>
    public override string ValueUnit => "MPa";
}

/// <summary>Editor for the Allowable Stress Div. 2 (MPa) size-ranged table.</summary>
public sealed class AllowableDiv2EditorViewModel : SizeRangedTableEditorViewModel
{
    /// <inheritdoc/>
    public override string Title => "Allowable — Div. 2";

    /// <inheritdoc/>
    public override string ValueUnit => "MPa";
}
