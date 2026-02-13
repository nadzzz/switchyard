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
	"github.com/nadzzz/switchyard/internal/tts"
)

// Dispatcher is the central routing engine.
type Dispatcher struct {
	interpreter interpreter.Interpreter
	transports  map[string]transport.Transport
	synthesizer tts.Synthesizer // nil if TTS is disabled
}

// New creates a new Dispatcher with the given interpreter and transports.
func New(interp interpreter.Interpreter, transports []transport.Transport, synthesizer tts.Synthesizer) *Dispatcher {
	tm := make(map[string]transport.Transport, len(transports))
	for _, t := range transports {
		tm[t.Name()] = t
	}
	return &Dispatcher{
		interpreter: interp,
		transports:  tm,
		synthesizer: synthesizer,
	}
}

// resolveResponseMode determines the effective ResponseMode for a message.
// If the caller didn't specify one, the default depends on whether TTS is available.
func (d *Dispatcher) resolveResponseMode(mode message.ResponseMode) message.ResponseMode {
	switch mode {
	case message.ResponseModeNone, message.ResponseModeText,
		message.ResponseModeAudio, message.ResponseModeTextAudio:
		return mode
	default:
		// Default: text+audio when TTS is available, text-only otherwise.
		if d.synthesizer != nil {
			return message.ResponseModeTextAudio
		}
		return message.ResponseModeText
	}
}

// wantText returns true if the response mode includes text output.
func wantText(mode message.ResponseMode) bool {
	return mode == message.ResponseModeText || mode == message.ResponseModeTextAudio
}

// wantAudio returns true if the response mode includes audio output.
func wantAudio(mode message.ResponseMode) bool {
	return mode == message.ResponseModeAudio || mode == message.ResponseModeTextAudio
}

// Handle processes a single message through the full pipeline.
// This function is passed as the transport.Handler to each transport.
func (d *Dispatcher) Handle(ctx context.Context, msg *message.Message) (*message.DispatchResult, error) {
	start := time.Now()
	logger := slog.With("message_id", msg.ID, "source", msg.Source)

	respMode := d.resolveResponseMode(msg.Instruction.ResponseMode)
	logger.Info("dispatch started", "response_mode", respMode)

	result := &message.DispatchResult{
		MessageID: msg.ID,
	}

	// Step 1: Transcribe audio (if present).
	var transcript string
	var detectedLang string
	if msg.HasAudio() {
		logger.Debug("transcribing audio", "content_type", msg.ContentType, "bytes", len(msg.Audio))
		res, err := d.interpreter.Transcribe(ctx, msg.Audio, msg.ContentType, interpreter.TranscribeOpts{
			Prompt: msg.Instruction.Prompt,
		})
		if err != nil {
			result.Error = fmt.Sprintf("transcription failed: %v", err)
			logger.Error("transcription failed", "error", err)
			return result, nil
		}
		transcript = res.Text
		detectedLang = res.Language
		result.Transcript = transcript
		result.Language = detectedLang
		logger.Info("transcription complete", "text_length", len(transcript), "language", detectedLang)
	} else if msg.Text != "" {
		transcript = msg.Text
		result.Transcript = transcript
		logger.Debug("using text input directly")
	} else {
		result.Error = "message has no audio and no text"
		return result, nil
	}

	// Step 2: Interpret transcript into commands.
	interpResult, err := d.interpreter.Interpret(ctx, transcript, msg.Instruction)
	if err != nil {
		result.Error = fmt.Sprintf("interpretation failed: %v", err)
		logger.Error("interpretation failed", "error", err)
		return result, nil
	}
	result.Commands = interpResult.Commands
	logger.Info("interpretation complete", "commands", len(interpResult.Commands))

	// Step 3: Populate natural-language response based on response_mode.
	if wantText(respMode) {
		result.ResponseText = interpResult.ResponseText
	}

	if wantAudio(respMode) && d.synthesizer != nil && interpResult.ResponseText != "" {
		lang := detectedLang
		if lang == "" {
			lang = "en"
		}
		logger.Debug("synthesizing response", "language", lang, "text_length", len(interpResult.ResponseText))
		synthResult, err := d.synthesizer.Synthesize(ctx, interpResult.ResponseText, tts.SynthesizeOpts{
			Language: lang,
		})
		if err != nil {
			logger.Warn("TTS synthesis failed, continuing without audio", "error", err)
		} else {
			result.SetResponseAudioBytes(synthResult.Audio)
			result.ResponseContentType = synthResult.ContentType
			logger.Info("TTS synthesis complete", "audio_bytes", len(synthResult.Audio))
		}
	}

	// Step 4: Route commands to target services.
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
