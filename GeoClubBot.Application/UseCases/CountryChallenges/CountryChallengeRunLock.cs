namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Everything that creates or evaluates country challenges waits for everything else that does: a manual
/// run racing the job would create the same day's challenges twice, and two evaluations at once would
/// award the same points twice, before either had stored its work.
/// </summary>
internal static class CountryChallengeRunLock
{
    public static readonly SemaphoreSlim Semaphore = new(1, 1);
}
