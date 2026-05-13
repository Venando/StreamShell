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
            onCancel: () => { });

        var lines = panel.GetLines("/");

        Assert.Equal(5, lines.Count); // 1 control + 1 title + 3 variants
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
            onCancel: () => { });

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
            onCancel: () => { });

        panel.GetLines("/");  // render initial state
        Assert.False(((IBottomPanel)panel).IsDirty);  // not dirty after render

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => cancelled = true);

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.True(cancelled);
    }

    // ── Cache Hit Tests ─────────────────────────────────────────────

    [Fact]
    public void GetLines_SameState_ReturnsCachedListReference()
    {
        var panel = new SelectionPanel("Pick", SampleVariants, null,
            onSubmit: _ => { },
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => { });

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
            onCancel: () => cancelled = true);

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
            onCancel: () => cancelled = true);

        ((IBottomPanel)panel).TryHandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.False(cancelled);
    }

    [Fact]
    public void MultiSelect_PreventCancel_HintsOmitEscape()
    {
        var info = new SelectionInfo { Min = 1, Max = 3, PreventCancel = true };
        var panel = new SelectionPanel("Pick", SampleVariants, info,
            onSubmit: _ => { },
            onCancel: () => { });

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
            onCancel: () => { });

        var lines = panel.GetLines("/");

        Assert.DoesNotContain("Esc: cancel", lines[0]);
        Assert.Contains("submit", lines[0]);
    }
}
