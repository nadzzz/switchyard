// Package mqtt implements the MQTT transport for switchyard.
//
// MQTT is well-suited for IoT devices and lightweight pub/sub messaging.
// This transport subscribes to a configurable topic and publishes responses
// back to the sender's reply topic.
package mqtt

import (
	"context"
	"log/slog"

	"github.com/nadzzz/switchyard/internal/message"
	"github.com/nadzzz/switchyard/internal/transport"
)

// Transport implements transport.Transport over MQTT.
type Transport struct {
	broker string
	topic  string
}

// New creates a new MQTT transport.
func New(broker, topic string) *Transport {
	return &Transport{broker: broker, topic: topic}
}

// Name returns the transport identifier.
func (t *Transport) Name() string { return "mqtt" }

// Listen connects to the MQTT broker and subscribes to the configured topic.
func (t *Transport) Listen(ctx context.Context, handler transport.Handler) error {
	// TODO: Implement MQTT client connection, subscription, and message handling.
	// Recommended library: github.com/eclipse/paho.mqtt.golang
	slog.Info("mqtt transport listening", "broker", t.broker, "topic", t.topic)
	<-ctx.Done()
	return nil
}

// Send publishes a payload to an MQTT topic derived from the target.
func (t *Transport) Send(ctx context.Context, target message.Target, payload []byte) error {
	// TODO: Implement MQTT publish to target endpoint (topic).
	slog.Debug("mqtt send", "target", target.Endpoint, "bytes", len(payload))
	return nil
}

// Close disconnects from the MQTT broker.
func (t *Transport) Close() error {
	// TODO: Disconnect MQTT client.
	return nil
}
