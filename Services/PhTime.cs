namespace GrabLog.Services;

public static class PhTime
{
    private static readonly TimeZoneInfo Tz = GetManila();

    private static TimeZoneInfo GetManila()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
            catch { return TimeZoneInfo.CreateCustomTimeZone("PHT", TimeSpan.FromHours(8), "PHT", "PHT"); }
        }
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    public static DateTime ToPhilippines(DateTime utcOrUnspecified)
    {
        var utc = utcOrUnspecified.Kind switch
        {
            DateTimeKind.Utc => utcOrUnspecified,
            DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Tz);
    }

    public static string Stamp(DateTime? utc = null)
    {
        var ph = utc.HasValue ? ToPhilippines(utc.Value) : Now;
        return ph.ToString("MMM d, yyyy h:mm tt") + " PHT";
    }

    public static string TimeOnly(DateTime? utc = null)
    {
        var ph = utc.HasValue ? ToPhilippines(utc.Value) : Now;
        return ph.ToString("h:mm tt") + " PHT";
    }
}
