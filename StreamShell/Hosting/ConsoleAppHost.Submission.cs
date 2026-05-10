namespace StreamShell;

// ReSharper disable once PartialTypeWithSinglePart
public partial class ConsoleAppHost
{
    private void HandleSubmittedInput(string submittedInput, int windowWidth)
    {
        _renderer.ClearInputBlock(submittedInput);

        bool hasAttachments = _inputHandler.Attachments.Count > 0;
        bool isCommand = !hasAttachments
            && CommandManager.TryGetCommandName(submittedInput, out string? commandName)
            && _commandManager.Contains(commandName!);

        var inputType = isCommand ? InputType.Command : InputType.PlainText;
        UserInputSubmitted?.Invoke(new UserInputSubmittedEventArgs
        {
            InputType = inputType,
            Attachments = _inputHandler.Attachments,
            RawOutput = submittedInput
        });

        if (isCommand)
            _ = ExecuteCommandAsync(submittedInput);

        _inputHandler.Reset();
    }

    /// <summary>Executes a command asynchronously via the CommandManager.</summary>
    private async Task ExecuteCommandAsync(string input)
    {
        string? error = await _commandManager.ExecuteAsync(input);
        if (error is not null)
            AddMessage(error);
    }
}
