using System.Net;
using System.Text.Json.Nodes;
using PocsagDecoder.Core.Decoding;
using PocsagDecoder.Core.Descriptions;
using PocsagDecoder.Core.Filtering;
using PocsagDecoder.Core.Forwarding;
using PocsagDecoder.Core.Models;
using PocsagDecoder.Core.Settings;
using Xunit;

namespace PocsagDecoder.Service.Tests;

public class ServiceTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add((request, body));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static PocsagMessage Message(int ric = 1234567, int function = 0, string text = "Brand 3") =>
        new() { Ric = ric, Function = function, Text = text, Baud = 1200, Timestamp = new DateTime(2026, 10, 18, 12, 0, 0) };

    private static AppSettings SettingsWithRule()
    {
        var settings = AppSettings.CreateDefault();
        settings.Rules.Clear();
        settings.Targets.Clear();
        settings.Rules.Add(new FilterRule { Name = "Einsatz", RicPattern = "1234567", SubRicA = true, TargetName = "Hook" });
        settings.Targets.Add(new WebhookTarget { Name = "Hook", Url = "http://example.test/hook?ric={ric}", Method = "GET" });
        settings.RicEntries.Add(new RicEntry { Ric = 1234567, Name = "Testwache" });
        settings.LogToCsv = false;
        return settings;
    }

    [Fact]
    public void ReadsAddonOptions()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, """{"settings_file":"/share/x.json","frequency_mhz":173.255,"gain":"auto","ppm":3,"ha_events":"treffer","ha_sensor":false}""");
        var o = AddonOptions.Load(file);
        File.Delete(file);
        Assert.Equal("/share/x.json", o.SettingsFile);
        Assert.Equal(173.255, o.FrequencyMHz);
        Assert.Equal(3, o.Ppm);
        Assert.Null(o.DeviceIndex);
        Assert.Equal(HaEventModes.Matched, o.HaEvents);
        Assert.False(o.HaSensor);
    }

    [Fact]
    public void AdaptsWindowsSettingsForLinux()
    {
        var settings = AppSettings.CreateDefault();
        settings.Decoder.RtlFmPath = @"C:\SDR\rtl_fm.exe";
        settings.Decoder.Mode = DecoderSourceMode.CustomCommand;
        settings.Decoder.GainDb = 20;
        settings.Targets.Add(new WebhookTarget { Name = "DB", Kind = TargetKinds.Database, FilePath = @"C:\Daten\alarme.db" });
        settings.Targets.Add(new WebhookTarget { Name = "Ton", Kind = TargetKinds.Sound, FilePath = @"C:\gong.wav" });

        var notes = LinuxSettings.Apply(settings, new AddonOptions { FrequencyMHz = 172.5, Gain = "38,6", Ppm = -2, SettingsFile = "/share/pocsag/settings.json" });

        Assert.Equal("rtl_fm", settings.Decoder.RtlFmPath);
        Assert.Equal("multimon-ng", settings.Decoder.MultimonPath);
        Assert.Equal(DecoderSourceMode.RtlSdr, settings.Decoder.Mode);
        Assert.Equal(172.5, settings.Decoder.FrequencyMHz);
        Assert.Equal(38.6, settings.Decoder.GainDb);
        Assert.Equal(-2, settings.Decoder.Ppm);
        Assert.Equal("/share/pocsag/alarme.db", settings.Targets.Single(t => t.Name == "DB").FilePath);
        Assert.Contains(notes, n => n.Contains("Ton"));
        Assert.Contains(notes, n => n.Contains("Kommandozeile"));
    }

    [Fact]
    public void AutoGainClearsGain()
    {
        var settings = AppSettings.CreateDefault();
        settings.Decoder.GainDb = 20;
        LinuxSettings.Apply(settings, new AddonOptions { Gain = "auto" });
        Assert.Null(settings.Decoder.GainDb);
    }

    [Fact]
    public async Task ForwardsMatchesAndSuppressesRepeats()
    {
        var handler = new RecordingHandler();
        var processor = new MessageProcessor(SettingsWithRule(), new WebhookSender(new HttpClient(handler)), _ => { });

        var first = await processor.ProcessAsync(Message());
        var second = await processor.ProcessAsync(Message());
        var other = await processor.ProcessAsync(Message(ric: 7654321));

        Assert.Single(handler.Requests);
        Assert.Equal("http://example.test/hook?ric=1234567", handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal(["Einsatz"], first.Rules);
        Assert.Contains("Hook: ✓", first.Forwarding);
        Assert.Equal("Testwache", first.Message.RicDescription);
        Assert.True(second.IsRepeat);
        Assert.Equal(["Einsatz"], second.Rules);
        Assert.Contains("Wiederholung", second.Forwarding);
        Assert.Empty(other.Rules);
        Assert.False(other.HasError);
    }

    [Fact]
    public async Task ReportsMissingTarget()
    {
        var settings = SettingsWithRule();
        settings.Targets.Clear();
        var processor = new MessageProcessor(settings, new WebhookSender(new HttpClient(new RecordingHandler())), _ => { });
        var result = await processor.ProcessAsync(Message());
        Assert.True(result.HasError);
        Assert.Contains("fehlt", result.Forwarding);
    }

    [Fact]
    public async Task WritesCsvLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pocsag-test-" + Guid.NewGuid().ToString("N"));
        var settings = SettingsWithRule();
        settings.LogToCsv = true;
        settings.LogDirectory = dir;
        var processor = new MessageProcessor(settings, new WebhookSender(new HttpClient(new RecordingHandler())), _ => { });
        await processor.ProcessAsync(Message());
        var file = Assert.Single(Directory.GetFiles(dir, "pocsag-*.csv"));
        Assert.Contains("Brand 3", File.ReadAllText(file));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task PublishesEventAndSensorToHomeAssistant()
    {
        var handler = new RecordingHandler();
        var ha = new HomeAssistantClient(new HttpClient(handler), "http://supervisor/core/api", "geheim");
        var item = new ProcessedMessage(Message(), ["Einsatz"], false, "Hook: ✓ 200 OK", false);

        Assert.True(ha.Available);
        await ha.SendEventAsync(item);
        await ha.UpdateSensorAsync(item);

        Assert.Equal("http://supervisor/core/api/events/pocsag_message", handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal("Bearer geheim", handler.Requests[0].Request.Headers.Authorization!.ToString());
        var data = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal("1234567", (string?)data["ric"]);
        Assert.Equal("A", (string?)data["subric"]);
        Assert.True((bool)data["matched"]!);

        Assert.Equal("http://supervisor/core/api/states/sensor.pocsag_letzte_meldung", handler.Requests[1].Request.RequestUri!.ToString());
        var state = JsonNode.Parse(handler.Requests[1].Body)!;
        Assert.Equal("Brand 3", (string?)state["state"]);
        Assert.Equal("Brand 3", (string?)state["attributes"]!["text"]);
    }

    [Fact]
    public void SensorStateFitsHomeAssistantLimit()
    {
        var item = new ProcessedMessage(Message(text: new string('x', 400)), [], false, "", false);
        Assert.True(((string?)HomeAssistantClient.BuildSensorState(item)["state"])!.Length <= 255);
    }

    [Fact]
    public void WithoutTokenNothingIsAvailable()
    {
        Assert.False(new HomeAssistantClient(new HttpClient(new RecordingHandler()), token: "").Available);
    }
}
