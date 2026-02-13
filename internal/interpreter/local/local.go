// Package local implements the Interpreter interface using self-hosted models.
//
// It supports any Whisper-compatible transcription endpoint (e.g., whisper.cpp
// server, faster-whisper) and any OpenAI-compatible chat endpoint (e.g., Ollama,
// vLLM, llama.cpp server).
package local

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"mime/multipart"
	"net/http"
	"net/url"
	"strings"

	"github.com/nadzzz/switchyard/internal/config"
	"github.com/nadzzz/switchyard/internal/interpreter"
	"github.com/nadzzz/switchyard/internal/message"
)

// Interpreter uses self-hosted models for transcription and command generation.
type Interpreter struct {
	whisperEndpoint string
	whisperType     string // "openai" or "asr"
	llmEndpoint     string
	llmModel        string
	vadFilter       bool
	defaultLanguage string
	client          *http.Client
}

// New creates a new local interpreter from config.
func New(cfg config.LocalConfig) *Interpreter {
	wt := cfg.WhisperType
	if wt == "" {
		wt = "openai"
	}
	model := cfg.LLMModel
	if model == "" {
		model = "llama3"
	}
	return &Interpreter{
		whisperEndpoint: cfg.WhisperEndpoint,
		whisperType:     wt,
		llmEndpoint:     cfg.LLMEndpoint,
		llmModel:        model,
		vadFilter:       cfg.VADFilter,
		defaultLanguage: cfg.Language,
		client:          &http.Client{},
	}
}

// Name returns the backend identifier.
func (i *Interpreter) Name() string { return "local" }

// Transcribe sends audio to the local Whisper-compatible endpoint.
// Supports two flavors:
//   - "openai": OpenAI-compatible API (whisper.cpp server, faster-whisper)
//   - "asr":    ahmetoner/whisper-asr-webservice (POST /asr with query params)
func (i *Interpreter) Transcribe(ctx context.Context, audio []byte, contentType string, opts interpreter.TranscribeOpts) (*interpreter.TranscribeResult, error) {
	switch i.whisperType {
	case "asr":
		return i.transcribeASR(ctx, audio, contentType, opts)
	default:
		return i.transcribeOpenAI(ctx, audio, contentType, opts)
	}
}

// transcribeASR handles the ahmetoner/whisper-asr-webservice format.
// API: POST /asr?task=transcribe&language=en&output=json&vad_filter=true
// Body: multipart/form-data with field "audio_file"
func (i *Interpreter) transcribeASR(ctx context.Context, audio []byte, contentType string, opts interpreter.TranscribeOpts) (*interpreter.TranscribeResult, error) {
	body := &bytes.Buffer{}
	writer := multipart.NewWriter(body)

	ext := extFromContentType(contentType)
	part, err := writer.CreateFormFile("audio_file", "audio"+ext)
	if err != nil {
		return nil, fmt.Errorf("creating form file: %w", err)
	}
	if _, err := io.Copy(part, bytes.NewReader(audio)); err != nil {
		return nil, fmt.Errorf("writing audio: %w", err)
	}
	writer.Close()

	// Build URL with query parameters.
	endpoint := i.whisperEndpoint
	q := make(url.Values)
	q.Set("task", "transcribe")
	q.Set("output", "verbose_json")
	q.Set("encode", "true")

	lang := opts.Language
	if lang == "" {
		lang = i.defaultLanguage
	}
	if lang != "" {
		q.Set("language", lang)
	}
	if opts.Prompt != "" {
		q.Set("initial_prompt", opts.Prompt)
	}
	if i.vadFilter {
		q.Set("vad_filter", "true")
	}

	reqURL := endpoint + "?" + q.Encode()
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, reqURL, body)
	if err != nil {
		return nil, fmt.Errorf("creating request: %w", err)
	}
	req.Header.Set("Content-Type", writer.FormDataContentType())

	slog.Debug("whisper-asr request", "url", reqURL)

	resp, err := i.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("asr transcription request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		respBody, _ := io.ReadAll(io.LimitReader(resp.Body, 2048))
		return nil, fmt.Errorf("asr transcription failed (status %d): %s", resp.StatusCode, respBody)
	}

	// The ASR service returns {"text": "...", "language": "..."} when output=verbose_json.
	var result struct {
		Text     string `json:"text"`
		Language string `json:"language"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, fmt.Errorf("decoding asr response: %w", err)
	}

	slog.Debug("asr transcription complete", "text_length", len(result.Text), "language", result.Language)
	return &interpreter.TranscribeResult{
		Text:     result.Text,
		Language: result.Language,
	}, nil
}

// transcribeOpenAI handles OpenAI-compatible whisper endpoints.
func (i *Interpreter) transcribeOpenAI(ctx context.Context, audio []byte, contentType string, opts interpreter.TranscribeOpts) (*interpreter.TranscribeResult, error) {
	body := &bytes.Buffer{}
	writer := multipart.NewWriter(body)

	ext := extFromContentType(contentType)
	part, err := writer.CreateFormFile("file", "audio"+ext)
	if err != nil {
		return nil, fmt.Errorf("creating form file: %w", err)
	}
	if _, err := io.Copy(part, bytes.NewReader(audio)); err != nil {
		return nil, fmt.Errorf("writing audio: %w", err)
	}

	if opts.Model != "" {
		_ = writer.WriteField("model", opts.Model)
	}
	lang := opts.Language
	if lang == "" {
		lang = i.defaultLanguage
	}
	if lang != "" {
		_ = writer.WriteField("language", lang)
	}
	_ = writer.WriteField("response_format", "verbose_json")
	writer.Close()

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, i.whisperEndpoint, body)
	if err != nil {
		return nil, fmt.Errorf("creating request: %w", err)
	}
	req.Header.Set("Content-Type", writer.FormDataContentType())

	resp, err := i.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("local transcription request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		respBody, _ := io.ReadAll(io.LimitReader(resp.Body, 2048))
		return nil, fmt.Errorf("local transcription failed (status %d): %s", resp.StatusCode, respBody)
	}

	var result struct {
		Text     string `json:"text"`
		Language string `json:"language"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, fmt.Errorf("decoding transcription: %w", err)
	}

	slog.Debug("local transcription complete", "text_length", len(result.Text), "language", result.Language)
	return &interpreter.TranscribeResult{
		Text:     result.Text,
		Language: result.Language,
	}, nil
}

// Interpret sends the transcribed text to the local LLM endpoint.
// Supports Ollama's /api/generate and OpenAI-compatible /v1/chat/completions.
func (i *Interpreter) Interpret(ctx context.Context, text string, instruction message.Instruction) (*interpreter.InterpretResult, error) {
	systemPrompt := buildSystemPrompt(instruction)

	// Try OpenAI-compatible chat completions format first (works with Ollama, vLLM, llama.cpp).
	reqBody := map[string]any{
		"model": i.llmModel,
		"messages": []map[string]string{
			{"role": "system", "content": systemPrompt},
			{"role": "user", "content": text},
		},
		"temperature": 0.2,
		"stream":      false,
	}

	bodyBytes, err := json.Marshal(reqBody)
	if err != nil {
		return nil, fmt.Errorf("marshalling request: %w", err)
	}

	// Determine endpoint — if it ends with /api/generate, use Ollama format.
	endpoint := i.llmEndpoint
	if strings.HasSuffix(endpoint, "/api/generate") {
		reqBody = map[string]any{
			"model":  i.llmModel,
			"system": systemPrompt,
			"prompt": text,
			"stream": false,
			"format": "json",
		}
		bodyBytes, _ = json.Marshal(reqBody)
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, endpoint, bytes.NewReader(bodyBytes))
	if err != nil {
		return nil, fmt.Errorf("creating request: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")

	resp, err := i.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("local LLM request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		respBody, _ := io.ReadAll(io.LimitReader(resp.Body, 2048))
		return nil, fmt.Errorf("local LLM failed (status %d): %s", resp.StatusCode, respBody)
	}

	respData, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("reading LLM response: %w", err)
	}

	// Extract the content from the response.
	content := extractContent(respData)
	if content == "" {
		return nil, fmt.Errorf("empty response from local LLM")
	}

	commands, responseText, err := parseCommands(content)
	if err != nil {
		return nil, fmt.Errorf("parsing commands: %w", err)
	}

	slog.Debug("local interpretation complete", "commands", len(commands), "has_response", responseText != "")
	return &interpreter.InterpretResult{
		Commands:     commands,
		ResponseText: responseText,
	}, nil
}

// Close is a no-op for the local interpreter.
func (i *Interpreter) Close() error { return nil }

// --- Internal helpers ---

func extractContent(data []byte) string {
	// Try OpenAI-compatible format: {"choices": [{"message": {"content": "..."}}]}
	var chatResp struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
	}
	if err := json.Unmarshal(data, &chatResp); err == nil && len(chatResp.Choices) > 0 {
		return chatResp.Choices[0].Message.Content
	}

	// Try Ollama format: {"response": "..."}
	var ollamaResp struct {
		Response string `json:"response"`
	}
	if err := json.Unmarshal(data, &ollamaResp); err == nil && ollamaResp.Response != "" {
		return ollamaResp.Response
	}

	return string(data)
}

func buildSystemPrompt(instr message.Instruction) string {
	var sb strings.Builder
	sb.WriteString("You are a voice command interpreter. ")
	sb.WriteString("Return structured commands as JSON.\n\n")

	if instr.CommandFormat != "" {
		sb.WriteString("Output format: " + instr.CommandFormat + "\n")
	}
	if instr.Prompt != "" {
		sb.WriteString("Context: " + instr.Prompt + "\n")
	}

	sb.WriteString("\nReturn: {\"commands\": [{\"action\": \"...\", \"params\": {...}}], \"response\": \"short confirmation in the user's language\"}\n")
	return sb.String()
}

func parseCommands(content string) ([]message.Command, string, error) {
	var wrapper struct {
		Commands []message.Command `json:"commands"`
		Response string            `json:"response"`
	}
	if err := json.Unmarshal([]byte(content), &wrapper); err == nil && len(wrapper.Commands) > 0 {
		for idx := range wrapper.Commands {
			raw, _ := json.Marshal(wrapper.Commands[idx])
			wrapper.Commands[idx].Raw = raw
		}
		return wrapper.Commands, wrapper.Response, nil
	}

	var single message.Command
	if err := json.Unmarshal([]byte(content), &single); err == nil && single.Action != "" {
		raw, _ := json.Marshal(single)
		single.Raw = raw
		return []message.Command{single}, "", nil
	}

	return nil, "", fmt.Errorf("could not parse LLM response: %.200s", content)
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
	default:
		return ".wav"
	}
}
