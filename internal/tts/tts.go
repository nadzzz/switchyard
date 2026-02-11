// Package tts defines the interface for text-to-speech synthesis.
//
// Switchyard uses TTS to generate audio responses in the same language that
// was detected during transcription. This allows voice-in → voice-out
// interactions through the dispatch pipeline.
package tts

import "context"

// SynthesizeOpts controls synthesis behavior.
type SynthesizeOpts struct {
	// Language is the ISO-639-1 code (e.g., "en", "fr", "es") to select the voice.
	Language string

	// Voice overrides automatic language-based voice selection.
	Voice string
}

// Synthesizer converts text to audio.
type Synthesizer interface {
	// Synthesize generates audio (raw PCM 16-bit LE) from the given text.
	// The returned audio can be wrapped in a WAV container by the caller.
	Synthesize(ctx context.Context, text string, opts SynthesizeOpts) (*SynthesizeResult, error)

	// Close releases any resources held by the synthesizer.
	Close() error
}

// SynthesizeResult holds the output of TTS synthesis.
type SynthesizeResult struct {
	// Audio is the synthesized audio as a WAV file.
	Audio []byte

	// ContentType is the MIME type of the audio (e.g., "audio/wav").
	ContentType string

	// SampleRate is the audio sample rate in Hz (e.g., 22050).
	SampleRate int

	// Channels is the number of audio channels (typically 1).
	Channels int
}
