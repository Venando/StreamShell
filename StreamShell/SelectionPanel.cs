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

    private readonly bool[] _toggled;
    private int _highlightIndex;
    private int _lastRenderHighlight;
    private IReadOnlyList<string>? _cachedLines;

    private volatile bool _isDirty;
    bool IBottomPanel.IsDirty => _isDirty;
    void IBottomPanel.ClearDirty() => _isDirty = false;

    /// <summary>
    /// Number of navigable items: variants only (single) or variants + submit button (multi).
    /// </summary>
    private int ItemCount => _info is not null ? _variants.Length + 1 : _variants.Length;

    /// <summary>Total lines: title + navigable items.</summary>
    public int LineCount => 1 + ItemCount;

    /// <summary>True in multi-select mode.</summary>
    private bool IsMulti => _info is not null;

    /// <summary>
    /// Creates a selection panel.
    /// </summary>
    public SelectionPanel(string title, IVariant[] variants, SelectionInfo? info,
        Action<IVariant[]> onSubmit)
    {
        _title = title;
        _variants = variants;
        _info = info;
        _onSubmit = onSubmit;
        _toggled = new bool[variants.Length];
    }

    // ── Interface: GetLines ──────────────────────────────────────────

    /// <summary>
    /// Returns panel lines. <paramref name="currentInput"/> is ignored.
    /// </summary>
    public IReadOnlyList<string> GetLines(string currentInput)
    {
        if (_highlightIndex == _lastRenderHighlight && _cachedLines is not null)
            return _cachedLines;

        var lines = new List<string>(LineCount);

        // Line 0: title
        lines.Add($"[bold]{_title}[/]");

        // Variant lines
        for (int i = 0; i < _variants.Length; i++)
        {
            bool highlighted = i == _highlightIndex;
            bool selected = IsMulti && _toggled[i];

            string indicator = selected ? "\u25a3 " : "\u2610 ";
            string selectedIndicator = "\u25a3 ";  // filled checkbox
            string unselectedIndicator = "\u2610 "; // empty checkbox

            string prefix;
            string color;

            if (IsMulti)
            {
                prefix = selected ? selectedIndicator : unselectedIndicator;
                color = highlighted ? "white" : "grey";
            }
            else
            {
                prefix = highlighted ? "> " : "  ";
                color = highlighted ? "white" : "grey";
            }

            lines.Add($"{prefix}[{color}]{_variants[i].Name}[/]");
        }

        // Submit button (multi-select only)
        if (_info is not null)
        {
            bool onSubmit = _highlightIndex == _variants.Length;
            string style = onSubmit ? "bold white" : "bold grey";
            lines.Add($"> [{style}]{_info.SubmitTitle}[/]");
        }

        _lastRenderHighlight = _highlightIndex;
        _cachedLines = lines;
        return lines;
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
                if (_highlightIndex < ItemCount - 1)
                {
                    _highlightIndex++;
                    _isDirty = true;
                }
                return true;

            case ConsoleKey.Enter:
                return HandleEnter();

            case ConsoleKey.Spacebar:
                return HandleSubmit();
        }

        return false;
    }

    // ── Key handlers ─────────────────────────────────────────────────

    private bool HandleEnter()
    {
        if (IsMulti)
        {
            // On the submit button?
            if (_highlightIndex == _variants.Length)
                return HandleSubmit();

            // Toggle the highlighted variant
            int idx = _highlightIndex;

            // Check max limit unless unlimited
            if (!_toggled[idx] && _info!.Max > 0)
            {
                int currentSelected = _toggled.Count(t => t);
                if (currentSelected >= _info.Max)
                    return true; // block toggle, already at max
            }

            _toggled[idx] = !_toggled[idx];
            _isDirty = true;

            // If max is 1, selecting also submits immediately
            if (_toggled[idx] && _info!.Max == 1)
                return HandleSubmit();

            return true;
        }

        // Single — select highlighted and submit
        SelectAndSubmit(_variants[_highlightIndex]);
        return true;
    }

    private bool HandleSubmit()
    {
        // Check minimum constraint
        if (IsMulti && _info!.Min > 0)
        {
            int selected = _toggled.Count(t => t);
            if (selected < _info.Min)
                return true; // block submit, below minimum
        }

        var result = IsMulti
            ? _variants.Where((v, i) => _toggled[i]).ToArray()
            : [_variants[_highlightIndex]];

        _onSubmit(result);
        return true;
    }

    private void SelectAndSubmit(IVariant variant)
    {
        _onSubmit([variant]);
    }
}
