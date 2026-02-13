namespace Switchyard.TestClient.Models;

/// <summary>
/// Mirrors switchyard message.Instruction (Go struct).
/// </summary>
public sealed class InstructionDto
{
    public List<TargetDto>? Targets { get; set; }
    public string? CommandFormat { get; set; }
    public string? Prompt { get; set; }
    public string? ResponseMode { get; set; }
}

/// <summary>
/// Mirrors switchyard message.Target.
/// </summary>
public sealed class TargetDto
{
    public string? ServiceName { get; set; }
    public string? Endpoint { get; set; }
    public string? Protocol { get; set; }
    public string? FormatTemplate { get; set; }
}

/// <summary>
/// JSON body for POST /dispatch when sending structured JSON.
/// Mirrors switchyard message.Message.
/// </summary>
public sealed class DispatchRequestDto
{
    public string? Id { get; set; }
    public string? Source { get; set; }
    public string? Audio { get; set; }        // base64
    public string? ContentType { get; set; }
    public string? Text { get; set; }
    public InstructionDto? Instruction { get; set; }
}

/// <summary>
/// Mirrors switchyard message.Command.
/// </summary>
public sealed class CommandDto
{
    public string? Action { get; set; }
    public object? Params { get; set; }
    public string? Raw { get; set; }
}

/// <summary>
/// Mirrors switchyard message.DispatchResult.
/// </summary>
public sealed class DispatchResultDto
{
    public string? MessageId { get; set; }
    public string? Transcript { get; set; }
    public string? Language { get; set; }
    public List<CommandDto>? Commands { get; set; }
    public List<string>? RoutedTo { get; set; }
    public string? ResponseText { get; set; }
    public string? ResponseAudio { get; set; }       // base64
    public string? ResponseContentType { get; set; }
    public string? Error { get; set; }
}
