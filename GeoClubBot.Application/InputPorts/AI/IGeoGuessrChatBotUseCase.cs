namespace UseCases.InputPorts.AI;

public interface IGeoGuessrChatBotUseCase
{
    Task<string?> GetAiResponseAsync(string prompt, Func<Task> startTypingAsync);
}