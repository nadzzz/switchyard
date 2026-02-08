// Package health provides a simple HTTP health check endpoint.
//
// Aspire, Docker, and Kubernetes all use this endpoint to monitor
// the daemon's liveness. When the daemon is running and ready to
// accept messages, /healthz returns 200 OK.
package health

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"net/http"
	"sync/atomic"
	"time"
)

// Server is a lightweight HTTP server that exposes /healthz.
type Server struct {
	port   int
	ready  atomic.Bool
	server *http.Server
}

// New creates a new health check server.
func New(port int) *Server {
	return &Server{port: port}
}

// SetReady marks the daemon as ready to accept traffic.
func (s *Server) SetReady(ready bool) {
	s.ready.Store(ready)
}

// ListenAndServe starts the health check HTTP server.
// It blocks until the context is cancelled.
func (s *Server) ListenAndServe(ctx context.Context) error {
	mux := http.NewServeMux()

	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		if !s.ready.Load() {
			w.WriteHeader(http.StatusServiceUnavailable)
			_ = json.NewEncoder(w).Encode(map[string]string{"status": "not_ready"})
			return
		}
		w.WriteHeader(http.StatusOK)
		_ = json.NewEncoder(w).Encode(map[string]string{"status": "ok"})
	})

	mux.HandleFunc("GET /readyz", func(w http.ResponseWriter, r *http.Request) {
		if !s.ready.Load() {
			w.WriteHeader(http.StatusServiceUnavailable)
			_ = json.NewEncoder(w).Encode(map[string]string{"status": "not_ready"})
			return
		}
		w.WriteHeader(http.StatusOK)
		_ = json.NewEncoder(w).Encode(map[string]string{"status": "ok"})
	})

	s.server = &http.Server{
		Addr:              fmt.Sprintf(":%d", s.port),
		Handler:           mux,
		ReadHeaderTimeout: 5 * time.Second,
	}

	slog.Info("health server listening", "port", s.port)

	go func() {
		<-ctx.Done()
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = s.server.Shutdown(shutdownCtx)
	}()

	if err := s.server.ListenAndServe(); err != http.ErrServerClosed {
		return fmt.Errorf("health server: %w", err)
	}
	return nil
}
