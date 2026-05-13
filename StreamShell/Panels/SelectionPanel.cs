namespace StreamShell;

/// <summary>
/// Bottom panel that renders a selectable list of variants.
/// Used by <see cref="ConsoleAppHost.PromptSelection"/>.
/// </summary>
internal class SelectionPanel : IBottomPanel
{
    private readonly string _title;
    private readonly IVariant[] _variants;
    private readonly SelectionInfo? _info;
    private readonly Action<IVariant[]> _onSubmit;
    private readonly Action _onCancel;

    private readonly bool[] _toggled;
    private readonly bool _preventCancel;
    private int _toggledVersion;
    private int _highlightIndex;
    private int _lastRenderHighlight;
    private int _lastToggledVersion;
    private readonly List<string> _cachedLines = new();

    private volatile bool _isDirty;
    bool IBottomPanel.IsDirty => _isDirty;
    void IBottomPanel.ClearDirty() => _isDirty = false;

    /// <summary>Total lines: controls + title + variants.</summary>
    public int LineCount => 2 + _variants.Length;

    /// <summary>Hides the input field while selection is active.</summary>
    bool IBottomPanel.AllowUserField => false;

    /// <summary>True in multi-select mode.</summary>
    private bool IsMulti => _info is not null;

    /// <summary>
    /// Creates a selection panel.
    /// </summary>
    public SelectionPanel(string title, IVariant[] variants, SelectionInfo? info,
        Action<IVariant[]> onSubmit, Action onCancel)
    {
        _title = title;
        _variants = variants;
        _info = info;
        _onSubmit = onSubmit;
        _onCancel = onCancel;
        _toggled = new bool[variants.Length];
        _preventCancel = info?.PreventCancel ?? false;
        _isDirty = true;
    }

    // ── Interface: GetLines ──────────────────────────────────────────

    /// <summary>
    /// Returns panel lines. <paramref name="currentInput"/> is ignored.
    /// </summary>
    public IReadOnlyList<string> GetLines(string currentInput)
    {
        if (_highlightIndex == _lastRenderHighlight
            && _toggledVersion == _lastToggledVersion
            && _cachedLines.Count > 0)
            return _cachedLines;

        // Reuse the cached lines list: clear and repopulate in place
        _cachedLines.Clear();

        // Line 0: control scheme (varies by mode)
        string controls = IsMulti
            ? _preventCancel
                ? "[dim]\u2191\u2193: navigate  Enter: toggle  Space: submit[/]"
                : "[dim]\u2191\u2193: navigate  Enter: toggle  Space: submit  Esc: cancel[/]"
            : _preventCancel
                ? "[dim]\u2191\u2193: navigate  Enter/Space: submit[/]"
                : "[dim]\u2191\u2193: navigate  Enter/Space: submit  Esc: cancel[/]";
        _cachedLines.Add(controls);

        // Line 1: title
        _cachedLines.Add($"[bold]{_title}[/]");

        // Variant lines
        for (int i = 0; i < _variants.Length; i++)
        {
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

        _lastRenderHighlight = _highlightIndex;
        _lastToggledVersion = _toggledVersion;
        return _cachedLines;
    }

    // ── Interface: TryHandleKey ──────────────────────────────────────

    bool IBottomPanel.TryHandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_highlightIndex > 0)
                {
                    _highlightIndex--;
                    _isDirty = true;
                }
                return true;

            case ConsoleKey.DownArrow:
                if (_highlightIndex < _variants.Length - 1)
                {
                    _highlightIndex++;
                    _isDirty = true;
                }
                return true;

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

    // ── Key handlers ─────────────────────────────────────────────────

    private bool HandleEnter()
    {
        if (IsMulti)
        {
            // Toggle the highlighted variant
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
        _onSubmit([_variants[_highlightIndex]]);
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
            : [_variants[_highlightIndex]];

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

    /// <summary>Builds a result array from toggled variants without LINQ allocation.</summary>
    private static IVariant[] BuildSelectedArray(IVariant[] variants, bool[] toggled)
    {
        int count = CountSelected(toggled);
        var result = new IVariant[count];
        int idx = 0;
        for (int i = 0; i < toggled.Length; i++)
        {
            if (toggled[i])
                result[idx++] = variants[i];
        }
        return result;
    }

    /// <summary>Disposes the panel. Clears cached lines to release references.</summary>
    public void Dispose()
    {
        _cachedLines.Clear();
    }
}
