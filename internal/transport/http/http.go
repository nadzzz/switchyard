// Package http implements the HTTP/WebSocket transport for switchyard.
//
// This transport exposes a REST API for command dispatch and a WebSocket
// endpoint for streaming audio. It is best suited for web clients, phones,
// and services that prefer HTTP-based communication.
package http

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"time"

	"github.com/nadzzz/switchyard/internal/message"
	"github.com/nadzzz/switchyard/internal/transport"

	httpSwagger "github.com/swaggo/http-swagger/v2"
)

// Transport implements transport.Transport over HTTP and WebSocket.
type Transport struct {
	port   int
	server *http.Server
}

// New creates a new HTTP transport on the given port.
func New(port int) *Transport {
	return &Transport{port: port}
}

// Name returns the transport identifier.
func (t *Transport) Name() string { return "http" }

// Listen starts the HTTP server and routes incoming requests to the handler.
func (t *Transport) Listen(ctx context.Context, handler transport.Handler) error {
	mux := http.NewServeMux()

	// POST /dispatch — accepts audio or text, returns commands.
	mux.HandleFunc("POST /dispatch", func(w http.ResponseWriter, r *http.Request) {
		t.handleDispatch(w, r, handler)
	})

	// GET /ws — WebSocket endpoint for streaming audio (future).
	mux.HandleFunc("GET /ws", func(w http.ResponseWriter, r *http.Request) {
		// TODO: Implement WebSocket upgrade and streaming audio handling.
		http.Error(w, "websocket not yet implemented", http.StatusNotImplemented)
	})

	// Swagger UI — serves the generated OpenAPI docs.
	mux.Handle("GET /swagger/", httpSwagger.Handler(
		httpSwagger.URL("/swagger/doc.json"),
	))

	t.server = &http.Server{
		Addr:              fmt.Sprintf(":%d", t.port),
		Handler:           mux,
		ReadHeaderTimeout: 10 * time.Second,
	}

	slog.Info("http transport listening", "port", t.port)

	go func() {
		<-ctx.Done()
		slog.Info("http transport shutting down")
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = t.server.Shutdown(shutdownCtx)
	}()

	if err := t.server.ListenAndServe(); err != http.ErrServerClosed {
		return fmt.Errorf("http listen: %w", err)
	}
	return nil
}

// handleDispatch processes a POST /dispatch request.
//
// @Summary     Dispatch a voice or text command
// @Description Accepts a JSON message (with optional pre-transcribed text or base64 audio) or raw audio bytes.
// @Description The message is run through the interpreter pipeline (transcribe → interpret) and the resulting
// @Description commands are routed to the configured target services.
// @Tags        dispatch
// @Accept      json
// @Accept      audio/wav
// @Accept      audio/ogg
// @Produce     json
// @Param       message  body      message.Message  true  "Dispatch request (JSON). For raw audio, POST the bytes directly with the appropriate Content-Type."
// @Param       X-Switchyard-Source       header  string  false  "Sender identifier (used with raw audio uploads)"
// @Param       X-Switchyard-Instruction  header  string  false  "JSON-encoded Instruction (used with raw audio uploads)"
// @Success     200  {object}  message.DispatchResult  "Interpreted commands"
// @Failure     400  {string}  string  "Invalid request body or headers"
// @Failure     500  {string}  string  "Internal processing error"
// @Router      /dispatch [post]
func (t *Transport) handleDispatch(w http.ResponseWriter, r *http.Request, handler transport.Handler) {
	var msg message.Message

	contentType := r.Header.Get("Content-Type")
	switch {
	case contentType == "application/json":
		if err := json.NewDecoder(r.Body).Decode(&msg); err != nil {
			http.Error(w, "invalid json: "+err.Error(), http.StatusBadRequest)
			return
		}
	default:
		// Treat body as raw audio; read instruction from headers.
		audioData, err := io.ReadAll(io.LimitReader(r.Body, 25<<20)) // 25 MB limit
		if err != nil {
			http.Error(w, "reading audio: "+err.Error(), http.StatusBadRequest)
			return
		}
		msg.Audio = audioData
		msg.ContentType = contentType
		msg.Source = r.Header.Get("X-Switchyard-Source")

		// Instruction can be passed as a JSON header.
		if instrHeader := r.Header.Get("X-Switchyard-Instruction"); instrHeader != "" {
			if err := json.Unmarshal([]byte(instrHeader), &msg.Instruction); err != nil {
				http.Error(w, "invalid instruction header: "+err.Error(), http.StatusBadRequest)
				return
			}
		}
	}

	result, err := handler(r.Context(), &msg)
	if err != nil {
		slog.Error("dispatch failed", "error", err)
		http.Error(w, "dispatch error: "+err.Error(), http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(result)
}

// Send delivers a payload to an HTTP target via POST.
func (t *Transport) Send(ctx context.Context, target message.Target, payload []byte) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, target.Endpoint, nil)
	if err != nil {
		return fmt.Errorf("http send: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")

	// Attach the payload.
	req.Body = io.NopCloser(io.LimitReader(
		io.NopCloser(jsonReader(payload)), 25<<20,
	))

	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return fmt.Errorf("http send: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 400 {
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 1024))
		return fmt.Errorf("http send: status %d: %s", resp.StatusCode, body)
	}

	slog.Debug("http send success", "target", target.Endpoint, "status", resp.StatusCode)
	return nil
}

// Close gracefully shuts down the HTTP server.
func (t *Transport) Close() error {
	if t.server != nil {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		return t.server.Shutdown(ctx)
	}
	return nil
}

// jsonReader wraps a byte slice as an io.Reader.
func jsonReader(data []byte) io.ReadCloser {
	return io.NopCloser(io.LimitReader(
		&byteReader{data: data}, int64(len(data)),
	))
}

type byteReader struct {
	data []byte
	pos  int
}

func (r *byteReader) Read(p []byte) (int, error) {
	if r.pos >= len(r.data) {
		return 0, io.EOF
	}
	n := copy(p, r.data[r.pos:])
	r.pos += n
	return n, nil
}
