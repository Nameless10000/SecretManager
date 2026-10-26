using System.Text.Json;

namespace SecretManager.App.Services;

internal sealed record DevicePreferences(bool AutomaticUpdates = true)
{
    internal static DevicePreferences Load(string folder)
    {
        try { return JsonSerializer.Deserialize<DevicePreferences>(File.ReadAllText(Path.Combine(folder, "device-settings.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "device-settings.json"); var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true);
    }
}
