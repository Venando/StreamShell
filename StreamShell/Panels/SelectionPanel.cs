namespace StreamShell;

/// <summary>
/// Bottom panel that renders a selectable list of variants with scroll support.
/// Used by <see cref="ConsoleAppHost.PromptSelection"/>.
/// When the item count exceeds the configured row count, the panel becomes
/// scrollable with automatic viewport management.
/// </summary>
internal class SelectionPanel : IBottomPanel
{
    private readonly string _title;
    private readonly IVariantEntry[] _variants;
    private readonly SelectionInfo? _info;
    private readonly Action<IVariant[]> _onSubmit;
    private readonly Action _onCancel;

    private readonly bool[] _toggled;
    private readonly bool _preventCancel;
    private int _toggledVersion;
    private int _highlightIndex;          // absolute index into _variants
    private int _scrollOffset;            // first visible variant
    private readonly int _variantCapacity; // how many variant slots fit
    private int _lastRenderHighlight;
    private int _lastRenderScroll;
    private int _lastToggledVersion;
    private readonly List<string> _cachedLines = new();

    private volatile bool _isDirty;
    bool IBottomPanel.IsDirty => _isDirty;
    void IBottomPanel.ClearDirty() => _isDirty = false;

    /// <summary>Total lines: controls + title + variant capacity (constant, padded with empty).</summary>
    public int LineCount => 2 + _variantCapacity;

    /// <summary>Hides the input field while selection is active.</summary>
    bool IBottomPanel.AllowUserField => false;

    /// <summary>True in multi-select mode (info provided with Max > 1).</summary>
    private bool IsMulti => _info is not null && _info.Max > 1;

    /// <summary>True when the panel needs scrolling (more items than visible slots).</summary>
    private bool IsScrolling => _variants.Length > _variantCapacity;

    /// <summary>Returns true if the entry is a selectable variant (not a decoration).</summary>
    private static bool IsSelectable(IVariantEntry entry) => entry is IVariant;

    /// <summary>
    /// Creates a selection panel.
    /// </summary>
    public SelectionPanel(string title, IVariantEntry[] variants, SelectionInfo? info,
        Action<IVariant[]> onSubmit, Action onCancel, int consoleHeight)
    {
        _title = title;
        _variants = variants;
        _info = info;
        _onSubmit = onSubmit;
        _onCancel = onCancel;
        _toggled = new bool[variants.Length];
        _preventCancel = info?.PreventCancel ?? false;

        int effectiveRows = info?.GetEffectiveRows(variants.Length, consoleHeight) ?? 8;
        _variantCapacity = Math.Max(1, effectiveRows - 2); // reserve 2 for controls + title
        _isDirty = true;
    }

    // ── Interface: GetLines ──────────────────────────────────────────

    /// <summary>
    /// Returns panel lines. <paramref name="currentInput"/> is ignored.
    /// Renders only the visible slice when scrolling is active.
    /// </summary>
    public IReadOnlyList<string> GetLines(string currentInput)
    {
        if (_highlightIndex == _lastRenderHighlight
            && _scrollOffset == _lastRenderScroll
            && _toggledVersion == _lastToggledVersion
            && _cachedLines.Count > 0)
            return _cachedLines;

        _cachedLines.Clear();

        int visibleEnd = Math.Min(_scrollOffset + _variantCapacity, _variants.Length);

        // Line 0: control scheme (varies by mode + scroll info)
        string controls = BuildControlsLine();
        _cachedLines.Add(controls);

        // Line 1: title
        _cachedLines.Add($"[bold]{_title}[/]");

        // Entry lines — only the visible slice
        for (int i = _scrollOffset; i < visibleEnd; i++)
        {
            if (_variants[i] is IDecoration)
            {
                _cachedLines.Add($"[grey]{_variants[i].Name}[/]");
                continue;
            }

            bool highlighted = i == _highlightIndex;
            bool selected = IsMulti && _toggled[i];

            string checkMark = selected ? "\u25a3 " : "\u2610 ";
            string arrow = highlighted ? "> " : "  ";
            string color = highlighted ? "white" : "grey";

            string line = IsMulti
                ? $"{arrow}{checkMark}[{color}]{_variants[i].Name}[/]"
                : $"{arrow}[{color}]{_variants[i].Name}[/]";
            _cachedLines.Add(line);
        }

        // Pad to LineCount with empty lines
        while (_cachedLines.Count < LineCount)
            _cachedLines.Add(string.Empty);

        _lastRenderHighlight = _highlightIndex;
        _lastRenderScroll = _scrollOffset;
        _lastToggledVersion = _toggledVersion;
        return _cachedLines;
    }

    private string BuildControlsLine()
    {
        string scrollInfo = IsScrolling
            ? $"  {_scrollOffset + 1}-{Math.Min(_scrollOffset + _variantCapacity, _variants.Length)}/{_variants.Length}"
            : "";

        if (IsMulti)
            return _preventCancel
                ? $"[dim]\u2191\u2193: navigate  Enter: toggle  Space: submit{scrollInfo}[/]"
                : $"[dim]\u2191\u2193: navigate  Enter: toggle  Space: submit  Esc: cancel{scrollInfo}[/]";

        return _preventCancel
            ? $"[dim]\u2191\u2193: navigate  Enter/Space: submit{scrollInfo}[/]"
            : $"[dim]\u2191\u2193: navigate  Enter/Space: submit  Esc: cancel{scrollInfo}[/]";
    }

    // ── Interface: TryHandleKey ──────────────────────────────────────

    bool IBottomPanel.TryHandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                return HandleUp();

            case ConsoleKey.DownArrow:
                return HandleDown();

            case ConsoleKey.Enter:
                return HandleEnter();

            case ConsoleKey.Spacebar:
                return HandleSubmit();

            case ConsoleKey.Escape:
                if (!_preventCancel)
                    _onCancel();
                return true;
        }

        return false;
    }

    private bool HandleUp()
    {
        int prev = _highlightIndex - 1;
        while (prev >= 0 && !IsSelectable(_variants[prev]))
            prev--;
        if (prev < 0)
            return true; // already at top selectable item

        _highlightIndex = prev;

        // Scroll up if highlight moved above the visible window
        if (_highlightIndex < _scrollOffset)
            _scrollOffset = _highlightIndex;

        _isDirty = true;
        return true;
    }

    private bool HandleDown()
    {
        int next = _highlightIndex + 1;
        while (next < _variants.Length && !IsSelectable(_variants[next]))
            next++;
        if (next >= _variants.Length)
            return true; // already at bottom selectable item

        _highlightIndex = next;

        // Scroll down if highlight moved below the visible window
        if (_highlightIndex >= _scrollOffset + _variantCapacity)
            _scrollOffset = _highlightIndex - _variantCapacity + 1;

        _isDirty = true;
        return true;
    }

    // ── Key handlers ─────────────────────────────────────────────────

    private bool HandleEnter()
    {
        // Defensive: if a decoration somehow got highlighted, ignore
        if (!IsSelectable(_variants[_highlightIndex]))
            return true;

        if (IsMulti)
        {
            int idx = _highlightIndex;

            // Check max limit unless unlimited
            if (!_toggled[idx] && _info!.Max > 0)
            {
                int currentSelected = CountSelected(_toggled);
                if (currentSelected >= _info.Max)
                    return true; // block toggle, already at max
            }

            _toggled[idx] = !_toggled[idx];
            _toggledVersion++;
            _isDirty = true;

            // If max is 1, selecting also submits immediately
            if (_toggled[idx] && _info!.Max == 1)
                return HandleSubmit();

            return true;
        }

        // Single — select highlighted and submit
        _onSubmit([(IVariant)_variants[_highlightIndex]]);
        return true;
    }

    private bool HandleSubmit()
    {
        // Check minimum constraint
        if (IsMulti && _info!.Min > 0)
        {
            int selected = CountSelected(_toggled);
            if (selected < _info.Min)
                return true; // block submit, below minimum
        }

        var result = IsMulti
            ? BuildSelectedArray(_variants, _toggled)
            : [(IVariant)_variants[_highlightIndex]];

        _onSubmit(result);
        return true;
    }

    /// <summary>Counts selected toggles without LINQ allocation.</summary>
    private static int CountSelected(bool[] toggled)
    {
        int count = 0;
        for (int i = 0; i < toggled.Length; i++)
        {
            if (toggled[i]) count++;
        }
        return count;
    }

    /// <summary>Builds a result array from toggled entries without LINQ allocation. Only IVariant entries are included.</summary>
    private static IVariant[] BuildSelectedArray(IVariantEntry[] entries, bool[] toggled)
    {
        int count = CountSelected(toggled);
        var result = new IVariant[count];
        int idx = 0;
        for (int i = 0; i < toggled.Length; i++)
        {
            if (toggled[i])
                result[idx++] = (IVariant)entries[i];
        }
        return result;
    }

    /// <summary>Disposes the panel. Clears cached lines to release references.</summary>
    public void Dispose()
    {
        _cachedLines.Clear();
    }
}
