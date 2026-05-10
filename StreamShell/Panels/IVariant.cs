namespace StreamShell;

/// <summary>
/// An item selectable in a <see cref="SelectionPanel"/>.
/// <see cref="Name"/> supports Spectre.Console markup.
/// </summary>
public interface IVariant
{
    /// <summary>Display name. May contain Spectre markup tags.</summary>
    string Name { get; }
}
