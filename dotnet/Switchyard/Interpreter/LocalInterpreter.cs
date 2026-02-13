using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using Switchyard.Config;
using Switchyard.Models;

namespace Switchyard.Interpreter;

/// <summary>
/// Local interpreter — uses self-hosted Whisper-compatible transcription endpoint
/// and OpenAI-compatible chat endpoint (Ollama, vLLM, llama.cpp).
/// </summary>
public sealed class LocalInterpreter : IInterpreter
{
    private readonly string _whisperEndpoint;
    private readonly string _whisperType; // "openai" | "asr"
    private readonly string _llmEndpoint;
    private readonly string _llmModel;
    private readonly bool _vadFilter;
    private readonly string _defaultLanguage;
    private readonly HttpClient _client;
    private readonly ILogger<LocalInterpreter> _logger;

    public string Name => "local";

    public LocalInterpreter(LocalConfig cfg, HttpClient client, ILogger<LocalInterpreter> logger)
    {
        _whisperEndpoint = cfg.WhisperEndpoint;
        _whisperType = string.IsNullOrEmpty(cfg.WhisperType) ? "openai" : cfg.WhisperType;
        _llmEndpoint = cfg.LlmEndpoint;
        _llmModel = string.IsNullOrEmpty(cfg.LlmModel) ? "llama3" : cfg.LlmModel;
        _vadFilter = cfg.VadFilter;
        _defaultLanguage = cfg.Language;
        _client = client;
        _logger = logger;
    }

    public Task<TranscribeResult> TranscribeAsync(
        byte[] audio, string contentType, TranscribeOpts opts, CancellationToken ct = default)
    {
        return _whisperType == "asr"
            ? TranscribeAsrAsync(audio, contentType, opts, ct)
            : TranscribeOpenAIAsync(audio, contentType, opts, ct);
    }

    /// <summary>ahmetoner/whisper-asr-webservice: POST /asr?task=transcribe&amp;...</summary>
    private async Task<TranscribeResult> TranscribeAsrAsync(
        byte[] audio, string contentType, TranscribeOpts opts, CancellationToken ct)
    {
        var ext = InterpreterHelpers.ExtFromContentType(contentType);

        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "audio/wav");
        form.Add(audioContent, "audio_file", $"audio{ext}");

        var qb = HttpUtility.ParseQueryString(string.Empty);
        qb["task"] = "transcribe";
        qb["output"] = "verbose_json";
        qb["encode"] = "true";

        var lang = opts.Language ?? _defaultLanguage;
        if (!string.IsNullOrEmpty(lang)) qb["language"] = lang;
        if (!string.IsNullOrEmpty(opts.Prompt)) qb["initial_prompt"] = opts.Prompt;
        if (_vadFilter) qb["vad_filter"] = "true";

        var url = $"{_whisperEndpoint}?{qb}";
        _logger.LogDebug("whisper-asr request: {Url}", url);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        var resp = await _client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var text = json.GetProperty("text").GetString() ?? "";
        var language = json.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";

        _logger.LogDebug("ASR transcription complete: {Length} chars, language={Lang}", text.Length, language);
        return new TranscribeResult { Text = text, Language = language };
    }

    /// <summary>OpenAI-compatible whisper (whisper.cpp, faster-whisper).</summary>
    private async Task<TranscribeResult> TranscribeOpenAIAsync(
        byte[] audio, string contentType, TranscribeOpts opts, CancellationToken ct)
    {
        var ext = InterpreterHelpers.ExtFromContentType(contentType);

        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "audio/wav");
        form.Add(audioContent, "file", $"audio{ext}");

        if (!string.IsNullOrEmpty(opts.Model))
            form.Add(new StringContent(opts.Model), "model");

        var lang = opts.Language ?? _defaultLanguage;
        if (!string.IsNullOrEmpty(lang))
            form.Add(new StringContent(lang), "language");

        form.Add(new StringContent("verbose_json"), "response_format");

        using var req = new HttpRequestMessage(HttpMethod.Post, _whisperEndpoint) { Content = form };
        var resp = await _client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var text = json.GetProperty("text").GetString() ?? "";
        var language = json.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";

        _logger.LogDebug("Local transcription complete: {Length} chars, language={Lang}", text.Length, language);
        return new TranscribeResult { Text = text, Language = language };
    }

    public async Task<InterpretResult> InterpretAsync(
        string text, Instruction instruction, CancellationToken ct = default)
    {
        var systemPrompt = InterpreterHelpers.BuildSystemPrompt(instruction);

        object body;
        if (_llmEndpoint.EndsWith("/api/generate"))
        {
            // Ollama format
            body = new
            {
                model = _llmModel,
                system = systemPrompt,
                prompt = text,
                stream = false,
                format = "json"
            };
        }
        else
        {
            // OpenAI-compatible chat completions
            body = new
            {
                model = _llmModel,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = text }
                },
                temperature = 0.2,
                stream = false
            };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, _llmEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        var resp = await _client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var respData = await resp.Content.ReadAsStringAsync(ct);
        var content = ExtractContent(respData);
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException("Empty response from local LLM");

        var (commands, responseText) = InterpreterHelpers.ParseCommands(content);

        _logger.LogDebug("Local interpretation complete: {Count} commands", commands.Count);
        return new InterpretResult { Commands = commands, ResponseText = responseText };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string ExtractContent(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // OpenAI-compatible: {"choices": [{"message": {"content": "..."}}]}
            if (root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c))
            {
                return c.GetString() ?? "";
            }

            // Ollama: {"response": "..."}
            if (root.TryGetProperty("response", out var r))
            {
                return r.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // Fall through
        }

        return data;
    }
}
