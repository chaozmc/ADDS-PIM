namespace ADDS.PIM.AdWorker.Hosting;

internal static class WorkerCommandTimeWindow
{
    public static bool IsCurrent(DateTimeOffset issuedUtc, DateTimeOffset nowUtc, int maxAgeSeconds)
    {
        var age = nowUtc - issuedUtc;
        return age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(maxAgeSeconds);
    }
}
