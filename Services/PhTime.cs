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

    public static string Format(DateTime? utcOrUnspecified, string dateFmt = "MMM d, yyyy", string timeFmt = "h:mm:ss tt")
    {
        if (!utcOrUnspecified.HasValue) return "—";
        var ph = ToPhilippines(utcOrUnspecified.Value);
        return ph.ToString(dateFmt) + " " + ph.ToString(timeFmt) + " PHT";
    }

    public static (string date, string time) Split(DateTime? utcOrUnspecified)
    {
        if (!utcOrUnspecified.HasValue) return ("—", "");
        var ph = ToPhilippines(utcOrUnspecified.Value);
        return (ph.ToString("MMM d, yyyy"), ph.ToString("h:mm:ss tt") + " PHT");
    }
}
