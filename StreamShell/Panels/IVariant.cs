namespace StreamShell;

/// <summary>
/// An entry in a <see cref="SelectionPanel"/>. Can be either a selectable
/// <see cref="IVariant"/> or a non-selectable <see cref="IDecoration"/>.
/// </summary>
public interface IVariantEntry
{
    /// <summary>Display name. May contain Spectre markup tags.</summary>
    string Name { get; }
}

/// <summary>
/// A non-selectable decoration rendered between variants.
/// Skipped during arrow key navigation.
/// </summary>
public interface IDecoration : IVariantEntry { }

/// <summary>
/// An item selectable in a <see cref="SelectionPanel"/>.
/// <see cref="Name"/> supports Spectre.Console markup.
/// </summary>
public interface IVariant : IVariantEntry { }
