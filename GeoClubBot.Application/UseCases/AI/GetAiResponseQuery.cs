using UseCases.Abstractions;

namespace UseCases.UseCases.AI;

/// <summary>
/// Asks the AI chatbot for a response to <paramref name="Prompt"/>. The
/// <paramref name="StartTypingAsync"/> callback is invoked once the handler is about to make
/// the actual LLM call, so the caller can show a "typing…" indicator.
/// </summary>
public sealed record GetAiResponseQuery(string Prompt, Func<Task> StartTypingAsync) : IQuery<string?>;
