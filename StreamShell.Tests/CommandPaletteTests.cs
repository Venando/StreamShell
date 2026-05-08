using Spectre.Console;

namespace StreamShell.Tests;

public class CommandPaletteTests
{
    // ── Helpers ───────────────────────────────────────────────────────

    private static Command MakeCmd(string name, string desc, string[]? argSuggestions = null)
        => new(name, desc, (_, _) => Task.CompletedTask, argSuggestions);

    private static CommandPalette MakePalette(params Command[] cmds) => new(cmds);

    private static string StripMarkup(string markup)
    {
        // Remove Spectre markup tags like [grey]...[/]
        int i;
        while ((i = markup.IndexOf('[')) >= 0)
        {
            int close = markup.IndexOf(']', i);
            if (close < 0) break;
            string tag = markup[i..(close + 1)];
            // Skip escaped bracket [[ → [
            if (tag == "[[")
            {
                markup = markup[..i] + "[" + markup[(i + 2)..];
                continue;
            }
            // Check if closing tag
            bool isClosing = tag.StartsWith("[/");
            markup = markup[..i] + markup[(close + 1)..];
            if (!isClosing)
            {
                // Find and remove the closing tag
                int closeIdx = markup.IndexOf("[/]");
                if (closeIdx >= 0 && closeIdx < markup.IndexOf("[", i))
                {
                    markup = markup[..closeIdx] + markup[(closeIdx + 3)..];
                }
            }
        }
        return markup.Trim();
    }

    private static List<string> NonEmptyHints(IReadOnlyList<string> hints)
        => hints.Where(h => !string.IsNullOrEmpty(h)).Select(StripMarkup).ToList();

    // ── Command Hints (existing behavior) ────────────────────────────

    [Fact]
    public void GetHints_NotCommand_ReturnsEmpty()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var hints = palette.GetHints("hello");
        Assert.All(hints, h => Assert.Empty(h));
    }

    [Fact]
    public void GetHints_SlashOnly_ReturnsEmpty()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var hints = palette.GetHints("/");
        Assert.All(hints, h => Assert.Empty(h));
    }

    [Fact]
    public void GetHints_MatchingCommand_ShowsHint()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var hints = palette.GetHints("/config");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Contains(nonEmpty, h => h.Contains("config"));
    }

    [Fact]
    public void GetHints_PartialMatch_ShowsHint()
    {
        var palette = MakePalette(
            MakeCmd("config", "Configure settings"),
            MakeCmd("help", "Show help"));
        var hints = palette.GetHints("/con");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Single(nonEmpty);
        Assert.Contains("config", nonEmpty[0]);
    }

    [Fact]
    public void GetHints_MultipleMatches_ShowsAll()
    {
        var palette = MakePalette(
            MakeCmd("config", "Configure settings"),
            MakeCmd("connect", "Connect to server"));
        var hints = palette.GetHints("/c");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Equal(2, nonEmpty.Count);
        Assert.Contains(nonEmpty, h => h.Contains("config"));
        Assert.Contains(nonEmpty, h => h.Contains("connect"));
    }

    [Fact]
    public void GetHints_NoMatch_ReturnsEmpty()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var hints = palette.GetHints("/xyz");
        Assert.All(hints, h => Assert.Empty(h));
    }

    [Fact]
    public void GetHints_CachesResult()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var first = palette.GetHints("/config");
        var second = palette.GetHints("/config");
        Assert.Same(first, second);
    }

    // ── Command Hints with Arguments (mixed mode) ────────────────────

    [Fact]
    public void GetHints_CommandWithArgs_MultipleMatches_ShowsCommandHints()
    {
        // When multiple commands match, argument suggestions are not shown
        var palette = MakePalette(
            MakeCmd("config", "Configure settings", ["show", "set key value"]),
            MakeCmd("connect", "Connect to server"));
        var hints = palette.GetHints("/c");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Equal(2, nonEmpty.Count);
        // Should show command descriptions, not argument suggestions
        Assert.Contains(nonEmpty, h => h.Contains("Configure"));
        Assert.Contains(nonEmpty, h => h.Contains("Connect"));
    }

    [Fact]
    public void GetHints_CommandWithArgs_NoSpaceYet_ShowsCommandHint()
    {
        // Without space after command name, don't show argument suggestions
        var palette = MakePalette(MakeCmd("config", "Configure settings",
            ["show", "set key value"]));
        var hints = palette.GetHints("/config");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Single(nonEmpty);
        Assert.Contains("Configure", nonEmpty[0]);
    }

    // ── Argument Suggestions ─────────────────────────────────────────

    [Fact]
    public void GetHints_ArgumentSuggestions_ShowsUniqueFirstWords()
    {
        var palette = MakePalette(MakeCmd("cmd", "A test command",
            ["pc", "mac", "linux ubuntu", "linux fedora"]));
        var hints = palette.GetHints("/cmd ");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Equal(3, nonEmpty.Count);
        Assert.Contains("pc", nonEmpty);
        Assert.Contains("mac", nonEmpty);
        Assert.Contains("linux", nonEmpty);
    }

    [Fact]
    public void GetHints_ArgumentSuggestions_FiltersByPartialWord()
    {
        // "/cmd l" is mid-word (no trailing space) → show full matching paths
        var palette = MakePalette(MakeCmd("cmd", "A test command",
            ["pc", "mac", "linux ubuntu", "linux fedora"]));
        var hints = palette.GetHints("/cmd l");
        var nonEmpty = NonEmptyHints(hints);
        // Both "linux ubuntu" and "linux fedora" match and are shown as full paths
        Assert.Equal(2, nonEmpty.Count);
        Assert.Contains(nonEmpty, h => h.Contains("linux ubuntu"));
        Assert.Contains(nonEmpty, h => h.Contains("linux fedora"));
    }

    [Fact]
    public void GetHints_ArgumentSuggestions_ShowsNextLevelAfterWord()
    {
        var palette = MakePalette(MakeCmd("cmd", "A test command",
            ["pc", "mac", "linux ubuntu", "linux fedora"]));
        var hints = palette.GetHints("/cmd linux ");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Equal(2, nonEmpty.Count);
        Assert.Contains("ubuntu", nonEmpty);
        Assert.Contains("fedora", nonEmpty);
    }

    [Fact]
    public void GetHints_ArgumentSuggestions_NoMatch_ShowsEmpty()
    {
        var palette = MakePalette(MakeCmd("cmd", "A test command",
            ["pc", "mac", "linux ubuntu"]));
        var hints = palette.GetHints("/cmd x");
        Assert.All(hints, h => Assert.Empty(h));
    }

    [Fact]
    public void GetHints_ArgumentSuggestions_RespectsMaxHeight()
    {
        var manyArgs = Enumerable.Range(0, 20).Select(i => $"arg{i}").ToArray();
        var palette = MakePalette(MakeCmd("cmd", "A test command", manyArgs));
        var hints = palette.GetHints("/cmd ");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Equal(CommandPalette.MaxHeight, nonEmpty.Count);
    }

    [Fact]
    public void GetHints_CommandWithoutArgs_StillShowsDescription()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var hints = palette.GetHints("/config ");
        var nonEmpty = NonEmptyHints(hints);
        Assert.Single(nonEmpty);
        Assert.Contains("Configure", nonEmpty[0]);
    }

    // ── GetTopSuggestion ─────────────────────────────────────────────

    [Fact]
    public void GetTopSuggestion_NoCommand_ReturnsNull()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        Assert.Null(palette.GetTopSuggestion("plain text"));
    }

    [Fact]
    public void GetTopSuggestion_SlashOnly_ReturnsNull()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        Assert.Null(palette.GetTopSuggestion("/"));
    }

    [Fact]
    public void GetTopSuggestion_MultipleCommands_CommonPrefixAdvances_ReturnsPrefix()
    {
        var palette = MakePalette(
            MakeCmd("configure", "Settings"),
            MakeCmd("conflict", "Conflict"));
        // "configure" and "conflict" share "conf"
        var result = palette.GetTopSuggestion("/con");
        Assert.Equal("/conf", result);
    }

    [Fact]
    public void GetTopSuggestion_MultipleCommands_NoAdvance_ReturnsNull()
    {
        var palette = MakePalette(
            MakeCmd("config", "Configure settings"),
            MakeCmd("connect", "Connect to server"));
        // "config" and "connect" share only "con" which == cmdPrefix
        var result = palette.GetTopSuggestion("/con");
        Assert.Null(result);
    }

    [Fact]
    public void GetTopSuggestion_SingleCommand_CompletesNameWithSpace()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var result = palette.GetTopSuggestion("/con");
        Assert.Equal("/config ", result);
    }

    [Fact]
    public void GetTopSuggestion_ExactName_CompletesWithSpace()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var result = palette.GetTopSuggestion("/config");
        Assert.Equal("/config ", result);
    }

    [Fact]
    public void GetTopSuggestion_ExactNameWithSpace_WithArgSuggestions_CompletesFirst()
    {
        var palette = MakePalette(MakeCmd("cmd", "Test",
            ["pc", "mac", "linux ubuntu"]));
        // "linux ubuntu" sorts before "mac" before "pc" alphabetically
        var result = palette.GetTopSuggestion("/cmd ");
        Assert.Equal("/cmd linux ubuntu ", result);
    }

    [Fact]
    public void GetTopSuggestion_PartialArg_CompletesToFirstMatch()
    {
        var palette = MakePalette(MakeCmd("cmd", "Test",
            ["pc", "mac", "linux ubuntu", "linux fedora"]));
        var result = palette.GetTopSuggestion("/cmd l");
        Assert.Equal("/cmd linux fedora ", result);
    }

    [Fact]
    public void GetTopSuggestion_AfterWord_CompletesToFirstSubMatch()
    {
        var palette = MakePalette(MakeCmd("cmd", "Test",
            ["linux ubuntu", "linux fedora"]));
        var result = palette.GetTopSuggestion("/cmd linux ");
        Assert.Equal("/cmd linux fedora ", result);
    }

    [Fact]
    public void GetTopSuggestion_SingleExactMatch_AppendsSpace()
    {
        var palette = MakePalette(MakeCmd("cmd", "Test",
            ["linux ubuntu"]));
        var result = palette.GetTopSuggestion("/cmd linux ");
        Assert.Equal("/cmd linux ubuntu ", result);
    }

    [Fact]
    public void GetTopSuggestion_SingleExactMatchNoSpace_AppendsSpace()
    {
        var palette = MakePalette(MakeCmd("cmd", "Test",
            ["linux ubuntu"]));
        var result = palette.GetTopSuggestion("/cmd linux u");
        Assert.Equal("/cmd linux ubuntu ", result);
    }

    [Fact]
    public void GetTopSuggestion_NoMatchingArgs_ReturnsNull()
    {
        var palette = MakePalette(MakeCmd("cmd", "Test",
            ["pc", "mac"]));
        var result = palette.GetTopSuggestion("/cmd x");
        Assert.Null(result);
    }

    [Fact]
    public void GetTopSuggestion_NoSuggestions_ReturnsNullOnArgPhase()
    {
        var palette = MakePalette(MakeCmd("config", "Configure settings"));
        var result = palette.GetTopSuggestion("/config set");
        Assert.Null(result);
    }

    [Fact]
    public void GetTopSuggestion_CompleteToAnything_PreservesCase()
    {
        var palette = MakePalette(MakeCmd("CONFIG", "Settings",
            ["Set Value", "Show All"]));
        var result = palette.GetTopSuggestion("/CONFIG ");
        Assert.Equal("/CONFIG Set Value ", result);
    }
}
