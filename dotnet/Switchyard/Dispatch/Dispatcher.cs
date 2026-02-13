using System.Diagnostics;
using System.Text.Json;
using Switchyard.Interpreter;
using Switchyard.Models;
using Switchyard.Transport;
using Switchyard.Tts;

namespace Switchyard.Dispatch;

/// <summary>
/// Central routing engine — receives messages from transports, runs them through
/// the interpreter pipeline (transcribe → interpret), then routes the resulting
/// commands to target services.
/// </summary>
public sealed class Dispatcher
{
    private readonly IInterpreter _interpreter;
    private readonly Dictionary<string, ITransport> _transports;
    private readonly ISynthesizer? _synthesizer;
    private readonly ILogger<Dispatcher> _logger;

    public Dispatcher(
        IInterpreter interpreter,
        IEnumerable<ITransport> transports,
        ISynthesizer? synthesizer,
        ILogger<Dispatcher> logger)
    {
        _interpreter = interpreter;
        _transports = transports.ToDictionary(t => t.Name);
        _synthesizer = synthesizer;
        _logger = logger;
    }

    /// <summary>The handler function passed to each transport.</summary>
    public async Task<DispatchResult> HandleAsync(DispatchMessage msg, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var respMode = ResolveResponseMode(
            ResponseModeExtensions.Parse(msg.Instruction.ResponseMode));

        _logger.LogInformation("Dispatch started: id={Id}, source={Source}, response_mode={Mode}",
            msg.Id, msg.Source, respMode.ToWireString());

        var result = new DispatchResult { MessageId = msg.Id };

        // Step 1: Transcribe audio (if present).
        string transcript;
        string detectedLang = "";

        if (msg.HasAudio)
        {
            _logger.LogDebug("Transcribing audio: {ContentType}, {Bytes} bytes",
                msg.ContentType, msg.Audio!.Length);

            try
            {
                var tRes = await _interpreter.TranscribeAsync(
                    msg.Audio!, msg.ContentType ?? "audio/wav",
                    new TranscribeOpts { Prompt = msg.Instruction.Prompt }, ct);

                transcript = tRes.Text;
                detectedLang = tRes.Language;
                result.Transcript = transcript;
                result.Language = detectedLang;
                _logger.LogInformation("Transcription complete: {Length} chars, language={Lang}",
                    transcript.Length, detectedLang);
            }
            catch (Exception ex)
            {
                result.Error = $"transcription failed: {ex.Message}";
                _logger.LogError(ex, "Transcription failed");
                return result;
            }
        }
        else if (!string.IsNullOrEmpty(msg.Text))
        {
            transcript = msg.Text!;
            result.Transcript = transcript;
            _logger.LogDebug("Using text input directly");
        }
        else
        {
            result.Error = "message has no audio and no text";
            return result;
        }

        // Step 2: Interpret transcript into commands.
        try
        {
            var interpResult = await _interpreter.InterpretAsync(transcript, msg.Instruction, ct);
            result.Commands = interpResult.Commands;
            _logger.LogInformation("Interpretation complete: {Count} commands", interpResult.Commands.Count);

            // Step 3: Populate natural-language response based on response_mode.
            if (respMode.WantText())
                result.ResponseText = interpResult.ResponseText;

            if (respMode.WantAudio() && _synthesizer is not null && !string.IsNullOrEmpty(interpResult.ResponseText))
            {
                var lang = string.IsNullOrEmpty(detectedLang) ? "en" : detectedLang;
                _logger.LogDebug("Synthesizing response: language={Lang}, text_length={Len}",
                    lang, interpResult.ResponseText.Length);

                try
                {
                    var synthResult = await _synthesizer.SynthesizeAsync(
                        interpResult.ResponseText,
                        new SynthesizeOpts { Language = lang }, ct);
                    result.SetResponseAudioBytes(synthResult.Audio);
                    result.ResponseContentType = synthResult.ContentType;
                    _logger.LogInformation("TTS synthesis complete: {Bytes} audio bytes", synthResult.Audio.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TTS synthesis failed, continuing without audio");
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = $"interpretation failed: {ex.Message}";
            _logger.LogError(ex, "Interpretation failed");
            return result;
        }

        // Step 4: Route commands to target services.
        var payload = JsonSerializer.SerializeToUtf8Bytes(result);

        if (msg.Instruction.Targets is { Count: > 0 } targets)
        {
            foreach (var target in targets)
            {
                if (!_transports.TryGetValue(target.Protocol, out var transport))
                {
                    _logger.LogWarning("No transport for protocol {Protocol}, target {Target}",
                        target.Protocol, target.ServiceName);
                    continue;
                }

                try
                {
                    await transport.SendAsync(target, payload, ct);
                    result.RoutedTo.Add(target.ServiceName);
                    _logger.LogInformation("Routed to target {Target}", target.ServiceName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send to target {Target}", target.ServiceName);
                }
            }
        }

        _logger.LogInformation("Dispatch complete: {Duration}ms, routed_to={Count}",
            sw.ElapsedMilliseconds, result.RoutedTo.Count);

        return result;
    }

    private ResponseMode ResolveResponseMode(ResponseMode mode) => mode switch
    {
        ResponseMode.None or ResponseMode.Text or ResponseMode.Audio or ResponseMode.TextAudio => mode,
        _ => _synthesizer is not null ? ResponseMode.TextAudio : ResponseMode.Text
    };
}
