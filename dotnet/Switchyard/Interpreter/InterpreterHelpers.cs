using System.Text.Json;
using Switchyard.Config;
using Switchyard.Models;

namespace Switchyard.Interpreter;

/// <summary>Shared helpers for building system prompts and parsing LLM responses.</summary>
internal static class InterpreterHelpers
{
    public static string BuildSystemPrompt(Instruction instruction)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a voice command interpreter for a home automation and robotics system.");
        sb.AppendLine("Interpret the user's transcribed speech and return structured commands as JSON.");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(instruction.CommandFormat))
            sb.AppendLine($"Output format: {instruction.CommandFormat}");

        if (!string.IsNullOrEmpty(instruction.Prompt))
            sb.AppendLine($"Additional context: {instruction.Prompt}");

        sb.AppendLine();
        sb.AppendLine("Return a JSON object with:");
        sb.AppendLine("- \"commands\": array of commands, each with \"action\" and \"params\"");
        sb.AppendLine("- \"response\": a short confirmation sentence in the SAME language the user spoke");
        sb.AppendLine();
        sb.AppendLine("Example: {\"commands\": [{\"action\": \"turn_on\", \"params\": {\"entity\": \"light.living_room\"}}], \"response\": \"Turning on the living room light\"}");

        return sb.ToString();
    }

    public static (List<Command> Commands, string ResponseText) ParseCommands(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Try {"commands": [...], "response": "..."}
            if (root.TryGetProperty("commands", out var cmdsEl) && cmdsEl.ValueKind == JsonValueKind.Array)
            {
                var commands = new List<Command>();
                foreach (var cmdEl in cmdsEl.EnumerateArray())
                {
                    var cmd = new Command
                    {
                        Action = cmdEl.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "",
                        Raw = cmdEl.Clone()
                    };

                    if (cmdEl.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
                    {
                        cmd.Params = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(p.GetRawText());
                    }

                    commands.Add(cmd);
                }

                var response = root.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
                return (commands, response);
            }

            // Try single command {"action": "...", "params": {...}}
            if (root.TryGetProperty("action", out var actionEl))
            {
                var cmd = new Command
                {
                    Action = actionEl.GetString() ?? "",
                    Raw = root.Clone()
                };

                if (root.TryGetProperty("params", out var pp) && pp.ValueKind == JsonValueKind.Object)
                {
                    cmd.Params = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(pp.GetRawText());
                }

                return ([cmd], "");
            }
        }
        catch (JsonException)
        {
            // Fall through to error
        }

        throw new InvalidOperationException($"Could not parse LLM response as commands: {content[..Math.Min(200, content.Length)]}");
    }

    public static string ExtFromContentType(string? ct) => ct switch
    {
        _ when ct?.Contains("wav") == true => ".wav",
        _ when ct?.Contains("ogg") == true => ".ogg",
        _ when ct?.Contains("mp3") == true || ct?.Contains("mpeg") == true => ".mp3",
        _ when ct?.Contains("flac") == true => ".flac",
        _ when ct?.Contains("webm") == true => ".webm",
        _ when ct?.Contains("m4a") == true => ".m4a",
        _ => ".wav"
    };

    /// <summary>
    /// Normalizes full language names (as returned by OpenAI: "english") to ISO-639-1 codes.
    /// </summary>
    public static string NormalizeLanguage(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return lang;
        if (lang.Length == 2) return lang.ToLowerInvariant();

        return lang.ToLowerInvariant() switch
        {
            "english" => "en",
            "french" => "fr",
            "spanish" => "es",
            "german" => "de",
            "italian" => "it",
            "portuguese" => "pt",
            "dutch" => "nl",
            "polish" => "pl",
            "russian" => "ru",
            "japanese" => "ja",
            "korean" => "ko",
            "chinese" => "zh",
            "arabic" => "ar",
            "hindi" => "hi",
            "turkish" => "tr",
            _ => lang.ToLowerInvariant()
        };
    }
}
