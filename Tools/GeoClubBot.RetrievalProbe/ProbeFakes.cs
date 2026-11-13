using System.Reflection;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Stand-ins for the bot's ports the probe must not reach (its database, GeoGuessr): every method
/// throws unless it is answered by name, so a handler that reaches for anything else is stopped, not
/// silently answered.
/// </summary>
public static class ProbeFakes
{
    /// <summary>A <typeparamref name="T"/> that answers nothing: every call throws.</summary>
    public static T Refusing<T>()
        where T : class => Answering<T>(new Dictionary<string, Func<object?[], object?>>());

    /// <summary>A <typeparamref name="T"/> whose named methods return what <paramref name="answers"/> gives, and whose others throw.</summary>
    public static T Answering<T>(IReadOnlyDictionary<string, Func<object?[], object?>> answers)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, AnsweringProxy>();
        ((AnsweringProxy)(object)proxy).Answers = answers;
        ((AnsweringProxy)(object)proxy).Name = typeof(T).Name;
        return proxy;
    }

    public class AnsweringProxy : DispatchProxy
    {
        internal IReadOnlyDictionary<string, Func<object?[], object?>> Answers { get; set; } = new Dictionary<string, Func<object?[], object?>>();

        internal string Name { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is not null && Answers.TryGetValue(targetMethod.Name, out var answer)
                ? answer(args ?? [])
                : throw new InvalidOperationException($"GeoClubBot.RetrievalProbe does not reach {Name}.{targetMethod?.Name}: refusing.");
    }
}
