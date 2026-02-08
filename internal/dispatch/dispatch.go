// Package dispatch implements the core message routing engine.
//
// The dispatcher receives messages from transports, runs them through
// the interpreter pipeline (transcribe → interpret), then routes the
// resulting commands to target services. The sender always receives
// the response — this is an architectural invariant.
package dispatch

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"time"

	"github.com/nadzzz/switchyard/internal/interpreter"
	"github.com/nadzzz/switchyard/internal/message"
	"github.com/nadzzz/switchyard/internal/transport"
)

// Dispatcher is the central routing engine.
type Dispatcher struct {
	interpreter interpreter.Interpreter
	transports  map[string]transport.Transport
}

// New creates a new Dispatcher with the given interpreter and transports.
func New(interp interpreter.Interpreter, transports []transport.Transport) *Dispatcher {
	tm := make(map[string]transport.Transport, len(transports))
	for _, t := range transports {
		tm[t.Name()] = t
	}
	return &Dispatcher{
		interpreter: interp,
		transports:  tm,
	}
}

// Handle processes a single message through the full pipeline.
// This function is passed as the transport.Handler to each transport.
func (d *Dispatcher) Handle(ctx context.Context, msg *message.Message) (*message.DispatchResult, error) {
	start := time.Now()
	logger := slog.With("message_id", msg.ID, "source", msg.Source)
	logger.Info("dispatch started")

	result := &message.DispatchResult{
		MessageID: msg.ID,
	}

	// Step 1: Transcribe audio (if present).
	var transcript string
	if msg.HasAudio() {
		logger.Debug("transcribing audio", "content_type", msg.ContentType, "bytes", len(msg.Audio))
		var err error
		transcript, err = d.interpreter.Transcribe(ctx, msg.Audio, msg.ContentType, interpreter.TranscribeOpts{
			Prompt: msg.Instruction.Prompt,
		})
		if err != nil {
			result.Error = fmt.Sprintf("transcription failed: %v", err)
			logger.Error("transcription failed", "error", err)
			return result, nil
		}
		result.Transcript = transcript
		logger.Info("transcription complete", "text_length", len(transcript))
	} else if msg.Text != "" {
		transcript = msg.Text
		result.Transcript = transcript
		logger.Debug("using text input directly")
	} else {
		result.Error = "message has no audio and no text"
		return result, nil
	}

	// Step 2: Interpret transcript into commands.
	commands, err := d.interpreter.Interpret(ctx, transcript, msg.Instruction)
	if err != nil {
		result.Error = fmt.Sprintf("interpretation failed: %v", err)
		logger.Error("interpretation failed", "error", err)
		return result, nil
	}
	result.Commands = commands
	logger.Info("interpretation complete", "commands", len(commands))

	// Step 3: Route commands to target services.
	payload, err := json.Marshal(result)
	if err != nil {
		result.Error = fmt.Sprintf("marshalling result: %v", err)
		return result, nil
	}

	for _, target := range msg.Instruction.Targets {
		t, ok := d.transports[target.Protocol]
		if !ok {
			logger.Warn("no transport for target protocol", "protocol", target.Protocol, "target", target.ServiceName)
			continue
		}

		if err := t.Send(ctx, target, payload); err != nil {
			logger.Error("failed to send to target", "target", target.ServiceName, "error", err)
			continue
		}

		result.RoutedTo = append(result.RoutedTo, target.ServiceName)
		logger.Info("routed to target", "target", target.ServiceName)
	}

	logger.Info("dispatch complete", "duration", time.Since(start), "routed_to", len(result.RoutedTo))

	// The result is always returned to the sender via the transport that received the message.
	return result, nil
}
