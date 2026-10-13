namespace VolumeKeeper;

internal sealed class DeviceInfo
{
    public required string EndpointId { get; init; }
    public string? StableId { get; init; }
    public required string Name { get; init; }
    public float Volume { get; set; }
    public bool Muted { get; set; }
}
