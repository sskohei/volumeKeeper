namespace VolumeKeeper;

internal static class VolumePolicy
{
    // A 0.01% adjustment is 0.0001 in the endpoint's scalar volume.
    public const float ChangeTolerance = 0.000001f;

    public static bool ShouldApplyOnSwitch(bool changed, bool hasInitialSnapshot,
        string? previousEndpointId, string? currentEndpointId, bool paused) =>
        changed && hasInitialSnapshot && !paused && currentEndpointId is not null &&
        !string.Equals(previousEndpointId, currentEndpointId, StringComparison.Ordinal);
}
