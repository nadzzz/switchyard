// Package transport defines the interface for pluggable message transports.
//
// Each transport (gRPC, HTTP/WebSocket, MQTT) implements this interface and
// registers itself with the dispatcher. The dispatcher doesn't care how messages
// arrive — it only works with the Transport contract.
package transport

import (
	"context"

	"github.com/nadzzz/switchyard/internal/message"
)

// Handler is a function that processes an incoming message and returns a result.
// The dispatcher provides this handler to each transport.
type Handler func(ctx context.Context, msg *message.Message) (*message.DispatchResult, error)

// Transport is the interface that every transport adapter must implement.
type Transport interface {
	// Name returns the transport identifier (e.g., "grpc", "http", "mqtt").
	Name() string

	// Listen starts accepting incoming messages and dispatches them to the handler.
	// It blocks until the context is cancelled.
	Listen(ctx context.Context, handler Handler) error

	// Send delivers a payload to a target address using this transport's protocol.
	Send(ctx context.Context, target message.Target, payload []byte) error

	// Close gracefully shuts down the transport, draining in-flight work.
	Close() error
}
