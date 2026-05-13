namespace StreamShell;

/// <summary>
/// Shared scroll navigation for list-based panels.
/// Tracks selection position within a scrollable viewport and handles
/// Up/Down arrow navigation with automatic scrolling when items exceed
/// the visible capacity.
///
/// Used by both <see cref="CommandPalette"/> and <see cref="SelectionPanel"/>
/// to keep scroll behavior consistent (DRY).
/// </summary>
internal class ScrollNavigator
{
    /// <summary>Index of the selected item within the visible window (0-based).</summary>
    public int SelectedIndex { get; set; }

    /// <summary>
    /// Offset applied to the item list — how many items are scrolled past
    /// before the visible window starts.
    /// </summary>
    public int ScrollOffset { get; set; }

    private int _totalItems;
    /// <summary>Total number of items in the full list.</summary>
    public int TotalItems
    {
        get => _totalItems;
        set
        {
            _totalItems = value;
            ClampToBounds();
        }
    }

    private int _visibleCapacity;
    /// <summary>Maximum number of items visible at once in the viewport.</summary>
    public int VisibleCapacity
    {
        get => _visibleCapacity;
        set
        {
            _visibleCapacity = value;
            ClampToBounds();
        }
    }

    /// <summary>True when there are more items than fit in the viewport.</summary>
    public bool IsScrolling => TotalItems > VisibleCapacity;

    /// <summary>First visible item index (inclusive).</summary>
    public int VisibleStart => ScrollOffset;

    /// <summary>Last visible item index (exclusive).</summary>
    public int VisibleEnd => Math.Min(ScrollOffset + VisibleCapacity, TotalItems);

    /// <summary>Number of items currently visible.</summary>
    public int VisibleCount => VisibleEnd - VisibleStart;

    /// <summary>
    /// Adjusts selection by <paramref name="delta"/> (+1 for Down, -1 for Up).
    /// Handles viewport scrolling when the selection reaches an edge.
    /// Returns true if the visible state actually changed.
    /// </summary>
    public bool AdjustSelection(int delta)
    {
        if (TotalItems <= 0 || VisibleCapacity <= 0)
            return false;

        int oldIndex = SelectedIndex;
        int oldScroll = ScrollOffset;

        int visibleCount = Math.Min(VisibleCapacity, TotalItems - ScrollOffset);
        int newIndex = SelectedIndex + delta;

        if (newIndex >= visibleCount && delta > 0)
        {
            // Past bottom of viewport — scroll down if more items exist
            if (TotalItems > VisibleCapacity && ScrollOffset + VisibleCapacity < TotalItems)
            {
                ScrollOffset = Math.Min(ScrollOffset + delta, TotalItems - VisibleCapacity);
                SelectedIndex = Math.Min(SelectedIndex, Math.Min(VisibleCapacity, TotalItems - ScrollOffset) - 1);
            }
        }
        else if (newIndex < 0 && delta < 0)
        {
            // Past top of viewport — scroll up
            if (ScrollOffset > 0)
            {
                ScrollOffset = Math.Max(0, ScrollOffset + delta);
                SelectedIndex = 0;
            }
        }
        else
        {
            SelectedIndex = Math.Clamp(newIndex, 0, visibleCount - 1);
        }

        return SelectedIndex != oldIndex || ScrollOffset != oldScroll;
    }

    /// <summary>Resets selection and scroll to the start.</summary>
    public void Reset()
    {
        SelectedIndex = 0;
        ScrollOffset = 0;
    }

    /// <summary>
    /// Clamps selection and scroll offset to valid ranges.
    /// Call after externally changing TotalItems or VisibleCapacity.
    /// </summary>
    public void ClampToBounds()
    {
        ScrollOffset = Math.Max(0, Math.Min(ScrollOffset, Math.Max(0, TotalItems - VisibleCapacity)));
        int visible = Math.Min(VisibleCapacity, TotalItems - ScrollOffset);
        if (visible > 0)
            SelectedIndex = Math.Max(0, Math.Min(SelectedIndex, visible - 1));
        else
            SelectedIndex = 0;
    }
}
