namespace Switchyard.Interpreter;

/// <summary>Options controlling transcription behavior.</summary>
public sealed class TranscribeOpts
{
    public string? Language { get; set; }
    public string? Prompt { get; set; }
    public string? Model { get; set; }
}

/// <summary>Output of audio transcription.</summary>
public sealed class TranscribeResult
{
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}

/// <summary>Output of command interpretation.</summary>
public sealed class InterpretResult
{
    public List<Models.Command> Commands { get; set; } = [];
    public string ResponseText { get; set; } = string.Empty;
}

/// <summary>
/// Interface for LLM-based audio interpretation.
/// Implementations: OpenAI (cloud) and Local (self-hosted via Ollama/whisper.cpp).
/// </summary>
public interface IInterpreter : IAsyncDisposable
{
    string Name { get; }

    Task<TranscribeResult> TranscribeAsync(
        byte[] audio, string contentType, TranscribeOpts opts,
        CancellationToken ct = default);

    Task<InterpretResult> InterpretAsync(
        string text, Models.Instruction instruction,
        CancellationToken ct = default);
}
