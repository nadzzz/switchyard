// Package interpreter defines the interface for LLM-based audio interpretation.
//
// An interpreter takes audio (or text) and an instruction, then produces
// structured commands. Switchyard ships with two backends: OpenAI (cloud)
// and Local (self-hosted via Ollama/whisper.cpp).
package interpreter

import (
	"context"

	"github.com/nadzzz/switchyard/internal/message"
)

// TranscribeOpts controls transcription behavior.
type TranscribeOpts struct {
	// Language is the ISO-639-1 code (e.g., "en", "fr") to guide transcription.
	Language string

	// Prompt provides context to improve recognition of domain-specific terms.
	Prompt string

	// Model overrides the default transcription model.
	Model string
}

// Interpreter is the interface for audio transcription and command generation.
type Interpreter interface {
	// Name returns the backend identifier (e.g., "openai", "local").
	Name() string

	// Transcribe converts audio bytes to text.
	Transcribe(ctx context.Context, audio []byte, contentType string, opts TranscribeOpts) (string, error)

	// Interpret takes transcribed text and an instruction, then produces commands.
	Interpret(ctx context.Context, text string, instruction message.Instruction) ([]message.Command, error)

	// Close releases any resources held by the interpreter.
	Close() error
}
