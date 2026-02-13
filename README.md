# Switchyard

**Switchyard is a voice-first message dispatch daemon that interprets audio inputs and routes structured commands to target services.**

*Named after the railway facility where trains are sorted and redirected — switchyard does the same for voice commands.*

```
                         ┌──────────────┐
                         │   Whisper /   │
                         │  Local STT    │
                         └──────┬───────┘
                                │ transcript
┌──────────┐   audio    ┌──────┴───────┐   commands    ┌────────────────┐
│  Robot    │──────────▶│              │──────────────▶│ Home Assistant │
│  Phone   │           │  SWITCHYARD  │               ├────────────────┤
│  IoT     │◀──────────│              │──────────────▶│ Robot Arm      │
└──────────┘  response  └──────┬───────┘               ├────────────────┤
                                │                       │ Any Service    │
                         ┌──────┴───────┐              └────────────────┘
                         │   GPT-4o /   │
                         │  Local LLM   │
                         └──────────────┘
```

## Features

- **Voice-first** — Send audio from any device; switchyard handles transcription and interpretation
- **Pluggable transports** — gRPC, HTTP/WebSocket, and MQTT adapters; add your own by implementing one interface
- **Pluggable LLM backends** — OpenAI (Whisper + GPT) or self-hosted (whisper.cpp + Ollama/vLLM)
- **Always-reply-to-sender** — Architectural invariant: the original sender always gets the response, in addition to any target services
- **Structured command output** — Responses are typed JSON commands, not free-text; format is defined by the sender's instruction
- **Aspire integration** — Full .NET Aspire AppHost for local development orchestration with dashboard, logs, and health monitoring
- **Docker-ready** — Multi-stage Dockerfile and Compose for production deployment

## Quick Start

### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| [Go](https://go.dev/dl/) | 1.23+ | Build and run switchyard |
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0+ | Aspire local dev orchestration |
| [Docker](https://docs.docker.com/get-docker/) | 24+ | Production deployment |

### Clone and configure

```bash
git clone https://github.com/nadzzz/switchyard.git
cd switchyard

# Set up environment variables
cp configs/.env.example .env
# Edit .env and fill in your API keys
```

### Run locally (Go directly)

```bash
# Build
go build -o bin/switchyard ./cmd/switchyard

# Run
./bin/switchyard
# or: go run ./cmd/switchyard
```

### Run with Aspire (recommended for development)

```bash
# Start the Aspire AppHost (launches switchyard + dashboard)
dotnet run --project aspire/Switchyard.AppHost

# Open the Aspire dashboard (URL printed in terminal, typically https://localhost:15888)
```

In VS Code, press **F5** and select the **"Aspire AppHost"** launch configuration.

### Deploy with Docker

```bash
# Build the image
docker build -f build/Dockerfile -t switchyard:latest .

# Run with Docker Compose
docker compose up -d

# Verify health
curl http://localhost:8081/healthz
```

## Configuration

Switchyard reads configuration from (in order of precedence):

1. **Environment variables** — prefixed with `SWITCHYARD_` (e.g., `SWITCHYARD_LOGGING_LEVEL=debug`)
2. **Config file** — `./switchyard.yaml`, `./configs/switchyard.yaml`, or `/etc/switchyard/switchyard.yaml`
3. **Defaults** — sensible defaults for all settings

See [`configs/switchyard.yaml`](configs/switchyard.yaml) for the full reference with comments.

### Key environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `OPENAI_API_KEY` | — | OpenAI API key (required if backend=openai) |
| `HA_TOKEN` | — | Home Assistant long-lived access token |
| `SWITCHYARD_INTERPRETER_BACKEND` | `openai` | `openai` or `local` |
| `SWITCHYARD_LOGGING_LEVEL` | `info` | `debug`, `info`, `warn`, `error` |
| `SWITCHYARD_TRANSPORTS_HTTP_PORT` | `8080` | HTTP transport port |
| `SWITCHYARD_TRANSPORTS_GRPC_PORT` | `50051` | gRPC transport port |
| `SWITCHYARD_SERVER_HEALTH_PORT` | `8081` | Health check endpoint port |

## Architecture

```
cmd/switchyard/          → Daemon entrypoint (main.go)
internal/
├── config/              → Viper-based configuration loading
├── dispatch/            → Core routing engine (message → interpret → route)
├── health/              → HTTP /healthz endpoint
├── interpreter/         → LLM interface + backends
│   ├── openai/          →   OpenAI Whisper + GPT-4o
│   └── local/           →   Self-hosted (whisper.cpp + Ollama)
├── message/             → Core data types (Message, Command, Instruction)
└── transport/           → Transport interface + adapters
    ├── grpc/            →   gRPC server/client
    ├── http/            →   REST + WebSocket
    └── mqtt/            →   MQTT pub/sub
api/proto/               → gRPC service definition (protobuf)
configs/                 → Default config files
aspire/                  → .NET Aspire AppHost for dev orchestration
build/                   → Dockerfile
scripts/                 → Build scripts (bash + PowerShell)
```

### Message flow

1. A **client** (robot, phone, IoT device) sends audio + an `Instruction` via any transport
2. The **dispatcher** passes the audio to the **interpreter** for transcription (Whisper)
3. The transcript + instruction go to the **interpreter** for command generation (GPT/LLM)
4. The resulting `Command[]` are routed to all **targets** specified in the instruction
5. The response is **always** sent back to the original sender

### Key interfaces

- **`transport.Transport`** — `Listen()`, `Send()`, `Close()` — implement to add a new transport
- **`interpreter.Interpreter`** — `Transcribe()`, `Interpret()`, `Close()` — implement to add a new LLM backend

## API

### HTTP

```bash
# Send audio for dispatch (text+audio response by default when TTS is enabled)
curl -X POST http://localhost:8080/dispatch \
  -H "Content-Type: audio/wav" \
  -H "X-Switchyard-Source: my-phone" \
  -H 'X-Switchyard-Instruction: {"command_format":"homeassistant","response_mode":"text","targets":[{"service_name":"homeassistant","endpoint":"http://ha.local:8123/api/services","protocol":"http"}]}' \
  --data-binary @recording.wav

# Send JSON message with voice response
curl -X POST http://localhost:8080/dispatch \
  -H "Content-Type: application/json" \
  -d '{
    "source": "my-phone",
    "text": "Turn on the living room lights",
    "instruction": {
      "command_format": "homeassistant",
      "response_mode": "text+audio",
      "targets": [{"service_name": "homeassistant", "endpoint": "http://ha.local:8123/api/services", "protocol": "http"}]
    }
  }'

# Pure conversation (no command dispatch)
curl -X POST http://localhost:8080/dispatch \
  -H "Content-Type: application/json" \
  -d '{
    "source": "my-phone",
    "text": "What is the weather like?",
    "instruction": {
      "response_mode": "text"
    }
  }'
```

### gRPC

See [`api/proto/switchyard.proto`](api/proto/switchyard.proto) for the full service definition.

### Health

```bash
curl http://localhost:8081/healthz    # Liveness
curl http://localhost:8081/readyz     # Readiness
```

## Building

```bash
# Using Make
make build          # Build binary to bin/switchyard
make test           # Run tests with race detection
make lint           # Run golangci-lint
make proto          # Generate Go code from protobuf
make docker-build   # Build Docker image
make help           # Show all targets

# Using Go directly
go build -o bin/switchyard ./cmd/switchyard

# Using scripts
./scripts/build.sh            # Linux/macOS
.\scripts\build.ps1           # Windows
```

## Running with Aspire in VS Code

1. Install the recommended VS Code extensions (prompted on first open, or see `.vscode/extensions.json`)
2. Copy `configs/.env.example` to `.env` at the project root
3. Fill in your `OPENAI_API_KEY` (and optionally `HA_TOKEN`)
4. Set Aspire user secrets for sensitive values:
   ```bash
   cd aspire/Switchyard.AppHost
   dotnet user-secrets set "OPENAI_API_KEY" "sk-your-key"
   ```
5. Press **F5** → select **"Aspire AppHost"**
6. The Aspire dashboard opens in your browser with switchyard running, logs streaming, and health status visible

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute.

## License

[Apache License 2.0](LICENSE)
