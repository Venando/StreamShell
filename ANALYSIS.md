# Bug Analysis: Autocomplete Hints for Sub-commands

## Bug Report Summary

When typing `/appconfig DirectLlm`, the hint palette shows one hint:
- `/appconfig DirectLlm`

Instead of four individual hints:
- `/appconfig DirectLlmApiType`
- `/appconfig DirectLlmModelName`
- `/appconfig DirectLlmToken`
- `/appconfig DirectLlmUrl`

## Code Walkthrough

### Command Registration

The `/appconfig` command is registered in `openclaw-ptt-client`'s `StreamShellInputHandler.cs`:
```csharp
var appConfigSuggestions = OpenClawCommandSuggestions.GetAppConfigSuggestions();
AddNativeCommand("appconfig", "...", ConfigHandler, appConfigSuggestions);
```

`GetAppConfigSuggestions()` reflects over `AppConfig` to return all public writable property names. Four match the `DirectLlm` prefix:
- `DirectLlmApiType`
- `DirectLlmModelName`
- `DirectLlmToken`
- `DirectLlmUrl`

### Hint Resolution Flow (CommandPalette.GetLines)

When the user types `/appconfig DirectLlm`:

1. **Input parsing**: `query = "appconfig DirectLlm"`, `spaceIndex = 9`
2. **Command matching** (`GetMatchingCommands`): Since a space is present, requires exact match on `cmdPrefix = "appconfig"`. Finds exactly 1 command → `_matchingBuffer.Count = 1`
3. **Branch selection**: Condition `_matchingBuffer.Count == 1 && suggestions.Length > 0 && spaceIndex >= 0` is TRUE → enters **argument completion mode**
4. **Args extraction**: `argsPart = "DirectLlm"`, `fullPrefix = "/appconfig "`

### Argument Suggestion Matching (GetArgMatchInfo)

`GetArgMatchInfo("DirectLlm", suggestions)`:
1. Filters suggestions starting with `"DirectLlm"` → 4 matches
2. Calculates longest common prefix of all 4 matches → `"DirectLlm"` (9 chars)
3. **KEY CHECK**: `commonPrefix.Length (9) > argsPart.Length (9)` → **false** (they are equal)
4. Returns `commonNextWord = null` (correct — the common prefix doesn't extend the typed text)

### Hint Display (CollectArgumentHints)

With `commonNextWord = null` and `atWordBoundary = false` (mid-word, no trailing space):

1. Enters the `else` branch: `"Mid-word with divergent matches → show each full path"`
2. Builds entries: `"/appconfig DirectLlmApiType"`, `"/appconfig DirectLlmModelName"`, `"/appconfig DirectLlmToken"`, `"/appconfig DirectLlmUrl"`
3. Sets `_lastMatchCount = 4`
4. Sets `CurrentSuggestion` to first entry with space
5. Adds all 4 as hint lines with markup

## Root Cause Analysis

The code logic in `CommandPalette.cs` **should produce 4 separate hints** for this scenario — the common prefix equals the typed text, so no compression occurs, and the else branch iterates over all matches. The actual flow appears correct.

### Suspected Issue Areas

Based on close reading, I found **two potential issues** that could produce the reported symptom:

#### 1. ⚠️ Status line uses wrong match count (UX issue, not the reported bug)

```csharp
int startIdx = ScrollOffset + 1;
int endIdx = Math.Min(ScrollOffset + HintCapacity, _matchingBuffer.Count);
_linesBuffer.Add($"[dim]Tab: autocomplete  {startIdx}-{endIdx}/{_matchingBuffer.Count}[/]");
```

The status line is built **before** `CollectArgumentHints` is called. It uses `_matchingBuffer.Count` (which is 1 — the single "appconfig" command) instead of `_lastMatchCount` (which is updated inside `CollectArgumentHints` to `entries.Count` = 4). This means the status line shows "1-1/1" instead of "1-4/4" when there are 4 argument suggestions. However, this only affects the scroll position display — the actual hint entries would still render correctly.

#### 2. ⚠️ Cache invalidation: `_lastMatchCount` stale after input change (POTENTIAL ROOT CAUSE)

```csharp
bool inputChanged = currentInput != _lastInput;
// ...
if (inputChanged)
    ResetSelection();
// ...
GetMatchingCommands(query, _matchingBuffer);
_lastMatchCount = _matchingBuffer.Count;  // ← SET TO COMMAND COUNT (1)
// Build status line with _matchingBuffer.Count (1)...
// Enter argument completion mode...
CollectArgumentHints(..., suggestions);     // ← OVERWRITES _lastMatchCount TO 4
```

While `_lastMatchCount` is correctly overwritten inside `CollectArgumentHints` for the **current call**, the **status line** has already been built with the old value. But this shouldn't cause wrong hint display.

**The real issue might be in the next call**: If the palette's `GetLines` is called again with the same input (cache hit), the cached lines are returned. But if the palette is called with a DIFFERENT input that triggers the command hint mode (else branch), `_lastMatchCount` retains the last value from argument mode, which affects selection clamping in the background loop.

## Most Likely Root Cause

After thorough analysis of all code paths, the **most probable cause** is:

### The `atWordBoundary` branch builds entries with a double-space defect

When the user types `/appconfig DirectLlm ` (WITH trailing space), `argsPart = "DirectLlm "`. The `atWordBoundary` branch:

```csharp
_sb.Append(cmdPath);    // "/appconfig "
_sb.Append(argsPart);   // "DirectLlm " (WITH trailing space)
_sb.Append(firstWord);  // "ApiType"
```

Produces entry: `"/appconfig DirectLlm ApiType"` (with space between "DirectLlm" and "ApiType") — which does **NOT** match the expected property name `"DirectLlmApiType"` (no space). When the user tabs this suggestion, it would insert the wrong command, which may cause confusion.

However, this doesn't fully explain the reported symptom of **only one hint showing instead of four**.

### Bottom line

The code logic **appears correct for the reported scenario** (no trailing space, mid-word). The `commonNextWord` compression correctly doesn't trigger when the typed prefix equals the common prefix. The `else` branch correctly builds separate entries for each matching suggestion.

**Recommendation**: Add a test case that exactly reproduces the scenario — typing `/appconfig DirectLlm` against suggestions `["DirectLlmApiType", "DirectLlmModelName", "DirectLlmToken", "DirectLlmUrl"]` — to verify the expected 4 hints appear, with no compression. If the test passes but the app still shows the bug, the issue may be in the calling code (PTT client's suggestion setup or panel recreation) rather than in StreamShell's `CommandPalette` logic.
