using StreamShell;

namespace StreamShell.Tests;

public class CommandParserTests
{
    // ── Split ─────────────────────────────────────────────────────────

    public static IEnumerable<object[]> SplitTestData()
    {
        // [input, expectedParts]
        yield return ["", new string[0]];
        yield return ["   ", new string[0]];
        yield return ["\t\n", new string[0]];
        yield return ["hello", new[] { "hello" }];
        yield return ["  hello  ", new[] { "hello" }];
        yield return ["hello world", new[] { "hello", "world" }];
        yield return ["hello   world", new[] { "hello", "world" }];
        yield return ["a b c d", new[] { "a", "b", "c", "d" }];
        yield return ["  a   b\tc\n d  ", new[] { "a", "b", "c", "d" }];
        yield return ["--key value", new[] { "--key", "value" }];
        yield return ["/command arg1 --name val", new[] { "/command", "arg1", "--name", "val" }];
    }

    [Theory]
    [MemberData(nameof(SplitTestData))]
    public void Split_ReturnsExpectedParts(string input, string[] expected)
    {
        var result = CommandParser.Split(input);
        Assert.Equal(expected, result);
    }

    // ── Parse ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyArrays()
    {
        var (positional, named) = CommandParser.Parse("");
        Assert.Empty(positional);
        Assert.Empty(named);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyArrays()
    {
        var (positional, named) = CommandParser.Parse("   ");
        Assert.Empty(positional);
        Assert.Empty(named);
    }

    [Fact]
    public void Parse_OnlyPositionalArgs_ReturnsPositionalOnly()
    {
        var (positional, named) = CommandParser.Parse("arg1 arg2 arg3");
        Assert.Equal(new[] { "arg1", "arg2", "arg3" }, positional);
        Assert.Empty(named);
    }

    [Fact]
    public void Parse_OnlyNamedArgs_ReturnsNamedOnly()
    {
        var (positional, named) = CommandParser.Parse("--key value");
        Assert.Empty(positional);
        Assert.Equal(new Dictionary<string, string> { { "key", "value" } }, named);
    }

    [Fact]
    public void Parse_MultipleNamedArgs_ReturnsAllNamed()
    {
        var (positional, named) = CommandParser.Parse("--name Alice --role admin");
        Assert.Empty(positional);
        Assert.Equal(2, named.Count);
        Assert.Equal("Alice", named["name"]);
        Assert.Equal("admin", named["role"]);
    }

    [Fact]
    public void Parse_MixedArgs_ReturnsBoth()
    {
        var (positional, named) = CommandParser.Parse("arg1 --key val arg2");
        Assert.Equal(new[] { "arg1", "arg2" }, positional);
        Assert.Equal(new Dictionary<string, string> { { "key", "val" } }, named);
    }

    [Fact]
    public void Parse_NamedWithoutValue_UsesEmptyString()
    {
        var (positional, named) = CommandParser.Parse("--verbose");
        Assert.Empty(positional);
        Assert.Equal(new Dictionary<string, string> { { "verbose", "" } }, named);
    }

    [Fact]
    public void Parse_NamedFollowedByAnotherNamed_FirstGetsEmptyValue()
    {
        var (positional, named) = CommandParser.Parse("--a --b val");
        Assert.Empty(positional);
        Assert.Equal("", named["a"]);
        Assert.Equal("val", named["b"]);
    }

    [Fact]
    public void Parse_ArgsWithLeadingSlash_TreatedAsPositional()
    {
        var (positional, named) = CommandParser.Parse("/command arg1 --key val");
        Assert.Equal(new[] { "/command", "arg1" }, positional);
        Assert.Equal(new Dictionary<string, string> { { "key", "val" } }, named);
    }

    [Fact]
    public void Parse_NamedValueWithDashes_NotTreatedAsNewNamed()
    {
        var (positional, named) = CommandParser.Parse("--path /usr/local/bin");
        Assert.Empty(positional);
        Assert.Equal(new Dictionary<string, string> { { "path", "/usr/local/bin" } }, named);
    }

    [Fact]
    public void Parse_QuotedArgs_PreservesSpacesWithinQuotes()
    {
        var (positional, named) = CommandParser.Parse("/command \"my file.txt\" arg2");
        Assert.Equal(new[] { "/command", "my file.txt", "arg2" }, positional);
        Assert.Empty(named);
    }

    [Fact]
    public void Parse_QuotedNamedArgValue_PreservesSpaces()
    {
        var (positional, named) = CommandParser.Parse("--path \"/usr/local/bin\"");
        Assert.Empty(positional);
        Assert.Equal(new Dictionary<string, string> { { "path", "/usr/local/bin" } }, named);
    }

    [Fact]
    public void Parse_MixedQuotedAndUnquotedArgs_Works()
    {
        var (positional, named) = CommandParser.Parse("arg1 \"arg with spaces\" arg3 --flag \"flag value\"");
        Assert.Equal(new[] { "arg1", "arg with spaces", "arg3" }, positional);
        Assert.Equal(new Dictionary<string, string> { { "flag", "flag value" } }, named);
    }

    [Fact]
    public void Parse_EmptyQuotes_ReturnsEmptyString()
    {
        var (positional, named) = CommandParser.Parse("\"\"");
        Assert.Equal(new[] { "" }, positional);
        Assert.Empty(named);
    }

    [Fact]
    public void Parse_UnclosedQuote_ConsumesRestOfInput()
    {
        var (positional, named) = CommandParser.Parse("start \"unclosed arg");
        Assert.Equal(new[] { "start", "unclosed arg" }, positional);
        Assert.Empty(named);
    }
}
