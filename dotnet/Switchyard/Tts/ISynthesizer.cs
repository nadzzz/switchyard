namespace Switchyard.Tts;

/// <summary>Options controlling TTS synthesis behavior.</summary>
public sealed class SynthesizeOpts
{
    public string Language { get; set; } = "en";
    public string? Voice { get; set; }
}

/// <summary>Result of TTS synthesis.</summary>
public sealed class SynthesizeResult
{
    public byte[] Audio { get; set; } = [];
    public string ContentType { get; set; } = "audio/wav";
    public int SampleRate { get; set; } = 22050;
    public int Channels { get; set; } = 1;
}

/// <summary>Interface for text-to-speech synthesis.</summary>
public interface ISynthesizer : IAsyncDisposable
{
    Task<SynthesizeResult> SynthesizeAsync(string text, SynthesizeOpts opts, CancellationToken ct = default);
}
