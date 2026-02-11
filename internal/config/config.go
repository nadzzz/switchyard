// Package config handles loading and validating the switchyard configuration.
package config

import (
	"fmt"
	"log/slog"
	"os"
	"strings"

	"github.com/spf13/viper"
)

// Config is the root configuration for the switchyard daemon.
type Config struct {
	Server      ServerConfig      `mapstructure:"server"`
	Transports  TransportsConfig  `mapstructure:"transports"`
	Interpreter InterpreterConfig `mapstructure:"interpreter"`
	TTS         TTSConfig         `mapstructure:"tts"`
	Targets     map[string]Target `mapstructure:"targets"`
	Logging     LoggingConfig     `mapstructure:"logging"`
}

// ServerConfig holds the health check server settings.
type ServerConfig struct {
	HealthPort int `mapstructure:"health_port"`
}

// TransportsConfig holds the configuration for each transport layer.
type TransportsConfig struct {
	GRPC GRPCConfig `mapstructure:"grpc"`
	HTTP HTTPConfig `mapstructure:"http"`
	MQTT MQTTConfig `mapstructure:"mqtt"`
}

// GRPCConfig configures the gRPC transport.
type GRPCConfig struct {
	Enabled bool `mapstructure:"enabled"`
	Port    int  `mapstructure:"port"`
}

// HTTPConfig configures the HTTP/WebSocket transport.
type HTTPConfig struct {
	Enabled bool `mapstructure:"enabled"`
	Port    int  `mapstructure:"port"`
}

// MQTTConfig configures the MQTT transport.
type MQTTConfig struct {
	Enabled bool   `mapstructure:"enabled"`
	Broker  string `mapstructure:"broker"`
	Topic   string `mapstructure:"topic"`
}

// InterpreterConfig selects and configures the LLM backend.
type InterpreterConfig struct {
	Backend string       `mapstructure:"backend"` // "openai" or "local"
	OpenAI  OpenAIConfig `mapstructure:"openai"`
	Local   LocalConfig  `mapstructure:"local"`
}

// OpenAIConfig holds OpenAI API settings.
type OpenAIConfig struct {
	APIKey             string `mapstructure:"api_key"`
	TranscriptionModel string `mapstructure:"transcription_model"`
	CompletionModel    string `mapstructure:"completion_model"`
}

// LocalConfig holds self-hosted LLM settings.
type LocalConfig struct {
	WhisperEndpoint string `mapstructure:"whisper_endpoint"`
	WhisperType     string `mapstructure:"whisper_type"` // "openai" (default) or "asr" (ahmetoner/whisper-asr-webservice)
	LLMEndpoint     string `mapstructure:"llm_endpoint"`
	LLMModel        string `mapstructure:"llm_model"` // Ollama model name (e.g., "llama3.2:1b")
	VADFilter       bool   `mapstructure:"vad_filter"`
	Language        string `mapstructure:"language"` // ISO-639-1 default language (e.g., "en", "fr")
}

// Target defines a downstream service in the config file.
type Target struct {
	Endpoint string `mapstructure:"endpoint"`
	Protocol string `mapstructure:"protocol"`
	Token    string `mapstructure:"token"`
}

// TTSConfig selects and configures the text-to-speech backend.
type TTSConfig struct {
	Enabled bool        `mapstructure:"enabled"`
	Backend string      `mapstructure:"backend"` // "piper"
	Piper   PiperConfig `mapstructure:"piper"`
}

// PiperConfig holds Piper TTS settings (Wyoming protocol).
//
// For a single Piper instance that serves all languages, set Endpoint.
// For per-language instances (recommended for production), set Endpoints
// which maps ISO-639-1 codes to individual Wyoming TCP endpoints.
// If both are set, Endpoints takes precedence and Endpoint is the fallback.
type PiperConfig struct {
	Endpoint  string            `mapstructure:"endpoint"`  // Default Wyoming TCP endpoint (host:port)
	Endpoints map[string]string `mapstructure:"endpoints"` // ISO-639-1 language code -> Wyoming TCP endpoint
	Voices    map[string]string `mapstructure:"voices"`    // ISO-639-1 language code -> Piper voice model name
}

// LoggingConfig holds structured logging settings.
type LoggingConfig struct {
	Level  string `mapstructure:"level"`  // debug, info, warn, error
	Format string `mapstructure:"format"` // json, text
}

// Load reads the configuration from file, environment variables, and defaults.
// If configFile is non-empty it is used directly; otherwise the standard
// search order applies: ./switchyard.yaml, ./configs/switchyard.yaml, /etc/switchyard/switchyard.yaml.
func Load(configFile string) (*Config, error) {
	v := viper.New()

	// Defaults
	v.SetDefault("server.health_port", 8081)
	v.SetDefault("transports.grpc.enabled", true)
	v.SetDefault("transports.grpc.port", 50051)
	v.SetDefault("transports.http.enabled", true)
	v.SetDefault("transports.http.port", 8080)
	v.SetDefault("transports.mqtt.enabled", false)
	v.SetDefault("transports.mqtt.broker", "tcp://localhost:1883")
	v.SetDefault("transports.mqtt.topic", "switchyard/#")
	v.SetDefault("interpreter.backend", "openai")
	v.SetDefault("interpreter.openai.transcription_model", "gpt-4o-transcribe")
	v.SetDefault("interpreter.openai.completion_model", "gpt-4o")
	v.SetDefault("interpreter.local.whisper_endpoint", "http://localhost:8000/v1/audio/transcriptions")
	v.SetDefault("interpreter.local.whisper_type", "openai")
	v.SetDefault("interpreter.local.llm_endpoint", "http://localhost:11434/api/generate")
	v.SetDefault("interpreter.local.llm_model", "llama3")
	v.SetDefault("interpreter.local.vad_filter", false)
	v.SetDefault("interpreter.local.language", "")
	v.SetDefault("tts.enabled", false)
	v.SetDefault("tts.backend", "piper")
	v.SetDefault("tts.piper.endpoint", "localhost:10200")
	v.SetDefault("logging.level", "info")
	v.SetDefault("logging.format", "json")

	// Config file
	if configFile != "" {
		v.SetConfigFile(configFile)
	} else {
		v.SetConfigName("switchyard")
		v.SetConfigType("yaml")
		v.AddConfigPath(".")
		v.AddConfigPath("./configs")
		v.AddConfigPath("/etc/switchyard")
	}

	// Environment variables: SWITCHYARD_SERVER_HEALTH_PORT, SWITCHYARD_INTERPRETER_BACKEND, etc.
	v.SetEnvPrefix("SWITCHYARD")
	v.SetEnvKeyReplacer(strings.NewReplacer(".", "_"))
	v.AutomaticEnv()

	// Read config file (optional — env vars and defaults are sufficient)
	if err := v.ReadInConfig(); err != nil {
		if _, ok := err.(viper.ConfigFileNotFoundError); !ok {
			return nil, fmt.Errorf("reading config: %w", err)
		}
		slog.Info("no config file found, using defaults and environment variables")
	} else {
		slog.Info("loaded config file", "path", v.ConfigFileUsed())
	}

	var cfg Config
	if err := v.Unmarshal(&cfg); err != nil {
		return nil, fmt.Errorf("unmarshalling config: %w", err)
	}

	// Resolve env var references in sensitive fields (e.g., "${OPENAI_API_KEY}")
	cfg.Interpreter.OpenAI.APIKey = resolveEnvRef(cfg.Interpreter.OpenAI.APIKey)
	for name, target := range cfg.Targets {
		target.Token = resolveEnvRef(target.Token)
		cfg.Targets[name] = target
	}

	return &cfg, nil
}

// resolveEnvRef replaces "${VAR_NAME}" patterns with the corresponding env var value.
func resolveEnvRef(val string) string {
	if strings.HasPrefix(val, "${") && strings.HasSuffix(val, "}") {
		envKey := val[2 : len(val)-1]
		if envVal := os.Getenv(envKey); envVal != "" {
			return envVal
		}
	}
	return val
}

// SetupLogging configures the global slog logger based on config.
func SetupLogging(cfg LoggingConfig) {
	var level slog.Level
	switch strings.ToLower(cfg.Level) {
	case "debug":
		level = slog.LevelDebug
	case "warn":
		level = slog.LevelWarn
	case "error":
		level = slog.LevelError
	default:
		level = slog.LevelInfo
	}

	opts := &slog.HandlerOptions{Level: level}

	var handler slog.Handler
	if strings.ToLower(cfg.Format) == "text" {
		handler = slog.NewTextHandler(os.Stdout, opts)
	} else {
		handler = slog.NewJSONHandler(os.Stdout, opts)
	}

	slog.SetDefault(slog.New(handler))
}
