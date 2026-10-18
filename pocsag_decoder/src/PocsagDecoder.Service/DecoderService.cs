using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.Service;

/// <summary>
/// Läuft ohne Fenster: startet rtl_fm + multimon-ng, verarbeitet jedes Telegramm, startet den Empfang
/// nach einem Abbruch neu und übernimmt eine ausgetauschte Sicherung ohne Neustart der App.
/// </summary>
public sealed class DecoderService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettingsCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LogMaintenanceInterval = TimeSpan.FromHours(1);

    private readonly AddonOptions _options;
    private readonly HomeAssistantClient _ha;
    private readonly WebhookSender _sender = new();
    private MessageProcessor _processor = null!;
    private DecoderPipeline? _pipeline;
    private string _decoderKey = string.Empty;
    private DateTime _settingsStamp;
    private volatile bool _restartRequested;

    public DecoderService(AddonOptions options, HomeAssistantClient ha)
    {
        _options = options;
        _ha = ha;
    }

    public static void Log(string line) => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}");

    public async Task RunAsync(CancellationToken ct)
    {
        Log($"POCSAG Decoder für Home Assistant {typeof(DecoderService).Assembly.GetName().Version?.ToString(3)}");
        _processor = new MessageProcessor(LoadSettings(), _sender, Log);
        if (!_ha.Available) Log("Kein Zugang zu Home Assistant (SUPERVISOR_TOKEN fehlt): Ereignisse und Sensor sind aus.");

        var nextMaintenance = DateTime.Now.AddSeconds(20);
        var nextSettingsCheck = DateTime.Now + SettingsCheck;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_pipeline is null || _restartRequested) await StartDecoderAsync();
                if (DateTime.Now >= nextSettingsCheck)
                {
                    nextSettingsCheck = DateTime.Now + SettingsCheck;
                    ReloadIfChanged();
                }
                if (DateTime.Now >= nextMaintenance)
                {
                    nextMaintenance = DateTime.Now + LogMaintenanceInterval;
                    MaintainLogs();
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ContinueWith(_ => { });
            }
        }
        finally
        {
            if (_pipeline is not null) await _pipeline.StopAsync();
            Log("Beendet.");
        }
    }

    private AppSettings LoadSettings()
    {
        AppSettings settings;
        var path = _options.SettingsFile;
        if (File.Exists(path))
        {
            settings = SettingsStore.Load(path);
            _settingsStamp = File.GetLastWriteTimeUtc(path);
            Log($"Einstellungen aus {path}: {settings.Rules.Count} Telegrammereignisse, {settings.Targets.Count} Schnittstellen, {settings.RicEntries.Count} RICs.");
        }
        else
        {
            settings = AppSettings.CreateDefault();
            _settingsStamp = default;
            Log($"Keine Sicherung unter {path} gefunden. Empfang läuft, aber ohne Telegrammereignisse und RIC-Liste.");
            Log("Im Windows-Programm unter Einstellungen → Sicherung erstellen und die Datei dorthin kopieren.");
        }
        foreach (var note in LinuxSettings.Apply(settings, _options)) Log("Hinweis: " + note);
        return settings;
    }

    private void ReloadIfChanged()
    {
        var path = _options.SettingsFile;
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        if (stamp == _settingsStamp) return;
        try
        {
            var settings = LoadSettings();
            _processor.Apply(settings);
            if (DecoderKey(settings.Decoder) != _decoderKey)
            {
                Log("Empfangseinstellungen geändert, Empfang wird neu gestartet.");
                _restartRequested = true;
            }
        }
        catch (Exception ex)
        {
            _settingsStamp = stamp; // nicht alle 30 s wiederholen, erst bei der nächsten Änderung
            Log("Sicherung konnte nicht gelesen werden, die bisherigen Einstellungen bleiben: " + ex.Message);
        }
    }

    private async Task StartDecoderAsync()
    {
        _restartRequested = false;
        if (_pipeline is not null)
        {
            await _pipeline.StopAsync();
            _pipeline = null;
        }
        var decoder = _processor.Settings.Decoder;
        var pipeline = new DecoderPipeline(decoder);
        pipeline.Log += (_, line) => Log(line);
        pipeline.MessageReceived += (_, message) => _ = HandleAsync(message);
        pipeline.Stopped += (_, _) =>
        {
            Log($"Empfang unterbrochen, neuer Versuch in {RestartDelay.TotalSeconds:0} s.");
            _ = Task.Delay(RestartDelay).ContinueWith(_ => _restartRequested = true);
        };
        try
        {
            pipeline.Start();
            _pipeline = pipeline;
            _decoderKey = DecoderKey(decoder);
            Log($"Empfang läuft auf {decoder.FrequencyMHz:0.000###} MHz.");
        }
        catch (Exception ex)
        {
            Log($"Empfang konnte nicht gestartet werden: {ex.Message} – neuer Versuch in {RestartDelay.TotalSeconds:0} s.");
            await pipeline.StopAsync();
            _pipeline = pipeline; // Platzhalter, damit nicht sofort erneut gestartet wird
            _ = Task.Delay(RestartDelay).ContinueWith(_ => _restartRequested = true);
        }
    }

    private static string DecoderKey(DecoderOptions d) => d.BuildRtlFmArguments() + "|" + d.BuildMultimonArguments() + "|" + d.GermanUmlauts;

    private async Task HandleAsync(PocsagMessage message)
    {
        try
        {
            var item = await _processor.ProcessAsync(message);
            Log($"RIC {message.RicFormatted}{message.SubRic} {message.Text}" + (item.Forwarding.Length > 0 ? $"  [{item.Forwarding}]" : string.Empty));
            await PublishAsync(item);
        }
        catch (Exception ex)
        {
            Log($"Fehler bei RIC {message.RicFormatted}: {ex.Message}");
        }
    }

    private async Task PublishAsync(ProcessedMessage item)
    {
        if (!_ha.Available) return;
        var events = _options.HaEvents.Trim().ToLowerInvariant();
        try
        {
            if (events == HaEventModes.All || (events == HaEventModes.Matched && item.Rules.Count > 0 && !item.IsRepeat))
                await _ha.SendEventAsync(item);
            if (_options.HaSensor) await _ha.UpdateSensorAsync(item);
        }
        catch (Exception ex)
        {
            Log("Meldung an Home Assistant fehlgeschlagen: " + ex.Message);
        }
    }

    private void MaintainLogs()
    {
        var settings = _processor.Settings;
        if (!settings.LogToCsv || !Directory.Exists(settings.LogDirectory)) return;
        try
        {
            var today = DateTime.Today;
            if (settings.LogCompress)
            {
                var result = LogMaintenance.Compress(settings.LogDirectory, today);
                if (result.Compressed > 0) Log($"Protokoll: {result.Compressed} Tag(e) komprimiert, {result.SavedBytes / 1024} KB gespart.");
            }
            if (settings.LogKeepDays > 0)
            {
                var deleted = LogMaintenance.DeleteOld(settings.LogDirectory, today, settings.LogKeepDays);
                if (deleted > 0) Log($"Protokoll: {deleted} alte Datei(en) gelöscht.");
            }
        }
        catch (Exception ex)
        {
            Log("Protokoll aufräumen fehlgeschlagen: " + ex.Message);
        }
    }
}
