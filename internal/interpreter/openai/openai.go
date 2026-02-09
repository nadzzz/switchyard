// Package openai implements the Interpreter interface using OpenAI's APIs.
//
// It uses the Audio Transcription API (Whisper / gpt-4o-transcribe) for
// speech-to-text, and the Chat Completions API for interpreting transcribed
// text into structured commands.
package openai

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"mime/multipart"
	"net/http"
	"strings"

	"github.com/nadzzz/switchyard/internal/config"
	"github.com/nadzzz/switchyard/internal/interpreter"
	"github.com/nadzzz/switchyard/internal/message"
)

const (
	transcriptionURL = "https://api.openai.com/v1/audio/transcriptions"
	chatURL          = "https://api.openai.com/v1/chat/completions"
)

// Interpreter uses OpenAI APIs for transcription and command generation.
type Interpreter struct {
	apiKey             string
	transcriptionModel string
	completionModel    string
	client             *http.Client
}

// New creates a new OpenAI interpreter from config.
func New(cfg config.OpenAIConfig) *Interpreter {
	return &Interpreter{
		apiKey:             cfg.APIKey,
		transcriptionModel: cfg.TranscriptionModel,
		completionModel:    cfg.CompletionModel,
		client:             &http.Client{},
	}
}

// Name returns the backend identifier.
func (i *Interpreter) Name() string { return "openai" }

// Transcribe sends audio to the OpenAI Transcription API.
func (i *Interpreter) Transcribe(ctx context.Context, audio []byte, contentType string, opts interpreter.TranscribeOpts) (string, error) {
	body := &bytes.Buffer{}
	writer := multipart.NewWriter(body)

	// Determine file extension from content type.
	ext := extFromContentType(contentType)
	part, err := writer.CreateFormFile("file", "audio"+ext)
	if err != nil {
		return "", fmt.Errorf("creating form file: %w", err)
	}
	if _, err := io.Copy(part, bytes.NewReader(audio)); err != nil {
		return "", fmt.Errorf("writing audio: %w", err)
	}

	model := i.transcriptionModel
	if opts.Model != "" {
		model = opts.Model
	}
	_ = writer.WriteField("model", model)

	if opts.Language != "" {
		_ = writer.WriteField("language", opts.Language)
	}
	if opts.Prompt != "" {
		_ = writer.WriteField("prompt", opts.Prompt)
	}
	_ = writer.WriteField("response_format", "json")
	writer.Close()

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, transcriptionURL, body)
	if err != nil {
		return "", fmt.Errorf("creating request: %w", err)
	}
	req.Header.Set("Authorization", "Bearer "+i.apiKey)
	req.Header.Set("Content-Type", writer.FormDataContentType())

	resp, err := i.client.Do(req)
	if err != nil {
		return "", fmt.Errorf("transcription request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		respBody, _ := io.ReadAll(io.LimitReader(resp.Body, 2048))
		return "", fmt.Errorf("transcription failed (status %d): %s", resp.StatusCode, respBody)
	}

	var result struct {
		Text string `json:"text"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return "", fmt.Errorf("decoding transcription: %w", err)
	}

	slog.Debug("transcription complete", "text_length", len(result.Text))
	return result.Text, nil
}

// Interpret sends the transcribed text + instruction to the Chat Completions API
// and returns structured commands.
func (i *Interpreter) Interpret(ctx context.Context, text string, instruction message.Instruction) ([]message.Command, error) {
	systemPrompt := buildSystemPrompt(instruction)

	reqBody := chatRequest{
		Model: i.completionModel,
		Messages: []chatMessage{
			{Role: "system", Content: systemPrompt},
			{Role: "user", Content: text},
		},
		ResponseFormat: &responseFormat{Type: "json_object"},
		Temperature:    0.2,
	}

	bodyBytes, err := json.Marshal(reqBody)
	if err != nil {
		return nil, fmt.Errorf("marshalling chat request: %w", err)
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, chatURL, bytes.NewReader(bodyBytes))
	if err != nil {
		return nil, fmt.Errorf("creating chat request: %w", err)
	}
	req.Header.Set("Authorization", "Bearer "+i.apiKey)
	req.Header.Set("Content-Type", "application/json")

	resp, err := i.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("chat request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		respBody, _ := io.ReadAll(io.LimitReader(resp.Body, 2048))
		return nil, fmt.Errorf("chat failed (status %d): %s", resp.StatusCode, respBody)
	}

	var chatResp chatResponse
	if err := json.NewDecoder(resp.Body).Decode(&chatResp); err != nil {
		return nil, fmt.Errorf("decoding chat response: %w", err)
	}

	if len(chatResp.Choices) == 0 {
		return nil, fmt.Errorf("no choices returned from chat API")
	}

	// Parse the JSON response into commands.
	content := chatResp.Choices[0].Message.Content
	commands, err := parseCommands(content)
	if err != nil {
		return nil, fmt.Errorf("parsing commands: %w", err)
	}

	slog.Debug("interpretation complete", "commands", len(commands))
	return commands, nil
}

// Close is a no-op for the OpenAI interpreter.
func (i *Interpreter) Close() error { return nil }

// --- Internal types and helpers ---

type chatRequest struct {
	Model          string          `json:"model"`
	Messages       []chatMessage   `json:"messages"`
	ResponseFormat *responseFormat  `json:"response_format,omitempty"`
	Temperature    float64         `json:"temperature"`
}

type chatMessage struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

type responseFormat struct {
	Type string `json:"type"`
}

type chatResponse struct {
	Choices []struct {
		Message struct {
			Content string `json:"content"`
		} `json:"message"`
	} `json:"choices"`
}

func buildSystemPrompt(instr message.Instruction) string {
	var sb strings.Builder
	sb.WriteString("You are a voice command interpreter for a home automation and robotics system.\n")
	sb.WriteString("Interpret the user's transcribed speech and return structured commands as JSON.\n\n")

	if instr.ResponseFormat != "" {
		sb.WriteString("Output format: " + instr.ResponseFormat + "\n")
	}
	if instr.Prompt != "" {
		sb.WriteString("Additional context: " + instr.Prompt + "\n")
	}

	sb.WriteString("\nReturn a JSON object with a \"commands\" array. Each command has:\n")
	sb.WriteString("- \"action\": the command verb (e.g., \"turn_on\", \"move_to\")\n")
	sb.WriteString("- \"params\": an object with action-specific parameters\n")
	sb.WriteString("\nExample: {\"commands\": [{\"action\": \"turn_on\", \"params\": {\"entity\": \"light.living_room\"}}]}\n")

	return sb.String()
}

func parseCommands(content string) ([]message.Command, error) {
	// Try parsing as {"commands": [...]}
	var wrapper struct {
		Commands []message.Command `json:"commands"`
	}
	if err := json.Unmarshal([]byte(content), &wrapper); err == nil && len(wrapper.Commands) > 0 {
		// Preserve the raw JSON for each command.
		for idx := range wrapper.Commands {
			raw, _ := json.Marshal(wrapper.Commands[idx])
			wrapper.Commands[idx].Raw = raw
		}
		return wrapper.Commands, nil
	}

	// Try parsing as a single command.
	var single message.Command
	if err := json.Unmarshal([]byte(content), &single); err == nil && single.Action != "" {
		raw, _ := json.Marshal(single)
		single.Raw = raw
		return []message.Command{single}, nil
	}

	return nil, fmt.Errorf("could not parse LLM response as commands: %.200s", content)
}

func extFromContentType(ct string) string {
	switch {
	case strings.Contains(ct, "wav"):
		return ".wav"
	case strings.Contains(ct, "ogg"):
		return ".ogg"
	case strings.Contains(ct, "mp3"), strings.Contains(ct, "mpeg"):
		return ".mp3"
	case strings.Contains(ct, "flac"):
		return ".flac"
	case strings.Contains(ct, "webm"):
		return ".webm"
	case strings.Contains(ct, "m4a"):
		return ".m4a"
	default:
		return ".wav"
	}
}
