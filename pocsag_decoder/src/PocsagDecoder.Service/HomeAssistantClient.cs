using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PocsagDecoder.Service;

/// <summary>
/// Meldet Telegramme an Home Assistant: als Ereignis „pocsag_message“ (für Automationen)
/// und als Sensor „sensor.pocsag_letzte_meldung“. Nutzt die Supervisor-API der App.
/// </summary>
public sealed class HomeAssistantClient
{
    public const string EventType = "pocsag_message";
    public const string SensorId = "sensor.pocsag_letzte_meldung";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _token;

    public HomeAssistantClient(HttpClient? http = null, string? baseUrl = null, string? token = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _baseUrl = (baseUrl ?? "http://supervisor/core/api").TrimEnd('/');
        _token = token ?? Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
    }

    /// <summary>false außerhalb von Home Assistant (kein Token), dann wird nichts gemeldet.</summary>
    public bool Available => !string.IsNullOrEmpty(_token);

    public static JsonObject BuildEventData(ProcessedMessage item)
    {
        var m = item.Message;
        return new JsonObject
        {
            ["ric"] = m.RicFormatted,
            ["subric"] = m.SubRic.ToString(),
            ["function"] = m.Function,
            ["text"] = m.Text,
            ["baud"] = m.Baud,
            ["time"] = m.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            ["ric_description"] = m.RicDescription,
            ["ric_location"] = m.RicLocation,
            ["ric_short"] = m.RicShortName,
            ["subric_description"] = m.SubRicDescription,
            ["description"] = m.Description,
            ["rules"] = new JsonArray(item.Rules.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["matched"] = item.Rules.Count > 0,
            ["repeat"] = item.IsRepeat,
            ["forwarding"] = item.Forwarding,
        };
    }

    public static JsonObject BuildSensorState(ProcessedMessage item)
    {
        var m = item.Message;
        var state = string.IsNullOrWhiteSpace(m.Text) ? $"{m.RicFormatted}{m.SubRic}" : m.Text.Trim();
        if (state.Length > 255) state = state[..254] + "…"; // Home Assistant erlaubt höchstens 255 Zeichen
        var attributes = BuildEventData(item);
        attributes["friendly_name"] = "POCSAG letzte Meldung";
        attributes["icon"] = "mdi:pager";
        return new JsonObject { ["state"] = state, ["attributes"] = attributes };
    }

    public Task SendEventAsync(ProcessedMessage item, CancellationToken ct = default) =>
        PostAsync($"/events/{EventType}", BuildEventData(item), ct);

    public Task UpdateSensorAsync(ProcessedMessage item, CancellationToken ct = default) =>
        PostAsync($"/states/{SensorId}", BuildSensorState(item), ct);

    private async Task PostAsync(string path, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(body.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Home Assistant antwortet {(int)response.StatusCode} {response.ReasonPhrase}");
    }
}
