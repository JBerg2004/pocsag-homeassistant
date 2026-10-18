using System.Globalization;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.Service;

/// <summary>Macht die Einstellungen des Windows-Programms unter Linux lauffähig und übernimmt die App-Optionen.</summary>
public static class LinuxSettings
{
    public const string RtlFm = "rtl_fm";
    public const string Multimon = "multimon-ng";

    /// <summary>Passt <paramref name="settings"/> an und liefert Hinweise für das Protokoll.</summary>
    public static List<string> Apply(AppSettings settings, AddonOptions options)
    {
        var notes = new List<string>();
        var d = settings.Decoder;
        // Pfade aus Windows (C:\…\rtl_fm.exe) passen hier nicht; im Container liegen die Programme im PATH.
        d.RtlFmPath = RtlFm;
        d.MultimonPath = Multimon;
        if (d.Mode == DecoderSourceMode.CustomCommand)
        {
            d.Mode = DecoderSourceMode.RtlSdr;
            notes.Add("Eigene Kommandozeile wird hier nicht unterstützt, es wird der normale RTL-SDR-Empfang verwendet.");
        }
        if (options.FrequencyMHz > 0) d.FrequencyMHz = options.FrequencyMHz;
        if (options.Ppm is { } ppm) d.Ppm = ppm;
        if (options.DeviceIndex is { } index) d.DeviceIndex = index;
        var gain = options.Gain.Trim();
        if (gain.Equals("auto", StringComparison.OrdinalIgnoreCase)) d.GainDb = null;
        else if (double.TryParse(gain.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var db)) d.GainDb = db;
        else if (gain.Length > 0) notes.Add($"Verstärkung \"{gain}\" nicht verstanden, es gilt der Wert aus der Sicherung.");

        settings.LogToCsv = options.LogToCsv;
        settings.LogDirectory = options.LogDirectory;

        foreach (var t in settings.Targets.Where(t => t.Enabled))
        {
            if (t.Kind == TargetKinds.Sound)
                notes.Add($"Schnittstelle \"{t.Name}\": Ton abspielen geht in Home Assistant nicht.");
            else if (t.Kind == TargetKinds.Program && LooksLikeWindowsPath(t.FilePath))
                notes.Add($"Schnittstelle \"{t.Name}\": \"{t.FilePath}\" ist ein Windows-Programm und läuft hier nicht.");
            else if (t.Kind == TargetKinds.Database && (string.IsNullOrWhiteSpace(t.FilePath) || LooksLikeWindowsPath(t.FilePath)))
            {
                var name = string.IsNullOrWhiteSpace(t.FilePath) ? "telegramme.db" : WindowsFileName(t.FilePath);
                t.FilePath = Path.Combine(Path.GetDirectoryName(options.SettingsFile) ?? "/share/pocsag", name);
                notes.Add($"Schnittstelle \"{t.Name}\": Datenbank liegt jetzt unter {t.FilePath}.");
            }
        }
        return notes;
    }

    private static bool LooksLikeWindowsPath(string? path) =>
        !string.IsNullOrEmpty(path) && (path.Contains('\\') || (path.Length > 1 && path[1] == ':')
            || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

    private static string WindowsFileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
