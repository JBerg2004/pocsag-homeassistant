using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocsagDecoder.Service;

/// <summary>Einstellungen aus dem Reiter „Konfiguration“ der App in Home Assistant (/data/options.json).</summary>
public sealed class AddonOptions
{
    public const string DefaultPath = "/data/options.json";

    /// <summary>Sicherung bzw. settings.json aus dem Windows-Programm.</summary>
    [JsonPropertyName("settings_file")]
    public string SettingsFile { get; set; } = "/share/pocsag/settings.json";

    /// <summary>0 = Frequenz aus der Sicherung.</summary>
    [JsonPropertyName("frequency_mhz")]
    public double FrequencyMHz { get; set; }

    /// <summary>Leer = Verstärkung aus der Sicherung, „auto“ = automatisch, sonst dB.</summary>
    [JsonPropertyName("gain")]
    public string Gain { get; set; } = string.Empty;

    /// <summary>null = Korrektur aus der Sicherung.</summary>
    [JsonPropertyName("ppm")]
    public int? Ppm { get; set; }

    [JsonPropertyName("device_index")]
    public int? DeviceIndex { get; set; }

    /// <summary>„alle“, „treffer“ (nur Telegrammereignisse) oder „aus“.</summary>
    [JsonPropertyName("ha_events")]
    public string HaEvents { get; set; } = HaEventModes.All;

    [JsonPropertyName("ha_sensor")]
    public bool HaSensor { get; set; } = true;

    [JsonPropertyName("log_to_csv")]
    public bool LogToCsv { get; set; } = true;

    [JsonPropertyName("log_directory")]
    public string LogDirectory { get; set; } = "/share/pocsag/logs";

    public static AddonOptions Load(string path = DefaultPath)
    {
        if (!File.Exists(path)) return new AddonOptions();
        return JsonSerializer.Deserialize<AddonOptions>(File.ReadAllText(path)) ?? new AddonOptions();
    }
}

public static class HaEventModes
{
    public const string All = "alle";
    public const string Matched = "treffer";
    public const string Off = "aus";
}
