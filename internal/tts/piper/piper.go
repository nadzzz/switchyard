// Package piper implements the TTS Synthesizer using a Piper Wyoming protocol server.
//
// Piper is a fast, local neural text-to-speech system. The linuxserver/piper
// container exposes the Wyoming protocol on TCP port 10200. This package
// implements a client for that protocol to synthesize speech.
//
// Wyoming protocol format (per event):
//
//	<json_length> <payload_length>\n
//	<json_bytes>\n
//	<payload_bytes>   (if payload_length > 0)
package piper

import (
	"bytes"
	"context"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net"
	"strconv"
	"strings"
	"time"

	"github.com/nadzzz/switchyard/internal/config"
	"github.com/nadzzz/switchyard/internal/tts"
)

// defaultVoices maps ISO-639-1 language codes to Piper voice model names.
var defaultVoices = map[string]string{
	"en": "en_US-lessac-medium",
	"fr": "fr_FR-siwis-medium",
	"es": "es_ES-mls_10246-low",
	"de": "de_DE-thorsten-medium",
	"it": "it_IT-riccardo-x_low",
	"pt": "pt_BR-faber-medium",
	"nl": "nl_NL-mls-medium",
	"pl": "pl_PL-darkman-medium",
	"ru": "ru_RU-ruslan-medium",
	"ja": "ja_JP-amitaro-medium",
	"ko": "ko_KR-kss-x_low",
	"zh": "zh_CN-huayan-medium",
}

// Synthesizer implements tts.Synthesizer using the Wyoming protocol.
type Synthesizer struct {
	endpoint  string            // default host:port of the Piper Wyoming server
	endpoints map[string]string // language -> host:port for per-language Piper instances
	voices    map[string]string // language -> voice name overrides
}

// New creates a new Piper synthesizer from config.
func New(cfg config.PiperConfig) *Synthesizer {
	// Merge user-configured voices with defaults.
	voices := make(map[string]string, len(defaultVoices))
	for k, v := range defaultVoices {
		voices[k] = v
	}
	for k, v := range cfg.Voices {
		voices[k] = v
	}

	cleanEndpoint := func(ep string) string {
		ep = strings.TrimPrefix(ep, "tcp://")
		ep = strings.TrimPrefix(ep, "http://")
		return ep
	}

	endpoint := cleanEndpoint(cfg.Endpoint)

	endpoints := make(map[string]string, len(cfg.Endpoints))
	for lang, ep := range cfg.Endpoints {
		endpoints[lang] = cleanEndpoint(ep)
	}

	return &Synthesizer{
		endpoint:  endpoint,
		endpoints: endpoints,
		voices:    voices,
	}
}

// Synthesize sends text to the Piper server and returns synthesized audio as WAV.
func (s *Synthesizer) Synthesize(ctx context.Context, text string, opts tts.SynthesizeOpts) (*tts.SynthesizeResult, error) {
	if text == "" {
		return nil, fmt.Errorf("empty text for synthesis")
	}

	// Select voice based on language or explicit override.
	voice := opts.Voice
	if voice == "" {
		voice = s.voices[opts.Language]
	}
	if voice == "" {
		voice = s.voices["en"] // fallback to English
	}

	// Select endpoint: per-language endpoint if available, else fallback.
	endpoint := s.endpoints[opts.Language]
	if endpoint == "" {
		endpoint = s.endpoint
	}
	if endpoint == "" {
		return nil, fmt.Errorf("no piper endpoint configured for language %q", opts.Language)
	}

	slog.Debug("piper synthesize", "text_length", len(text), "voice", voice, "language", opts.Language, "endpoint", endpoint)

	// Connect to the Wyoming server.
	dialer := net.Dialer{Timeout: 10 * time.Second}
	conn, err := dialer.DialContext(ctx, "tcp", endpoint)
	if err != nil {
		return nil, fmt.Errorf("connecting to piper: %w", err)
	}
	defer conn.Close()

	// Set deadline from context.
	if deadline, ok := ctx.Deadline(); ok {
		_ = conn.SetDeadline(deadline)
	} else {
		_ = conn.SetDeadline(time.Now().Add(30 * time.Second))
	}

	// Send synthesize event.
	synthEvent := wyomingEvent{
		Type: "synthesize",
		Data: map[string]any{
			"text": text,
			"voice": map[string]any{
				"name": voice,
			},
		},
	}
	if err := writeEvent(conn, synthEvent, nil); err != nil {
		return nil, fmt.Errorf("sending synthesize event: %w", err)
	}

	// Read response events: audio-start → audio-chunk* → audio-stop
	var (
		pcmBuf     bytes.Buffer
		sampleRate = 22050
		channels   = 1
		width      = 2
	)

	for {
		evt, payload, err := readEvent(conn)
		if err != nil {
			return nil, fmt.Errorf("reading piper event: %w", err)
		}

		switch evt.Type {
		case "audio-start":
			if rate, ok := evt.Data["rate"].(float64); ok {
				sampleRate = int(rate)
			}
			if ch, ok := evt.Data["channels"].(float64); ok {
				channels = int(ch)
			}
			if w, ok := evt.Data["width"].(float64); ok {
				width = int(w)
			}
			slog.Debug("piper audio-start", "rate", sampleRate, "channels", channels, "width", width)

		case "audio-chunk":
			if len(payload) > 0 {
				pcmBuf.Write(payload)
			}

		case "audio-stop":
			slog.Debug("piper audio-stop", "pcm_bytes", pcmBuf.Len())
			wav := pcmToWAV(pcmBuf.Bytes(), sampleRate, channels, width)
			return &tts.SynthesizeResult{
				Audio:       wav,
				ContentType: "audio/wav",
				SampleRate:  sampleRate,
				Channels:    channels,
			}, nil

		case "error":
			msg := "unknown error"
			if text, ok := evt.Data["text"].(string); ok {
				msg = text
			}
			return nil, fmt.Errorf("piper error: %s", msg)

		default:
			slog.Debug("piper unknown event", "type", evt.Type)
		}
	}
}

// Close is a no-op — connections are per-request.
func (s *Synthesizer) Close() error { return nil }

// --- Wyoming protocol helpers ---

type wyomingEvent struct {
	Type          string         `json:"type"`
	Data          map[string]any `json:"data,omitempty"`
	PayloadLength int            `json:"payload_length,omitempty"`
}

// writeEvent sends a Wyoming event over the connection.
func writeEvent(w io.Writer, evt wyomingEvent, payload []byte) error {
	evt.PayloadLength = 0 // omit from JSON; length goes in the header line
	jsonBytes, err := json.Marshal(evt)
	if err != nil {
		return fmt.Errorf("marshalling event: %w", err)
	}

	// Header: <json_length> <payload_length>\n
	header := fmt.Sprintf("%d %d\n", len(jsonBytes), len(payload))
	if _, err := io.WriteString(w, header); err != nil {
		return err
	}

	// JSON + newline
	if _, err := w.Write(jsonBytes); err != nil {
		return err
	}
	if _, err := io.WriteString(w, "\n"); err != nil {
		return err
	}

	// Payload (if any)
	if len(payload) > 0 {
		if _, err := w.Write(payload); err != nil {
			return err
		}
	}

	return nil
}

// readEvent reads a Wyoming event from the connection.
func readEvent(r io.Reader) (*wyomingEvent, []byte, error) {
	// Read header line: "<json_length> <payload_length>\n"
	headerBuf := make([]byte, 0, 64)
	oneByte := make([]byte, 1)
	for {
		if _, err := io.ReadFull(r, oneByte); err != nil {
			return nil, nil, fmt.Errorf("reading header: %w", err)
		}
		if oneByte[0] == '\n' {
			break
		}
		headerBuf = append(headerBuf, oneByte[0])
	}

	parts := strings.SplitN(string(headerBuf), " ", 2)
	if len(parts) != 2 {
		return nil, nil, fmt.Errorf("invalid wyoming header: %q", string(headerBuf))
	}

	jsonLen, err := strconv.Atoi(strings.TrimSpace(parts[0]))
	if err != nil {
		return nil, nil, fmt.Errorf("parsing json_length: %w", err)
	}
	payloadLen, err := strconv.Atoi(strings.TrimSpace(parts[1]))
	if err != nil {
		return nil, nil, fmt.Errorf("parsing payload_length: %w", err)
	}

	// Read JSON + trailing newline.
	jsonBuf := make([]byte, jsonLen+1) // +1 for the \n
	if _, err := io.ReadFull(r, jsonBuf); err != nil {
		return nil, nil, fmt.Errorf("reading json: %w", err)
	}
	jsonBuf = jsonBuf[:jsonLen] // strip trailing newline

	var evt wyomingEvent
	if err := json.Unmarshal(jsonBuf, &evt); err != nil {
		return nil, nil, fmt.Errorf("unmarshalling event: %w", err)
	}

	// Read payload.
	var payload []byte
	if payloadLen > 0 {
		payload = make([]byte, payloadLen)
		if _, err := io.ReadFull(r, payload); err != nil {
			return nil, nil, fmt.Errorf("reading payload: %w", err)
		}
	}

	return &evt, payload, nil
}

// pcmToWAV wraps raw PCM data in a WAV container.
func pcmToWAV(pcm []byte, sampleRate, channels, bytesPerSample int) []byte {
	dataLen := len(pcm)
	fileLen := 36 + dataLen // 44-byte header minus 8 bytes for RIFF header = 36

	buf := &bytes.Buffer{}
	buf.Grow(44 + dataLen)

	// RIFF header
	buf.WriteString("RIFF")
	_ = binary.Write(buf, binary.LittleEndian, uint32(fileLen))
	buf.WriteString("WAVE")

	// fmt subchunk
	buf.WriteString("fmt ")
	_ = binary.Write(buf, binary.LittleEndian, uint32(16))           // subchunk1 size
	_ = binary.Write(buf, binary.LittleEndian, uint16(1))            // audio format (PCM)
	_ = binary.Write(buf, binary.LittleEndian, uint16(channels))     // channels
	_ = binary.Write(buf, binary.LittleEndian, uint32(sampleRate))   // sample rate
	byteRate := sampleRate * channels * bytesPerSample
	_ = binary.Write(buf, binary.LittleEndian, uint32(byteRate))     // byte rate
	blockAlign := channels * bytesPerSample
	_ = binary.Write(buf, binary.LittleEndian, uint16(blockAlign))   // block align
	_ = binary.Write(buf, binary.LittleEndian, uint16(bytesPerSample*8)) // bits per sample

	// data subchunk
	buf.WriteString("data")
	_ = binary.Write(buf, binary.LittleEndian, uint32(dataLen))
	buf.Write(pcm)

	return buf.Bytes()
}
