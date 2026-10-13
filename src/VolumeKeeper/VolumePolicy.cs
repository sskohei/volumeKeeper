namespace VolumeKeeper;

internal static class VolumePolicy
{
    public static bool ShouldApplyOnSwitch(bool changed, bool hasInitialSnapshot,
        string? previousEndpointId, string? currentEndpointId, bool paused) =>
        changed && hasInitialSnapshot && !paused && currentEndpointId is not null &&
        !string.Equals(previousEndpointId, currentEndpointId, StringComparison.Ordinal);
}
