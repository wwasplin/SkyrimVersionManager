namespace SkyrimVersionManager.Services;

public static class VersionMath
{
    public static int Compare(string a, string b)
    {
        static int[] Parse(string v) => v.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        int[] pa = Parse(a), pb = Parse(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int ca = i < pa.Length ? pa[i] : 0, cb = i < pb.Length ? pb[i] : 0;
            if (ca != cb) return ca.CompareTo(cb);
        }
        return 0;
    }

    /// <summary>
    /// The compatibility "era" of a version: "1.5" (classic SE), "1.6" (AE), "1.7", ...
    /// Crossing an era boundary breaks native DLL mods (Address Library databases are per-era)
    /// and can strand save games.
    /// </summary>
    public static string Era(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? parts[0] + "." + parts[1] : version;
    }
}
