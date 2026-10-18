using PocsagDecoder.Core.Descriptions;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;

namespace PocsagDecoder.Service;

/// <summary>Ergebnis eines Telegramms: zutreffende Telegrammereignisse (auch bei Wiederholungen) und was gesendet wurde.</summary>
public sealed record ProcessedMessage(PocsagMessage Message, IReadOnlyList<string> Rules, bool IsRepeat, string Forwarding, bool HasError);

/// <summary>
/// Gleiche Abfolge wie im Windows-Programm: RIC-Liste ergänzen, Telegrammereignisse prüfen,
/// Wiederholungen unterdrücken, an die Schnittstellen senden und ins Protokoll schreiben.
/// </summary>
public sealed class MessageProcessor
{
    private readonly WebhookSender _sender;
    private readonly RicDirectory _directory = new();
    private readonly DuplicateFilter _duplicates = new();
    private readonly DuplicateFilter _repeatMarker = new();
    private readonly Action<string> _log;
    private AppSettings _settings;
    private CsvMessageLog _csv;

    public MessageProcessor(AppSettings settings, WebhookSender sender, Action<string> log)
    {
        _sender = sender;
        _log = log;
        _settings = settings;
        _csv = new CsvMessageLog(settings.LogDirectory);
        Apply(settings);
    }

    public AppSettings Settings => _settings;

    /// <summary>Neue Einstellungen übernehmen (z. B. nach dem Austausch der Sicherung).</summary>
    public void Apply(AppSettings settings)
    {
        _settings = settings;
        _directory.SetGlobalSubRics(settings.GlobalSubRics);
        _directory.Rebuild(settings.RicEntries);
        _duplicates.Window = TimeSpan.FromSeconds(Math.Max(0, settings.DuplicateWindowSeconds));
        _repeatMarker.Window = _duplicates.Window;
        if (!string.IsNullOrWhiteSpace(settings.LogDirectory)) _csv = new CsvMessageLog(settings.LogDirectory);
        foreach (var error in _sender.SyncTcpServers(settings.Targets)) _log("TCP-Ausgabe: " + error);
    }

    public async Task<ProcessedMessage> ProcessAsync(PocsagMessage message, CancellationToken ct = default)
    {
        _directory.Annotate(message);
        var matched = RuleEngine.Match(_settings.Rules, message);
        var hits = matched;
        var isRepeat = _repeatMarker.IsDuplicate(message);
        var forwarding = new List<string>();
        var hasError = false;

        if (hits.Count > 0 && _duplicates.IsDuplicate(message))
        {
            forwarding.Add("Wiederholung – nicht erneut gesendet");
            hits = hits.Where(r => r.SendRepeats).ToList();
        }

        var sends = new List<Task<(string Text, bool Ok)>>();
        foreach (var rule in hits)
        {
            if (rule.TargetNames.Count == 0)
            {
                forwarding.Add($"{rule.Name}: markiert");
                continue;
            }
            foreach (var name in rule.TargetNames)
            {
                var target = _settings.Targets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    forwarding.Add($"{rule.Name}: Ziel \"{name}\" fehlt");
                    hasError = true;
                }
                else if (!target.Enabled) forwarding.Add($"{target.Name}: deaktiviert");
                else sends.Add(SendAsync(message, rule, target, ct));
            }
        }
        foreach (var (text, ok) in await Task.WhenAll(sends).ConfigureAwait(false))
        {
            forwarding.Add(text);
            hasError |= !ok;
        }

        var result = new ProcessedMessage(message, matched.Select(r => r.Name).ToList(), isRepeat, string.Join("; ", forwarding), hasError);
        WriteCsv(result);
        return result;
    }

    private async Task<(string, bool)> SendAsync(PocsagMessage message, FilterRule rule, WebhookTarget target, CancellationToken ct)
    {
        var result = await _sender.SendAsync(target, message, rule.Name, ct).ConfigureAwait(false);
        _log($"Senden RIC {message.RicFormatted}{message.SubRic} via \"{rule.Name}\" an {target.Name}: {result.Detail} (Versuche: {result.Attempts})");
        return ($"{target.Name}: {(result.Success ? "✓ " : "✗ ")}{result.Detail}", result.Success);
    }

    private void WriteCsv(ProcessedMessage item)
    {
        if (!_settings.LogToCsv) return;
        try { _csv.Append(item.Message, item.Forwarding); }
        catch (Exception ex) { _log("CSV-Protokoll fehlgeschlagen: " + ex.Message); }
    }
}
