using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switchyard.Models;

/// <summary>Response mode controls what natural-language output the caller wants.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResponseMode>))]
public enum ResponseMode
{
    None,
    Text,
    Audio,
    TextAudio
}

/// <summary>Helper to parse response_mode strings like "text+audio".</summary>
public static class ResponseModeExtensions
{
    public static ResponseMode Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "none" => ResponseMode.None,
        "text" => ResponseMode.Text,
        "audio" => ResponseMode.Audio,
        "text+audio" or "textaudio" => ResponseMode.TextAudio,
        _ => ResponseMode.Text
    };

    public static string ToWireString(this ResponseMode mode) => mode switch
    {
        ResponseMode.None => "none",
        ResponseMode.Text => "text",
        ResponseMode.Audio => "audio",
        ResponseMode.TextAudio => "text+audio",
        _ => "text"
    };

    public static bool WantText(this ResponseMode mode) =>
        mode is ResponseMode.Text or ResponseMode.TextAudio;

    public static bool WantAudio(this ResponseMode mode) =>
        mode is ResponseMode.Audio or ResponseMode.TextAudio;
}

/// <summary>Incoming message from any transport.</summary>
public sealed class DispatchMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? Audio { get; set; }

    [JsonPropertyName("content_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentType { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("instruction")]
    public Instruction Instruction { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool HasAudio => Audio is { Length: > 0 };
}

/// <summary>Describes how to process and route a message.</summary>
public sealed class Instruction
{
    [JsonPropertyName("targets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MessageTarget>? Targets { get; set; }

    [JsonPropertyName("command_format")]
    public string CommandFormat { get; set; } = string.Empty;

    [JsonPropertyName("response_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseMode { get; set; }

    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Prompt { get; set; }
}

/// <summary>A downstream service target.</summary>
public sealed class MessageTarget
{
    [JsonPropertyName("service_name")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("format_template")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FormatTemplate { get; set; }
}

/// <summary>A single structured command produced by the interpreter.</summary>
public sealed class Command
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Params { get; set; }

    [JsonPropertyName("raw")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Raw { get; set; }
}

/// <summary>Outcome of processing a message through the pipeline.</summary>
public sealed class DispatchResult
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("transcript")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Transcript { get; set; }

    [JsonPropertyName("language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    [JsonPropertyName("commands")]
    public List<Command> Commands { get; set; } = [];

    [JsonPropertyName("routed_to")]
    public List<string> RoutedTo { get; set; } = [];

    [JsonPropertyName("response_text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseText { get; set; }

    [JsonPropertyName("response_audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseAudio { get; set; }

    [JsonPropertyName("response_content_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseContentType { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>Base64-encodes raw audio bytes into ResponseAudio.</summary>
    public void SetResponseAudioBytes(byte[] audio)
    {
        if (audio is { Length: > 0 })
        {
            ResponseAudio = Convert.ToBase64String(audio);
        }
    }
}

/// <summary>
/// Source-generated JSON serializer context for AOT/trimming compatibility.
/// Use <c>SwitchyardJsonContext.Default</c> wherever serialization is needed.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(DispatchMessage))]
[JsonSerializable(typeof(DispatchResult))]
[JsonSerializable(typeof(Instruction))]
[JsonSerializable(typeof(Command))]
[JsonSerializable(typeof(MessageTarget))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(OpenAIChatRequest))]
[JsonSerializable(typeof(OpenAITranscriptionForm))]
[JsonSerializable(typeof(OllamaGenerateRequest))]
public sealed partial class SwitchyardJsonContext : JsonSerializerContext;

// --- Interpreter request DTOs for source-generated serialization ---

/// <summary>OpenAI Chat Completions API request body.</summary>
public sealed class OpenAIChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponseFormatSpec? ResponseFormat { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.2;

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; set; }
}

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class ResponseFormatSpec
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "json_object";
}

/// <summary>Ollama /api/generate request body.</summary>
public sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("system")]
    public string System { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = "json";
}

/// <summary>Placeholder to register the type — actual form data is built via MultipartFormDataContent.</summary>
public sealed class OpenAITranscriptionForm
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("response_format")]
    public string ResponseFormat { get; set; } = "verbose_json";
}
