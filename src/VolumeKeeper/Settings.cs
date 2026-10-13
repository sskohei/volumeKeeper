using System.Text.Json;

namespace VolumeKeeper;

internal sealed class DeviceProfile
{
    public string EndpointId { get; set; } = "";
    public string? StableId { get; set; }
    public string Name { get; set; } = "";
    public float Volume { get; set; }
}

internal sealed class AppSettings
{
    public bool Paused { get; set; }
    public List<DeviceProfile> Profiles { get; set; } = [];

    public DeviceProfile? Find(DeviceInfo device)
    {
        if (!string.IsNullOrEmpty(device.StableId))
        {
            var stableMatch = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.StableId, device.StableId, StringComparison.Ordinal));
            if (stableMatch is not null) return stableMatch;
        }
        var endpointMatches = Profiles.Where(profile =>
            string.Equals(profile.EndpointId, device.EndpointId, StringComparison.Ordinal) &&
            (string.IsNullOrEmpty(device.StableId) || string.IsNullOrEmpty(profile.StableId)))
            .Take(2).ToArray();
        return endpointMatches.Length == 1 ? endpointMatches[0] : null;
    }

    public DeviceProfile Save(DeviceInfo device, float volume)
    {
        var profile = Find(device);
        if (profile is null)
        {
            profile = new DeviceProfile();
            Profiles.Add(profile);
        }

        profile.EndpointId = device.EndpointId;
        profile.StableId = device.StableId ?? profile.StableId;
        profile.Name = device.Name;
        profile.Volume = Math.Clamp(volume, 0f, 1f);
        return profile;
    }
}

internal sealed class SettingsStore
{
    public string Path { get; }

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VolumeKeeper", "settings.json");
    }

    public AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(Path)) return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path))
                ?? throw new JsonException("設定ファイルが空です。");
            settings.Profiles ??= [];
            settings.Profiles = settings.Profiles.Where(profile =>
                !string.IsNullOrEmpty(profile.EndpointId) &&
                float.IsFinite(profile.Volume) &&
                profile.Volume is >= 0f and <= 1f).ToList();
            return settings;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = $"設定を読み込めませんでした: {error.Message}";
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temporary, Path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
