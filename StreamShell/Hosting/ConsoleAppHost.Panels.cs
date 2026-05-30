namespace StreamShell;

// ReSharper disable once PartialTypeWithSinglePart
public partial class ConsoleAppHost
{
    /// <summary>
    /// Swaps the bottom panel. Cancels the previous panel's background task (if any) and
    /// starts the new one. Raises BottomPanelChanged so the renderer adjusts.
    /// </summary>
    public void SetBottomPanel(IBottomPanel panel)
    {
        // Dispose previous panel before swapping
        if (_bottomPanel != _defaultPanel)
            _bottomPanel.Dispose();

        // Cancel previous panel's background task
        _panelCts.Cancel();
        _panelCts.Dispose();
        _panelCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        _bottomPanel = panel;
        if (_renderer is ConsoleRenderer cr)
            cr.ShowBottomSeparator = panel.ShowBottomSeparator;
        WireUpAutoComplete();
        BottomPanelChanged?.Invoke(this, new BottomPanelChangedEventArgs(panel));

        // Start new panel's background loop (fire-and-forget; the linked token
        // ensures it is cancelled when swapped or when the host stops).
        _ = panel.RunAsync(_panelCts.Token);
    }

    /// <summary>Restores the default bottom panel (EmptyBottomPanel by default).</summary>
    public void ResetBottomPanel()
    {
        SetBottomPanel(_defaultPanel);
    }

    /// <summary>Replaces the default bottom panel with a custom one. Used when no command is active.</summary>
    public void SetDefaultPanel(IBottomPanel panel)
    {
        _defaultPanel?.Dispose();
        _defaultPanel = panel;
        // If we're currently on the old default, swap to the new one
        if (_bottomPanel is not CommandPalette && _bottomPanel is not SelectionPanel)
            ResetBottomPanel();
    }

    private void WireUpAutoComplete()
    {
        if (_inputHandler is UserInputHandler uih)
        {
            uih.AutoCompleteProvider = input =>
            {
                _bottomPanel.GetLines(input);
                return _bottomPanel.CurrentSuggestion;
            };

            // Let the active panel intercept keys (e.g. Up/Down for hint selection)
            uih.KeyInterceptor = key => _bottomPanel.TryHandleKey(key);
        }
    }

    /// <summary>
    /// Swaps between the default panel and CommandPalette based on whether the
    /// current input starts with "/". Skips if already on the correct panel.
    /// Does nothing when a non-standard panel (e.g. SelectionPanel) is active.
    /// </summary>
    private void EnsureProperPanel()
    {
        // SelectionPanel is modal — never swap it out for command palette or default.
        // It must remain active until its workflow completes (submit/cancel).
        if (_bottomPanel is SelectionPanel)
            return;

        string input = _inputHandler.CurrentInput;
        bool isCommand = input.Length > 0 && input[0] == '/';

        if (isCommand && _bottomPanel is CommandPalette)
            return;
        if (!isCommand && !(_bottomPanel is CommandPalette))
            return;

        if (isCommand)
            SetBottomPanel(new CommandPalette(() => _commandManager.AllCommands, Settings, _terminal));
        else
            SetBottomPanel(_defaultPanel);
    }
}
