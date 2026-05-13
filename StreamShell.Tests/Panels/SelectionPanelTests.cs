using System.Linq;
using StreamShell;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  SelectionPanel Tests — variant selection, navigation, constraints
// ═════════════════════════════════════════════════════════════════════

public class SelectionPanelTests
{
    private static readonly IVariantEntry[] SampleVariants =
    {
        new TestVariant("Option A"),
        new TestVariant("Option B"),
        new TestVariant("Option C"),
    };

    private sealed record TestVariant(string Name) : IVariant;

    // ── Single Select Mode (SelectionInfo = null) ────────────────────

    [Fact]
    public void SingleSelect_GetLines_ReturnsControlAndTitleAndVariants()
    {
        IVariant[]? result = null;
        var panel = new SelectionPanel("Choose one", SampleVariants, null,
            onSubmit: r => result = r,
            onCancel: () => { }, consoleHeight: 24);

        var lines = panel.GetLines("/");

        Assert.Equal(8, lines.Count); // 1 control + 1 title + 6 variant slots (Rows=8)
        Assert.Contains("\u2191\u2193", lines[0]);  // controls info
        Assert.Contains("Choose one", lines[1]);    // title
        Assert.Contains("Option A", lines[2]);
        Assert.Contains("Option B", lines[3]);
        Assert.Contains("Option C", lines[4]);
    }

    [Fact]
    public void SingleSelect_EnterOnFirst_SubmitsFirstVariant()
    {
        IVariant[]? submitted = null;
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: r => submitted = r,
            onCancel: () => { }, consoleHeight: 24);

        // Enter on first variant (highlighted by default)
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));

        Assert.NotNull(submitted);
        Assert.Single(submitted);
        Assert.Equal("Option A", submitted![0].Name);
    }

    [Fact]
    public void SingleSelect_NavigateDown_ChangesHighlight()
    {
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        panel.GetLines("/");  // render initial state
        ((IBottomPanel)panel).ClearDirty();  // simulate host render cycle
        Assert.False(((IBottomPanel)panel).IsDirty);  // not dirty after clear

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.True(((IBottomPanel)panel).IsDirty);  // dirty after navigation

        var lines = panel.GetLines("/");
        Assert.Contains(">", lines[3]);  // Option B highlighted
    }

    [Fact]
    public void SingleSelect_NavigateUp_AtTop_Stays()
    {
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        panel.GetLines("/");
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        // Should still have first variant highlighted
        var lines = panel.GetLines("/");
        Assert.Contains(">", lines[2]);  // Option A
    }

    [Fact]
    public void SingleSelect_Space_SubmitsHighlighted()
    {
        IVariant[]? submitted = null;
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: r => submitted = r,
            onCancel: () => { }, consoleHeight: 24);

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Spacebar, false, false, false));

        Assert.NotNull(submitted);
        Assert.Equal("Option A", submitted![0].Name);
    }

    [Fact]
    public void SingleSelect_Escape_Cancels()
    {
        bool cancelled = false;
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: _ => { },
            onCancel: () => cancelled = true, consoleHeight: 24);

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.True(cancelled);
    }

    // ── Cache Hit Tests ─────────────────────────────────────────────

    [Fact]
    public void GetLines_SameState_ReturnsCachedListReference()
    {
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        var lines1 = panel.GetLines("/");
        var lines2 = panel.GetLines("/");

        // Same state → should return the same cached list instance
        Assert.Same(lines1, lines2);
    }

    [Fact]
    public void GetLines_AfterNavigation_ContentChanges()
    {
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        var lines1 = panel.GetLines("/");
        string before = lines1[2];

        // Navigate down
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        var lines2 = panel.GetLines("/");
        string after = lines2[2];

        // Content should have changed (Option B now highlighted)
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void GetLines_AfterToggle_ContentChanges()
    {
        var info = new SelectionInfo { Min = 0, Max = 3 };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        var lines1 = panel.GetLines("/");
        string before = lines1[2];

        // Toggle first variant
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));

        var lines2 = panel.GetLines("/");
        string after = lines2[2];

        // Content should have changed (checked symbol)
        Assert.NotEqual(before, after);
    }

    // ── Multi Select Mode (SelectionInfo provided) ───────────────────

    [Fact]
    public void MultiSelect_GetLines_ShowsCheckboxes()
    {
        var info = new SelectionInfo { Min = 1, Max = 2 };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        var lines = panel.GetLines("/");

        Assert.Contains("Space: submit", lines[0]);  // multi-select controls
        Assert.Contains("Pick", lines[1]);    // multi-select title
        // Should have checkbox symbols (☐) — ignore markup tags
        Assert.Contains("\u2610", lines[2].Replace("[/]", "").Replace("]", "")); // unchecked
    }

    [Fact]
    public void MultiSelect_EnterTogglesVariant()
    {
        var info = new SelectionInfo { Min = 1, Max = 2 };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        panel.GetLines("/");  // render initial state
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));

        var lines = panel.GetLines("/");
        // Option A should now show checked symbol
        Assert.Contains("\u25a3", lines[2]); // checked
    }

    [Fact]
    public void MultiSelect_EnterTwice_TogglesOff()
    {
        var info = new SelectionInfo { Min = 0, Max = 2 };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        panel.GetLines("/");
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));

        var lines = panel.GetLines("/");
        Assert.Contains("\u2610", lines[2]); // back to unchecked
    }

    [Fact]
    public void MultiSelect_MaxLimit_BlocksAdditionalToggle()
    {
        var info = new SelectionInfo { Min = 0, Max = 2 };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        // Toggle Option A and B
        panel.GetLines("/");
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)); // A

        // Navigate down
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)); // B

        // Navigate to C, try toggle — blocked at max 2
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));

        // Option C should not be toggled (max = 2)
        var lines = panel.GetLines("/");
        Assert.Contains("\u2610", lines[4]); // C still unchecked
    }

    [Fact]
    public void MultiSelect_Space_SubmitsToggledVariants()
    {
        var info = new SelectionInfo { Min = 0, Max = 3 };
        IVariant[]? submitted = null;
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: r => submitted = r,
            onCancel: () => { }, consoleHeight: 24);

        // Toggle A and C
        panel.GetLines("/");
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)); // A
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)); // C
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Spacebar, false, false, false));

        Assert.NotNull(submitted);
        Assert.Equal(2, submitted!.Length);
        Assert.Equal("Option A", submitted[0].Name);
        Assert.Equal("Option C", submitted[1].Name);
    }

    [Fact]
    public void MultiSelect_MinConstraint_BlocksSubmitWhenBelowMin()
    {
        var info = new SelectionInfo { Min = 2, Max = 3 };
        bool submitted = false;
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => submitted = true,
            onCancel: () => { }, consoleHeight: 24);

        // Toggle only one, then try to submit
        panel.GetLines("/");
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)); // A
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Spacebar, false, false, false));

        Assert.False(submitted); // should be blocked
    }

    [Fact]
    public void MultiSelect_MaxIsOne_TogglesAndSubmitsOnEnter()
    {
        var info = new SelectionInfo { Min = 1, Max = 1 };
        IVariant[]? submitted = null;
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: r => submitted = r,
            onCancel: () => { }, consoleHeight: 24);

        panel.GetLines("/");
        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false));

        Assert.NotNull(submitted);
        Assert.Single(submitted!);
        Assert.Equal("Option A", submitted![0].Name);
    }

    [Fact]
    public void MultiSelect_Escape_Cancels()
    {
        bool cancelled = false;
        var info = new SelectionInfo { Min = 1, Max = 3 };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => cancelled = true, consoleHeight: 24);

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.True(cancelled);
    }

    // ── PreventCancel Mode ─────────────────────────────────────────

    [Fact]
    public void MultiSelect_PreventCancel_Escape_DoesNotCancel()
    {
        bool cancelled = false;
        var info = new SelectionInfo { Min = 1, Max = 3, PreventCancel = true };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => cancelled = true, consoleHeight: 24);

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.False(cancelled);
    }

    [Fact]
    public void MultiSelect_PreventCancel_HintsOmitEscape()
    {
        var info = new SelectionInfo { Min = 1, Max = 3, PreventCancel = true };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        var lines = panel.GetLines("/");

        Assert.DoesNotContain("Esc: cancel", lines[0]);
        Assert.Contains("Space: submit", lines[0]);
    }

    [Fact]
    public void MultiSelect_PreventCancel_MaxOne_HintsOmitEscape()
    {
        var info = new SelectionInfo { Min = 1, Max = 1, PreventCancel = true };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 24);

        var lines = panel.GetLines("/");

        Assert.DoesNotContain("Esc: cancel", lines[0]);
        Assert.Contains("submit", lines[0]);
    }

    // ── Rows / Scrolling Tests ───────────────────────────────────────

    [Fact]
    public void SelectionInfo_Rows_DefaultIsEight()
    {
        var info = new SelectionInfo();
        Assert.Equal(8, info.Rows);
    }

    [Fact]
    public void SelectionInfo_GetEffectiveRows_ReturnsRows_WhenSpecified()
    {
        var info = new SelectionInfo { Rows = 5 };
        Assert.Equal(5, info.GetEffectiveRows(100, 50));
    }

    [Fact]
    public void SelectionInfo_GetEffectiveRows_AutoResolves_WhenRowsBelowTwo()
    {
        var info = new SelectionInfo { Rows = 0 };
        // 10 items, console height 30 → min(10, 25) = 10, floored to 3
        Assert.Equal(10, info.GetEffectiveRows(10, 30));
    }

    [Fact]
    public void SelectionInfo_GetEffectiveRows_AutoResolves_UsesConsoleMinusFive()
    {
        var info = new SelectionInfo { Rows = 0 };
        // 100 items, console height 20 → min(100, 15) = 15
        Assert.Equal(15, info.GetEffectiveRows(100, 20));
    }

    [Fact]
    public void SelectionInfo_GetEffectiveRows_AutoResolves_FloorOfThree()
    {
        var info = new SelectionInfo { Rows = 0 };
        // 1 item, console height 7 → min(1, 2) = 1, floored to 3
        Assert.Equal(3, info.GetEffectiveRows(1, 7));
    }

    [Fact]
    public void Scrolling_LineCount_IsConstant()
    {
        var variants = Enumerable.Range(1, 20)
            .Select(i => new TestVariant($"Item {i}"))
            .Cast<IVariantEntry>()
            .ToArray();

        var info = new SelectionInfo { Rows = 8 };
        var panel = new SelectionPanel("Many", variants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        // Rows=8 → variantCapacity=6, LineCount=2+6=8
        Assert.Equal(8, ((IBottomPanel)panel).LineCount);
    }

    [Fact]
    public void Scrolling_ShowsScrollInfo_WhenItemsExceedVisible()
    {
        var variants = Enumerable.Range(1, 20)
            .Select(i => new TestVariant($"Item {i}"))
            .Cast<IVariantEntry>()
            .ToArray();

        var info = new SelectionInfo { Rows = 5 };
        var panel = new SelectionPanel("Many", variants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        var lines = panel.GetLines("/");

        // Controls line should contain scroll indicator like "1-3/20"
        Assert.Contains("1-", lines[0]);
        Assert.Contains("/20", lines[0]);
    }

    [Fact]
    public void Scrolling_NoScrollInfo_WhenItemsFit()
    {
        var info = new SelectionInfo { Rows = 8 };
        var panel = new SelectionPanel("Few", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        var lines = panel.GetLines("/");

        // 3 variants, capacity=6 → no scrolling
        Assert.DoesNotContain("/3", lines[0]);
    }

    [Fact]
    public void Scrolling_DownArrow_ScrollsWhenPastBottom()
    {
        var variants = Enumerable.Range(1, 20)
            .Select(i => new TestVariant($"Item {i}"))
            .Cast<IVariantEntry>()
            .ToArray();

        var info = new SelectionInfo { Rows = 5 }; // capacity = 3
        var panel = new SelectionPanel("Many", variants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        // Navigate past the visible window (3 items) to trigger scroll
        var iface = (IBottomPanel)panel;
        for (int i = 0; i < 3; i++)
            iface.TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        var lines = panel.GetLines("/");

        // Scrolled: visible window starts at index 1, shows Items 2,3,4
        Assert.Contains("Item 2", lines[2]);
        Assert.Contains("Item 4", lines[4]);
    }

    [Fact]
    public void Scrolling_UpArrow_ScrollsWhenPastTop()
    {
        var variants = Enumerable.Range(1, 20)
            .Select(i => new TestVariant($"Item {i}"))
            .Cast<IVariantEntry>()
            .ToArray();

        var info = new SelectionInfo { Rows = 5 }; // capacity = 3
        var panel = new SelectionPanel("Many", variants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        var iface = (IBottomPanel)panel;
        // Scroll down to item 7
        for (int i = 0; i < 6; i++)
            iface.TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        // Now scroll back up
        iface.TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        var lines = panel.GetLines("/");

        // Should show Item 5 (scrolled back one)
        Assert.Contains("Item 5", lines[2]);
    }

    [Fact]
    public void Scrolling_PadsEmptyLines_ToLineCount()
    {
        var info = new SelectionInfo { Rows = 10 };
        var panel = new SelectionPanel("Pad", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        var lines = panel.GetLines("/");

        // Rows=10 → LineCount=10, 3 variants → padded with empty
        Assert.Equal(10, lines.Count);
        // Last lines should be empty (padding)
        for (int i = 5; i < lines.Count; i++)
            Assert.Equal(string.Empty, lines[i]);
    }

    [Fact]
    public void Scrolling_Decorations_AreRenderedInScrollWindow()
    {
        var decorated = new IVariantEntry[]
        {
            new TestDecoration("--- Group 1 ---"),
            new TestVariant("A"),
            new TestVariant("B"),
            new TestDecoration("--- Group 2 ---"),
            new TestVariant("C"),
            new TestVariant("D"),
        };

        var info = new SelectionInfo { Rows = 5 }; // capacity = 3
        var panel = new SelectionPanel("Deco", decorated, info,
            onSubmit: _ => { },
            onCancel: () => { }, consoleHeight: 50);

        var lines = panel.GetLines("/");

        // Only first 3 entries visible
        Assert.Contains("Group 1", lines[2]);
        Assert.Contains("A", lines[3]);
        Assert.Contains("B", lines[4]);
    }

    private sealed record TestDecoration(string Name) : IDecoration;
}
