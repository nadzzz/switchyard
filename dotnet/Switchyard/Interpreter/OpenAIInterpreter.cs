using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Switchyard.Config;
using Switchyard.Models;

namespace Switchyard.Interpreter;

/// <summary>
/// OpenAI interpreter — uses OpenAI's Audio Transcription API and Chat Completions API.
/// </summary>
public sealed class OpenAIInterpreter : IInterpreter
{
    private const string TranscriptionUrl = "https://api.openai.com/v1/audio/transcriptions";
    private const string ChatUrl = "https://api.openai.com/v1/chat/completions";

    private readonly string _apiKey;
    private readonly string _transcriptionModel;
    private readonly string _completionModel;
    private readonly HttpClient _client;
    private readonly ILogger<OpenAIInterpreter> _logger;

    public string Name => "openai";

    public OpenAIInterpreter(OpenAIConfig cfg, HttpClient client, ILogger<OpenAIInterpreter> logger)
    {
        _apiKey = ResolveEnvRef(cfg.ApiKey);
        _transcriptionModel = cfg.TranscriptionModel;
        _completionModel = cfg.CompletionModel;
        _client = client;
        _logger = logger;
    }

    public async Task<TranscribeResult> TranscribeAsync(
        byte[] audio, string contentType, TranscribeOpts opts, CancellationToken ct = default)
    {
        var ext = InterpreterHelpers.ExtFromContentType(contentType);

        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "audio/wav");
        form.Add(audioContent, "file", $"audio{ext}");

        var model = !string.IsNullOrEmpty(opts.Model) ? opts.Model : _transcriptionModel;
        form.Add(new StringContent(model), "model");

        if (!string.IsNullOrEmpty(opts.Language))
            form.Add(new StringContent(opts.Language), "language");
        if (!string.IsNullOrEmpty(opts.Prompt))
            form.Add(new StringContent(opts.Prompt), "prompt");

        form.Add(new StringContent("verbose_json"), "response_format");

        using var req = new HttpRequestMessage(HttpMethod.Post, TranscriptionUrl) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var resp = await _client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var text = json.GetProperty("text").GetString() ?? "";
        var lang = json.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
        lang = InterpreterHelpers.NormalizeLanguage(lang);

        _logger.LogDebug("Transcription complete: {Length} chars, language={Lang}", text.Length, lang);
        return new TranscribeResult { Text = text, Language = lang };
    }

    public async Task<InterpretResult> InterpretAsync(
        string text, Instruction instruction, CancellationToken ct = default)
    {
        var systemPrompt = InterpreterHelpers.BuildSystemPrompt(instruction);

        var body = new
        {
            model = _completionModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            },
            response_format = new { type = "json_object" },
            temperature = 0.2
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ChatUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var resp = await _client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var choices = json.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("No choices returned from chat API");

        var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var (commands, responseText) = InterpreterHelpers.ParseCommands(content);

        _logger.LogDebug("Interpretation complete: {Count} commands", commands.Count);
        return new InterpretResult { Commands = commands, ResponseText = responseText };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string ResolveEnvRef(string val)
    {
        if (val.StartsWith("${") && val.EndsWith("}"))
        {
            var envKey = val[2..^1];
            var envVal = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrEmpty(envVal)) return envVal;
        }
        return val;
    }
}
