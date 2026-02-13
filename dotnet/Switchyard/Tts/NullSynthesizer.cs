namespace Switchyard.Tts;

/// <summary>No-op synthesizer used when TTS is disabled. Avoids null checks throughout the codebase.</summary>
public sealed class NullSynthesizer : ISynthesizer
{
    public static readonly NullSynthesizer Instance = new();

    public Task<SynthesizeResult> SynthesizeAsync(string text, SynthesizeOpts opts, CancellationToken ct = default)
        => Task.FromResult(new SynthesizeResult());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
