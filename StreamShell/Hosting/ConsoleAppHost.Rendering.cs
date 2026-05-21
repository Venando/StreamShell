using System.Text;

namespace StreamShell;

// ReSharper disable once PartialTypeWithSinglePart
public partial class ConsoleAppHost
{
    // ── Tracks render state between loop iterations ───────────────────
    internal sealed record RenderSnapshot(
        string? LastInput,
        int LastCursor,
        bool LastHasSelection,
        int LastInputLineCount,
        int LastWindowWidth,
        int LastPanelLineCount,
        int LastBufferHeight
    );

    // ── Resize detection ─────────────────────────────────────────────
    private int _resizeStableTicks = 0;
    private bool _resizeDetected = false;

    // ── Rendering shenanigans ─────────────────────────────────────────────
    private int _lastInputBlockHeight = -1;
    private int _emptyBlocksNumberAfterClearing = 0;
    private DateTime _lastDateTime;

    /// <summary>
    /// Console width at the last replay emission.
    /// Messages visible at this width are already correctly wrapped.
    /// A new replay only fires when the settled width is narrower than this value.
    /// Initialized to the starting terminal width in constructors.
    /// </summary>
    private int _lastReplayWidth;

    /// <summary>For testing: tracks how many times replay was triggered.</summary>
    internal int ReplayTriggerCount { get; private set; }

    private async Task RunLoop(CancellationToken token)
    {
        var state = new RenderSnapshot(null, 0, false, 0, _terminal.WindowWidth, _bottomPanel.LineCount, _terminal.BufferHeight);

        while (!token.IsCancellationRequested)
        {
            (string? submittedInput, state) = ProcessOneTick(state);
            if (submittedInput == "__QUIT__")
                break;

            try
            {
                await Task.Delay(10, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Holds render-time state captured from the input handler and terminal.
    /// Extracted into a struct so <see cref="ProcessOneTick"/> can pass it
    /// to rendering methods without unpacking individual fields (SRP + readability).
    /// </summary>
    private readonly record struct TickState(
        string Input,
        int Cursor,
        bool HasSelection,
        int SelectionStart,
        int SelectionLength,
        int WindowWidth,
        int Margin
    );

    /// <summary>Processes exactly one tick of the render/input loop.
    /// Returns (submittedInput, newState) — submittedInput is null if nothing was submitted,
    /// or "__QUIT__" when Ctrl+D was pressed.
    /// For testing: allows controlled single-iteration execution without the infinite loop.
    /// </summary>
    // Tracks attachment count to avoid recomputing PlaceholderStrings on every tick
    private int _lastAttachmentCount = -1;
    private List<string>? _cachedPlaceholders;

    internal (string? SubmittedInput, RenderSnapshot NewState) ProcessOneTick(RenderSnapshot state)
    {
        EnsureProperPanel();

        var tick = CaptureTickState();
        SyncPlaceholderCache();

        // Let panel compute lines (may update dynamic LineCount), then sync renderer
        _bottomPanel.GetLines(tick.Input);
        _renderer.SetPanelLineCount(_bottomPanel.LineCount);

        // ── Buffer height change: exposed lines become empty blocks ──────
        int currentBufferHeight = _terminal.BufferHeight;
        int bufferHeightDelta = currentBufferHeight - state.LastBufferHeight;

        if (bufferHeightDelta > 0 && state.LastBufferHeight > 0 && state.LastInput is not null)
        {
            // Buffer grew: the input block moved down, exposing lines in the message area.
            // Add exposed lines to empty-block counter so messages fill them from top.
            _emptyBlocksNumberAfterClearing += bufferHeightDelta;

            // Clear old block content so separator / hints don't leak into messages.
            int oldBlockOffset = (1 + state.LastPanelLineCount)
                + _renderer.GetInputLineCount(state.LastInput);
            int oldBlockTop = state.LastBufferHeight - oldBlockOffset - 1;

            for (int i = 0; i < oldBlockOffset + 1; i++)
            {
                int line = oldBlockTop + i;
                if (line >= 0 && line < currentBufferHeight)
                {
                    _terminal.SetCursorPosition(0, line);
                    _terminal.Write("\x1b[K");
                }
            }
        }
        else if (bufferHeightDelta < 0)
        {
            // Buffer shrank: cap empty blocks so they don't exceed available space.
            _emptyBlocksNumberAfterClearing = Math.Max(0,
                _emptyBlocksNumberAfterClearing + bufferHeightDelta);
        }

        bool widthDecreased = tick.WindowWidth < state.LastWindowWidth;
        bool widthChanged = tick.WindowWidth != state.LastWindowWidth;

        if (widthDecreased)
        {
            _resizeDetected = true;
            _resizeStableTicks = 0;
        }
        else if (_resizeDetected && !widthChanged)
        {
            _resizeStableTicks++;
            if (_resizeStableTicks >= 5) // ~50ms of stability
            {
                _resizeDetected = false;
                _resizeStableTicks = 0;

                // Only replay if the settled width is narrower than the last replay width.
                // If the user increased the console (e.g. +50) then decreased (-20),
                // the settled width may still be wider than when messages were last
                // emitted — in that case no replay is needed.
                bool shouldReplay = tick.WindowWidth < _lastReplayWidth;
                if (shouldReplay)
                {
                    _lastReplayWidth = tick.WindowWidth;
                    ReplayTriggerCount++;

                    // Move messages from history back into the queue so they render
                    // through the normal pipeline (scroll region, empty-block tracking,
                    // proper cursor positioning). This prevents the top separator from
                    // leaking into the message stream when messages replay directly via
                    // AnsiConsole.MarkupLine without a scroll region.
                    int replayCount = Settings.MessageReplayCount < 0
                        ? Console.WindowHeight + 1
                        : Settings.MessageReplayCount;
                    if (_renderer is ConsoleRenderer cr)
                    {
                        cr.RetrieveMessagesFromHistory(replayCount, (Span<string> messages) =>
                        {
                            for (int i = messages.Length - 1; i >= 0; i--)
                                _messages.EnqueueAsFirst(messages[i]);
                        });
                    }
                }
                // Fall through to TryRender — RenderQueuedMessages will handle both
                // replayed messages (now in _messages) and any new queued messages,
                // with proper scroll-region setup and empty-block tracking.
            }
        }
        else if (_resizeDetected && widthChanged && !widthDecreased)
        {
            // Width increased during resize — reset
            _resizeDetected = false;
            _resizeStableTicks = 0;
        }

        bool rendered = TryRender(state, tick);
        var newState = rendered
            ? new RenderSnapshot(tick.Input, tick.Cursor, tick.HasSelection,
                _renderer.GetInputLineCount(tick.Input), tick.WindowWidth, _bottomPanel.LineCount, _terminal.BufferHeight)
            : state;

        if (_inputHandler.QuitRequested)
        {
            _inputHandler.QuitRequested = false;
            return ("__QUIT__", newState);
        }

        string? submitted = _inputHandler.ProcessInput();
        if (submitted != null)
        {
            HandleSubmittedInput(submitted, tick.WindowWidth);
            newState = new RenderSnapshot(null, 0, false, 0, tick.WindowWidth, _bottomPanel.LineCount, _terminal.BufferHeight);
        }

        return (submitted, newState);
    }

    /// <summary>Captures the current input handler state and terminal dimensions into a single struct.</summary>
    private TickState CaptureTickState()
    {
        // Always sync right margins with the current terminal width — the
        // user may have resized the window since the last tick. Without this,
        // ConsoleRenderer and UserInputHandler would use stale constructor-time
        // values, causing text to wrap at the wrong column ("right margin stays
        // at the same place" after a resize).
        int windowWidth = _terminal.WindowWidth;
        _inputHandler.RightMargin = windowWidth;
        _inputHandler.PrefixMargin = Settings.PrefixMargin;
        _inputHandler.WrappingRightMargin = Settings.WrappingRightMargin;
        if (_renderer is ConsoleRenderer cr)
            cr.RightMargin = windowWidth;

        _inputHandler.TryGetSelection(out int selStart, out int selLength);
        return new TickState(
            _inputHandler.CurrentInput,
            _inputHandler.CursorPosition,
            _inputHandler.HasSelection,
            selStart,
            selLength,
            windowWidth,
            _inputHandler.RightMargin
        );
    }

    /// <summary>
    /// Synchronizes the placeholder cache with the current attachment list.
    /// Avoids recomputing PlaceholderStrings on every tick when attachments
    /// haven't changed.
    /// </summary>
    private void SyncPlaceholderCache()
    {
        if (_renderer is not ConsoleRenderer cr)
            return;

        int attachCount = _inputHandler.Attachments.Count;
        if (attachCount != _lastAttachmentCount)
        {
            _lastAttachmentCount = attachCount;
            _cachedPlaceholders = null;
        }

        if (_cachedPlaceholders is null)
        {
            var placeholders = new List<string>(attachCount);
            foreach (var a in _inputHandler.Attachments)
            {
                if (!string.IsNullOrEmpty(a.Placeholder))
                    placeholders.Add(a.Placeholder);
            }
            _cachedPlaceholders = placeholders;
        }

        cr.PlaceholderStrings = _cachedPlaceholders;
    }

    /// <summary>Priority render check: messages first, then input changes. Returns true when the screen was updated.</summary>
    private bool TryRender(RenderSnapshot state, TickState tick)
    {
        // Priority 1: queued messages need a full re-render
        if (RenderQueuedMessages(state, tick))
            return true;

        // Priority 2: input/cursor/resize changes need an update
        return RenderInputChanges(state, tick);
    }

    /// <summary>
    /// Renders up to <see cref="StreamShellSettings.RenderChunkSize"/> queued messages
    /// in one go, then re-renders the input block. Batching reduces flicker and improves
    /// throughput for bulk output. Returns true when at least one message was rendered
    /// or the input block height changed.
    /// </summary>
    private bool RenderQueuedMessages(RenderSnapshot state, TickState tick)
    {
        int chunkSize = GetRenderChunkSize();

        int inputBlockHeight = _renderer.GetBlockOffset(tick.Input);
        int blockHeightDelta = _lastInputBlockHeight >= 0
            ? inputBlockHeight - _lastInputBlockHeight
            : 0;
        _lastInputBlockHeight = inputBlockHeight;

        // Nothing to do if no messages and no block height change
        if (_messages.Count == 0 && blockHeightDelta == 0)
            return false;

        bool anyRendered = false;
        int messageCount = 0;
        bool scrollRegionSet = false;
        int renderedMessageCount = 0;

        // Handle block growth: some lines get covered by the input block.
        // First consume any empty lines that were waiting to be filled;
        // only retrieve from history if growth exceeds the empty gap.
        if (blockHeightDelta > 0)
        {
            int growth = blockHeightDelta;

            int emptyConsumed = Math.Min(_emptyBlocksNumberAfterClearing, growth);
            _emptyBlocksNumberAfterClearing -= emptyConsumed;
            growth -= emptyConsumed;

            if (growth > 0 && _renderer is ConsoleRenderer cr)
            {
                cr.RetrieveMessagesFromHistory(growth, (Span<string> messages) =>
                {
                    for (int i = messages.Length - 1; i >= 0; i--)
                        _messages.EnqueueAsFirst(messages[i]);
                });
            }
        }

        void SetupMessageRenderArea()
        {
            if (_renderer is ConsoleRenderer cr)
            {
                cr.SetMessageScrollRegion(inputBlockHeight);
                scrollRegionSet = true;

                // Position cursor one line above the scroll bottom so that after
                // the message's leading WriteLine, the message text lands at the
                // bottom of the scroll region (just above the input block).
                int scrollBottom = _terminal.BufferHeight - inputBlockHeight - 1;
                int pendingShrinkLines = blockHeightDelta < 0 ? -blockHeightDelta : 0;
                int effectiveEmptyBlocks = _emptyBlocksNumberAfterClearing + pendingShrinkLines;
                int cursorTop = scrollBottom - 1 - effectiveEmptyBlocks;

                _terminal.CursorTop = Math.Max(0, cursorTop);
                _terminal.CursorLeft = 0;
            }
            else
            {
                if (state.LastInput is not null)
                    _renderer.ClearInputBlockForReRender(state.LastInput, tick.Input, state.LastPanelLineCount);
                else
                    _renderer.ClearInputLine();
            }
        }

        while (messageCount < chunkSize && _messages.TryDequeue(out var message))
        {
            if (!anyRendered)
                SetupMessageRenderArea();

            _renderer.RenderMessage(message);
            anyRendered = true;
            messageCount++;
            renderedMessageCount++;
        }

        // If the block changed but no messages were rendered, we still need to
        // set up the scroll region so exposed-line cleanup and input re-render work.
        if (!anyRendered && blockHeightDelta != 0 && _renderer is ConsoleRenderer crFallback)
        {
            crFallback.SetMessageScrollRegion(inputBlockHeight);
            scrollRegionSet = true;
        }

        // Reset scroll region before rendering the input block.
        if (scrollRegionSet && _renderer is ConsoleRenderer cr2)
            cr2.ResetScrollRegion();

        // When the block shrinks, the lines that used to be part of the input
        // block are now exposed in the message area. Clear them so stale
        // separator / hint content doesn't remain visible.
        if (blockHeightDelta < 0)
        {
            int linesExposed = -blockHeightDelta;
            // GetBlockOffset omits the blank WriteLine between input and hints,
            // so subtract 1 to reach the actual input block top.
            int blockTopForClearing = _terminal.BufferHeight - inputBlockHeight - 1;

            for (int i = 1; i <= linesExposed; i++)
            {
                int line = blockTopForClearing - i;
                if (line >= 0 && line < _terminal.BufferHeight)
                {
                    _terminal.SetCursorPosition(0, line);
                    _terminal.Write("\x1b[K");
                }
            }

            _emptyBlocksNumberAfterClearing += linesExposed;
        }

        // Position cursor at the top of the input block and render it.
        int inputBlockTop = _terminal.BufferHeight - inputBlockHeight - 1;
        _terminal.CursorTop = Math.Max(0, Math.Min(inputBlockTop, _terminal.BufferHeight - 1));
        _terminal.CursorLeft = 0;
        RenderFullInputBlock(tick);

        // Each rendered message roughly fills one empty block line. This is
        // approximate since wrapped messages may consume more, but the scroll
        // region naturally accommodates the difference on subsequent ticks.
        _emptyBlocksNumberAfterClearing = Math.Max(0, _emptyBlocksNumberAfterClearing - renderedMessageCount);

        if (_bottomPanel.IsDirty)
            _bottomPanel.ClearDirty();

        return true;
    }

    /// <summary>Computes how many messages to render this tick based on settings.</summary>
    private int GetRenderChunkSize()
    {
        if (Settings.PrintingMode == MessagePrintingMode.IntChunks)
            return Settings.RenderChunkSize;

        var currentDateTime = DateTime.UtcNow;
        double elapsed = (currentDateTime - _lastDateTime).TotalSeconds;
        double fractionRendered = 1.0 - Math.Exp(-Settings.ExpDecayRate * elapsed);
        _lastDateTime = currentDateTime;
        return Math.Max(1, (int)(_messages.Count * fractionRendered));
    }

    /// <summary>Applies input/cursor/resize changes. Returns true when the screen was updated.</summary>
    private bool RenderInputChanges(RenderSnapshot state, TickState tick)
    {
        bool panelDirty = _bottomPanel.IsDirty;
        bool sepDirty = _separatorDirty;
        if (!StateDiffersFromRender(state, tick) && !panelDirty && !sepDirty)
            return false;

        if (panelDirty)
            _bottomPanel.ClearDirty();
        _separatorDirty = false;

        // Always use the flicker-free overwrite approach:
        // position at old block top and render over existing content.
        // Each visual line now has \x1b[K emitted at end, so stale trailing
        // characters are cleared without a separate clear-before-render step.
        OverwriteInputBlockInPlace(state, tick);
        return true;
    }

    /// <summary>
    /// Overwrites the input block in place without clearing first.
    /// Positions the cursor at the top of the old rendered block, then
    /// renders the full block (separator + input + hints) over existing
    /// content. Handles height changes after rendering to clear excess
    /// lines when the block shrinks.
    /// </summary>
    private void OverwriteInputBlockInPlace(RenderSnapshot state, TickState tick)
    {
        int oldBlockOffset = state.LastInput is not null
            ? (1 + state.LastPanelLineCount) + _renderer.GetInputLineCount(state.LastInput)
            : 0;
        int newBlockOffset = _renderer.GetBlockOffset(tick.Input);

        _renderer.OverwriteFullBlock(tick.Input, GetCommandHints(tick.Input), oldBlockOffset,
            tick.Cursor, tick.HasSelection, tick.SelectionStart, tick.SelectionLength, tick.Margin);

        if (state.LastInput is not null)
            _renderer.HandleBlockHeightChange(oldBlockOffset, newBlockOffset);
    }

    /// <summary>Returns true when any tracked state has changed from the last render.</summary>
    private bool StateDiffersFromRender(RenderSnapshot state, TickState tick)
    {
        return state.LastInput != tick.Input
            || state.LastCursor != tick.Cursor
            || state.LastHasSelection != tick.HasSelection
            || state.LastWindowWidth != tick.WindowWidth
            || state.LastPanelLineCount != _bottomPanel.LineCount
            || state.LastBufferHeight != _terminal.BufferHeight;
    }

    private void RenderFullInputBlock(TickState tick)
    {
        _renderer.RenderInputBlock(tick.Input, GetCommandHints(tick.Input),
            tick.Cursor, tick.HasSelection, tick.SelectionStart, tick.SelectionLength, tick.Margin);
    }

    private IReadOnlyList<string> GetCommandHints(string input) => _bottomPanel.GetLines(input);
}
