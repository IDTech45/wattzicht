namespace Wattzicht.Api.Services;

/// <summary>Hulpfuncties voor Nederlandse lokale tijd (dagsamenvattingen en "overdag"-regels werken op lokale klok).</summary>
public static class DutchTime
{
    public static readonly TimeZoneInfo Zone = Find();

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(ToLocal(utc).DateTime);

    /// <summary>Uur van de dag (0–23) in Nederlandse tijd.</summary>
    public static int LocalHour(DateTimeOffset utc) => ToLocal(utc).Hour;

    /// <summary>Begin van de lokale dag als UTC-moment.</summary>
    public static DateTimeOffset StartOfLocalDayUtc(DateOnly date)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = Zone.GetUtcOffset(localMidnight);
        return new DateTimeOffset(localMidnight, offset).ToUniversalTime();
    }

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Europe/Amsterdam", "W. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Amsterdam-fallback", TimeSpan.FromHours(1), "Amsterdam", "Amsterdam");
    }
}
