// Package grpc implements the gRPC transport for switchyard.
//
// This transport exposes a gRPC server that accepts DispatchRequest messages
// containing audio payloads and instructions. It is the preferred transport
// for low-latency, strongly-typed communication with robots and edge devices.
package grpc

import (
	"context"
	"fmt"
	"log/slog"
	"net"

	"github.com/nadzzz/switchyard/internal/message"
	"github.com/nadzzz/switchyard/internal/transport"
	"google.golang.org/grpc"
)

// Transport implements transport.Transport over gRPC.
type Transport struct {
	port   int
	server *grpc.Server
}

// New creates a new gRPC transport on the given port.
func New(port int) *Transport {
	return &Transport{port: port}
}

// Name returns the transport identifier.
func (t *Transport) Name() string { return "grpc" }

// Listen starts the gRPC server and routes incoming requests to the handler.
func (t *Transport) Listen(ctx context.Context, handler transport.Handler) error {
	lis, err := net.Listen("tcp", fmt.Sprintf(":%d", t.port))
	if err != nil {
		return fmt.Errorf("grpc listen: %w", err)
	}

	t.server = grpc.NewServer()

	// TODO: Register the generated SwitchyardService server here once proto is compiled.
	// pb.RegisterSwitchyardServiceServer(t.server, &serviceServer{handler: handler})

	slog.Info("grpc transport listening", "port", t.port)

	go func() {
		<-ctx.Done()
		slog.Info("grpc transport shutting down")
		t.server.GracefulStop()
	}()

	return t.server.Serve(lis)
}

// Send delivers a payload to a gRPC target.
func (t *Transport) Send(ctx context.Context, target message.Target, payload []byte) error {
	// TODO: Implement gRPC client send to target endpoint.
	slog.Debug("grpc send", "target", target.Endpoint, "bytes", len(payload))
	return nil
}

// Close gracefully stops the gRPC server.
func (t *Transport) Close() error {
	if t.server != nil {
		t.server.GracefulStop()
	}
	return nil
}
