// Package message defines the core data types flowing through the switchyard pipeline.
package message

import (
	"encoding/json"
	"time"
)

// Message represents an incoming request from any transport.
type Message struct {
	// ID is a unique identifier for this message (UUID).
	ID string `json:"id"`

	// Source identifies the sender (e.g., "robot-arm-01", "phone-alice").
	Source string `json:"source"`

	// Audio is the raw audio payload. Nil if the message is text-only.
	Audio []byte `json:"audio,omitempty"`

	// ContentType is the MIME type of the audio (e.g., "audio/wav", "audio/ogg").
	ContentType string `json:"content_type,omitempty"`

	// Text is an optional pre-transcribed text input (bypasses transcription).
	Text string `json:"text,omitempty"`

	// Instruction tells switchyard how to interpret and route the response.
	Instruction Instruction `json:"instruction"`

	// Timestamp is when the message was received by switchyard.
	Timestamp time.Time `json:"timestamp"`
}

// HasAudio returns true if the message contains an audio payload.
func (m *Message) HasAudio() bool {
	return len(m.Audio) > 0
}

// Instruction describes how to process and route a message.
type Instruction struct {
	// Targets lists the services that should receive the interpreted commands.
	// The original sender always receives the response regardless of this list.
	Targets []Target `json:"targets,omitempty"`

	// ResponseFormat specifies the desired output format (e.g., "homeassistant", "json", "ros2").
	ResponseFormat string `json:"response_format"`

	// Prompt is additional context for the LLM interpreter (e.g., "return motor commands").
	Prompt string `json:"prompt,omitempty"`
}

// Target defines a downstream service that should receive commands.
type Target struct {
	// ServiceName is a human-readable identifier (e.g., "homeassistant", "robot").
	ServiceName string `json:"service_name"`

	// Endpoint is the address to reach this target (e.g., "http://ha.local:8123/api/services").
	Endpoint string `json:"endpoint"`

	// Protocol is the protocol to use ("http", "grpc", "mqtt").
	Protocol string `json:"protocol"`

	// FormatTemplate is an optional Go template to transform commands before sending.
	FormatTemplate string `json:"format_template,omitempty"`
}

// Command is a single structured command produced by the interpreter.
type Command struct {
	// Action is the command verb (e.g., "turn_on", "move_to", "set_temperature").
	Action string `json:"action"`

	// Params holds action-specific parameters.
	Params map[string]any `json:"params,omitempty"`

	// Raw is the original JSON as returned by the LLM, preserved for forwarding.
	Raw json.RawMessage `json:"raw,omitempty"`
}

// DispatchResult is the outcome of processing a message through the pipeline.
type DispatchResult struct {
	// MessageID is the original message ID.
	MessageID string `json:"message_id"`

	// Transcript is the text produced by audio transcription (empty if text input).
	Transcript string `json:"transcript,omitempty"`

	// Language is the ISO-639-1 code detected during transcription (e.g., "en", "fr", "es").
	Language string `json:"language,omitempty"`

	// Commands is the list of interpreted commands.
	Commands []Command `json:"commands"`

	// RoutedTo lists the targets that received the commands.
	RoutedTo []string `json:"routed_to"`

	// ResponseText is a natural-language confirmation (in the detected language).
	ResponseText string `json:"response_text,omitempty"`

	// ResponseAudio is the TTS-synthesized audio of ResponseText (WAV format).
	ResponseAudio []byte `json:"response_audio,omitempty"`

	// ResponseContentType is the MIME type of ResponseAudio (e.g., "audio/wav").
	ResponseContentType string `json:"response_content_type,omitempty"`

	// Error is set if processing failed at any stage.
	Error string `json:"error,omitempty"`
}
