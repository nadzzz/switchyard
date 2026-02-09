// Switchyard is a voice-first message dispatch daemon that interprets audio
// inputs and routes structured commands to target services.
//
// Usage:
//
//	switchyard [flags]
//	switchyard --config /path/to/switchyard.yaml
package main

import (
	"context"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"sync"
	"syscall"

	"github.com/nadzzz/switchyard/internal/config"
	"github.com/nadzzz/switchyard/internal/dispatch"
	"github.com/nadzzz/switchyard/internal/health"
	"github.com/nadzzz/switchyard/internal/interpreter"
	localinterp "github.com/nadzzz/switchyard/internal/interpreter/local"
	openaiinterp "github.com/nadzzz/switchyard/internal/interpreter/openai"
	"github.com/nadzzz/switchyard/internal/transport"
	grpctransport "github.com/nadzzz/switchyard/internal/transport/grpc"
	httptransport "github.com/nadzzz/switchyard/internal/transport/http"
	mqtttransport "github.com/nadzzz/switchyard/internal/transport/mqtt"
)

// version is set at build time via ldflags.
var version = "dev"

func main() {
	showVersion := flag.Bool("version", false, "print version and exit")
	configFile := flag.String("config", "", "path to config file (e.g. configs/switchyard.local.yaml)")
	flag.Parse()

	if *showVersion {
		fmt.Printf("switchyard %s\n", version)
		os.Exit(0)
	}

	// Load configuration.
	cfg, err := config.Load(*configFile)
	if err != nil {
		slog.Error("failed to load configuration", "error", err)
		os.Exit(1)
	}

	// Setup structured logging.
	config.SetupLogging(cfg.Logging)
	slog.Info("switchyard starting", "version", version)

	// Create root context with signal handling for graceful shutdown.
	ctx, cancel := signal.NotifyContext(context.Background(),
		syscall.SIGINT, syscall.SIGTERM)
	defer cancel()

	// Initialize the interpreter backend.
	var interp interpreter.Interpreter
	switch cfg.Interpreter.Backend {
	case "openai":
		interp = openaiinterp.New(cfg.Interpreter.OpenAI)
		slog.Info("using OpenAI interpreter",
			"transcription_model", cfg.Interpreter.OpenAI.TranscriptionModel,
			"completion_model", cfg.Interpreter.OpenAI.CompletionModel)
	case "local":
		interp = localinterp.New(cfg.Interpreter.Local)
		slog.Info("using local interpreter",
			"whisper", cfg.Interpreter.Local.WhisperEndpoint,
			"llm", cfg.Interpreter.Local.LLMEndpoint)
	default:
		slog.Error("unknown interpreter backend", "backend", cfg.Interpreter.Backend)
		os.Exit(1)
	}
	defer interp.Close()

	// Initialize enabled transports.
	var transports []transport.Transport

	if cfg.Transports.GRPC.Enabled {
		transports = append(transports, grpctransport.New(cfg.Transports.GRPC.Port))
	}
	if cfg.Transports.HTTP.Enabled {
		transports = append(transports, httptransport.New(cfg.Transports.HTTP.Port))
	}
	if cfg.Transports.MQTT.Enabled {
		transports = append(transports, mqtttransport.New(cfg.Transports.MQTT.Broker, cfg.Transports.MQTT.Topic))
	}

	if len(transports) == 0 {
		slog.Error("no transports enabled — enable at least one in config")
		os.Exit(1)
	}

	// Create the dispatcher.
	dispatcher := dispatch.New(interp, transports)

	// Start health check server.
	healthServer := health.New(cfg.Server.HealthPort)
	go func() {
		if err := healthServer.ListenAndServe(ctx); err != nil {
			slog.Error("health server failed", "error", err)
		}
	}()

	// Start all transports.
	var wg sync.WaitGroup
	for _, t := range transports {
		wg.Add(1)
		go func(t transport.Transport) {
			defer wg.Done()
			slog.Info("starting transport", "name", t.Name())
			if err := t.Listen(ctx, dispatcher.Handle); err != nil {
				slog.Error("transport failed", "name", t.Name(), "error", err)
			}
		}(t)
	}

	// Mark as ready once all transports are started.
	healthServer.SetReady(true)
	slog.Info("switchyard ready",
		"transports", len(transports),
		"health_port", cfg.Server.HealthPort)

	// Block until shutdown signal.
	<-ctx.Done()
	slog.Info("shutdown signal received, draining...")

	// Close all transports gracefully.
	for _, t := range transports {
		if err := t.Close(); err != nil {
			slog.Error("transport close error", "name", t.Name(), "error", err)
		}
	}

	wg.Wait()
	slog.Info("switchyard stopped")
}
