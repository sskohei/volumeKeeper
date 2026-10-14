namespace VolumeKeeper;

// Session peak meters report the signal before the session volume is applied.
// Keep a short peak envelope, reduce loud sessions promptly, and recover slowly.
internal static class LoudnessPolicy
{
    public const float TargetPeak = 0.12f;
    public const float SilenceFloor = 0.005f;
    public const float PeakDecay = 0.95f;
    public const float RecoveryStep = 0.015f;

    public static float NextPeak(float previous, float measured) =>
        Math.Max(Math.Clamp(measured, 0f, 1f), previous * PeakDecay);

    public static float NextVolume(float baseline, float applied, float peakEnvelope, float measured)
    {
        if (measured < SilenceFloor) return applied;
        var desired = Math.Min(baseline, TargetPeak / Math.Max(peakEnvelope, SilenceFloor));
        return desired < applied ? desired : Math.Min(desired, applied + RecoveryStep);
    }
}
