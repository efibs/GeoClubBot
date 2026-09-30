namespace UseCases.UseCases.CountryChallenges;

public static class CountryChallengeCalendar
{
    /// <summary>The date at <paramref name="now"/> in <paramref name="timeZone"/>, which decides the weekday.</summary>
    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

    /// <summary>The first <paramref name="day"/> on or after <paramref name="from"/>.</summary>
    public static DateOnly NextOccurrence(DateOnly from, DayOfWeek day) =>
        from.AddDays(((int)day - (int)from.DayOfWeek + 7) % 7);
}
