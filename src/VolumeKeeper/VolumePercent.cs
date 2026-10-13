using System.Globalization;

namespace VolumeKeeper;

internal static class VolumePercent
{
    public static decimal ToDecimal(float level) =>
        Math.Round(Math.Clamp((decimal)level * 100m, 0m, 100m), 2,
            MidpointRounding.AwayFromZero);

    public static string Format(float level) =>
        ToDecimal(level).ToString("0.##", CultureInfo.CurrentCulture);
}
