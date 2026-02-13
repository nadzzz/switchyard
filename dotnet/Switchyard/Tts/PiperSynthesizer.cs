using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Switchyard.Config;

namespace Switchyard.Tts;

/// <summary>
/// Piper TTS synthesizer using the Wyoming protocol over TCP.
/// Mirrors the Go piper package.
/// </summary>
public sealed class PiperSynthesizer : ISynthesizer
{
    private static readonly Dictionary<string, string> DefaultVoices = new()
    {
        ["en"] = "en_US-lessac-medium",
        ["fr"] = "fr_FR-siwis-medium",
        ["es"] = "es_ES-mls_10246-low",
        ["de"] = "de_DE-thorsten-medium",
        ["it"] = "it_IT-riccardo-x_low",
        ["pt"] = "pt_BR-faber-medium",
        ["nl"] = "nl_NL-mls-medium",
        ["pl"] = "pl_PL-darkman-medium",
        ["ru"] = "ru_RU-ruslan-medium",
        ["ja"] = "ja_JP-amitaro-medium",
        ["ko"] = "ko_KR-kss-x_low",
        ["zh"] = "zh_CN-huayan-medium",
    };

    private readonly string _endpoint;
    private readonly Dictionary<string, string> _endpoints;
    private readonly Dictionary<string, string> _voices;
    private readonly ILogger<PiperSynthesizer> _logger;

    public PiperSynthesizer(PiperConfig cfg, ILogger<PiperSynthesizer> logger)
    {
        _endpoint = CleanEndpoint(cfg.Endpoint);
        _endpoints = cfg.Endpoints.ToDictionary(
            kv => kv.Key, kv => CleanEndpoint(kv.Value));
        _voices = new Dictionary<string, string>(DefaultVoices);
        foreach (var (k, v) in cfg.Voices) _voices[k] = v;
        _logger = logger;
    }

    public async Task<SynthesizeResult> SynthesizeAsync(string text, SynthesizeOpts opts, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Empty text for synthesis", nameof(text));

        // Select voice
        var voice = opts.Voice;
        if (string.IsNullOrEmpty(voice))
            _voices.TryGetValue(opts.Language, out voice);
        if (string.IsNullOrEmpty(voice))
            voice = _voices.GetValueOrDefault("en", "en_US-lessac-medium");

        // Select endpoint
        var endpoint = _endpoints.GetValueOrDefault(opts.Language, _endpoint);
        if (string.IsNullOrEmpty(endpoint))
            throw new InvalidOperationException($"No piper endpoint configured for language '{opts.Language}'");

        _logger.LogDebug("Piper synthesize: {Length} chars, voice={Voice}, lang={Lang}, endpoint={Ep}",
            text.Length, voice, opts.Language, endpoint);

        // Parse host:port
        var parts = endpoint.Split(':', 2);
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 10200;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);
        await using var stream = tcp.GetStream();

        // Send synthesize event
        var synthEvent = new WyomingEvent
        {
            Type = "synthesize",
            Data = new Dictionary<string, object>
            {
                ["text"] = text,
                ["voice"] = new Dictionary<string, object> { ["name"] = voice }
            }
        };
        await WriteEventAsync(stream, synthEvent, ct);

        // Read events: audio-start → audio-chunk* → audio-stop
        using var pcmBuffer = new MemoryStream();
        int sampleRate = 22050, channels = 1, width = 2;

        while (true)
        {
            var (evt, payload) = await ReadEventAsync(stream, ct);
            switch (evt.Type)
            {
                case "audio-start":
                    if (evt.Data.TryGetValue("rate", out var rateObj) && rateObj is JsonElement rateEl)
                        sampleRate = rateEl.TryGetInt32(out var sr) ? sr : (int)rateEl.GetDouble();
                    if (evt.Data.TryGetValue("channels", out var chObj) && chObj is JsonElement chEl)
                        channels = chEl.TryGetInt32(out var ch) ? ch : (int)chEl.GetDouble();
                    if (evt.Data.TryGetValue("width", out var wObj) && wObj is JsonElement wEl)
                        width = wEl.TryGetInt32(out var w) ? w : (int)wEl.GetDouble();
                    _logger.LogDebug("Piper audio-start: rate={Rate}, channels={Ch}, width={W}", sampleRate, channels, width);
                    break;

                case "audio-chunk":
                    if (payload.Length > 0)
                        await pcmBuffer.WriteAsync(payload, ct);
                    break;

                case "audio-stop":
                    _logger.LogDebug("Piper audio-stop: {Bytes} PCM bytes", pcmBuffer.Length);
                    var wav = PcmToWav(pcmBuffer.ToArray(), sampleRate, channels, width);
                    return new SynthesizeResult
                    {
                        Audio = wav,
                        ContentType = "audio/wav",
                        SampleRate = sampleRate,
                        Channels = channels
                    };

                case "error":
                    var errMsg = evt.Data.TryGetValue("text", out var errObj) && errObj is JsonElement errEl
                        ? errEl.GetString() ?? "unknown error"
                        : "unknown error";
                    throw new InvalidOperationException($"Piper error: {errMsg}");
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- Wyoming protocol helpers ---

    private sealed class WyomingEvent
    {
        public string Type { get; set; } = "";
        public Dictionary<string, object> Data { get; set; } = new();
    }

    private sealed class JsonObjectConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonElement.ParseValue(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    private static async Task WriteEventAsync(NetworkStream stream, WyomingEvent evt, CancellationToken ct)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(new { type = evt.Type, data = evt.Data });
        var header = $"{jsonBytes.Length} 0\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        await stream.WriteAsync(headerBytes, ct);
        await stream.WriteAsync(jsonBytes, ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<(WyomingEvent Event, byte[] Payload)> ReadEventAsync(NetworkStream stream, CancellationToken ct)
    {
        // Read header line
        var headerLine = await ReadLineAsync(stream, ct);
        var headerParts = headerLine.Split(' ', 2);
        if (headerParts.Length != 2)
            throw new InvalidOperationException($"Invalid Wyoming header: '{headerLine}'");

        var jsonLen = int.Parse(headerParts[0].Trim());
        var payloadLen = int.Parse(headerParts[1].Trim());

        // Read JSON + trailing newline
        var jsonBuf = new byte[jsonLen + 1];
        await ReadExactAsync(stream, jsonBuf, ct);

        var jsonStr = Encoding.UTF8.GetString(jsonBuf, 0, jsonLen);
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        var evt = new WyomingEvent
        {
            Type = root.GetProperty("type").GetString() ?? ""
        };
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
        {
            evt.Data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataEl.GetRawText(),
                new JsonSerializerOptions { Converters = { new JsonObjectConverter() } })
                ?? new();
        }

        // Read payload
        byte[] payload = [];
        if (payloadLen > 0)
        {
            payload = new byte[payloadLen];
            await ReadExactAsync(stream, payload, ct);
        }

        return (evt, payload);
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0) throw new EndOfStreamException("Unexpected end of Wyoming stream");
            if (buf[0] == (byte)'\n') break;
            sb.Append((char)buf[0]);
        }
        return sb.ToString();
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (n == 0) throw new EndOfStreamException("Unexpected end of Wyoming stream");
            offset += n;
        }
    }

    private static string CleanEndpoint(string ep) =>
        ep.Replace("tcp://", "").Replace("http://", "");

    /// <summary>Wraps raw PCM data in a WAV container.</summary>
    private static byte[] PcmToWav(byte[] pcm, int sampleRate, int channels, int bytesPerSample)
    {
        var dataLen = pcm.Length;
        var fileLen = 36 + dataLen;

        using var ms = new MemoryStream(44 + dataLen);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write((uint)fileLen);
        bw.Write("WAVE"u8);

        // fmt subchunk
        bw.Write("fmt "u8);
        bw.Write(16u);                              // subchunk1 size
        bw.Write((ushort)1);                         // audio format (PCM)
        bw.Write((ushort)channels);
        bw.Write((uint)sampleRate);
        bw.Write((uint)(sampleRate * channels * bytesPerSample)); // byte rate
        bw.Write((ushort)(channels * bytesPerSample));            // block align
        bw.Write((ushort)(bytesPerSample * 8));                   // bits per sample

        // data subchunk
        bw.Write("data"u8);
        bw.Write((uint)dataLen);
        bw.Write(pcm);

        return ms.ToArray();
    }
}
