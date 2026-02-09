# Contributing to Switchyard

Thank you for your interest in contributing to Switchyard! This document provides guidelines and instructions for contributing.

## Getting Started

1. **Fork** the repository on GitHub
2. **Clone** your fork locally:
   ```bash
   git clone https://github.com/<your-username>/switchyard.git
   cd switchyard
   ```
3. **Install dependencies**:
   ```bash
   go mod tidy
   ```
4. **Set up environment**:
   ```bash
   cp configs/.env.example .env
   # Edit .env with your API keys
   ```
5. **Build and verify**:
   ```bash
   make build
   make test
   ```

## Development Environment

### Required tools

| Tool | Version | Install |
|------|---------|---------|
| Go | 1.23+ | [go.dev/dl](https://go.dev/dl/) |
| .NET SDK | 9.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| golangci-lint | latest | `go install github.com/golangci/golangci-lint/cmd/golangci-lint@latest` |
| protoc | 3.x+ | [github.com/protocolbuffers/protobuf](https://github.com/protocolbuffers/protobuf/releases) |

### VS Code setup

Open the project in VS Code. You'll be prompted to install recommended extensions:

- **Go** (`golang.go`) — Go language support, debugging with Delve
- **C# Dev Kit** (`ms-dotnettools.csdevkit`) — .NET/Aspire support
- **.NET Aspire** (`ms-dotnettools.dotnet-aspire`) — Aspire dashboard integration
- **vscode-proto3** (`zxh404.vscode-proto3`) — Protobuf syntax highlighting
- **Docker** (`ms-azuretools.vscode-docker`) — Dockerfile and Compose support

### Running locally

```bash
# Go directly
make run

# With Aspire (recommended)
make aspire
# or press F5 → "Aspire AppHost" in VS Code
```

## Code Style

- **Format**: Always run `gofmt` (handled automatically by the Go VS Code extension on save)
- **Lint**: Run `make lint` before committing — all code must pass `golangci-lint`
- **No `panic()` in production code** — return errors up the call stack
- **Error wrapping**: Use `fmt.Errorf("context: %w", err)` to preserve error chains
- **Logging**: Use `log/slog` (stdlib) — no third-party logging libraries
- **Naming**: Follow [Go naming conventions](https://go.dev/doc/effective_go#names) — exported names are PascalCase, unexported are camelCase

## Branching Strategy

| Branch | Purpose |
|--------|---------|
| `main` | Stable, release-ready code |
| `feat/<name>` | New features (e.g., `feat/websocket-streaming`) |
| `fix/<name>` | Bug fixes (e.g., `fix/transcription-timeout`) |
| `docs/<name>` | Documentation changes |
| `refactor/<name>` | Code restructuring without behavior changes |

## Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <description>

[optional body]
```

**Types**: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `ci`

**Scopes**: `transport`, `interpreter`, `dispatch`, `config`, `health`, `proto`, `aspire`, `docker`

**Examples**:
```
feat(transport): add WebSocket streaming support
fix(interpreter): handle empty transcription response
docs(readme): add MQTT configuration example
refactor(dispatch): extract target routing into separate function
test(interpreter): add unit tests for command parsing
```

## Pull Requests

1. Create a feature branch from `main`
2. Make your changes with clear, atomic commits
3. Ensure all checks pass:
   ```bash
   make test
   make lint
   ```
4. Push your branch and open a PR against `main`
5. Write a clear description of what changed and why
6. Link any related issues

### PR checklist

- [ ] Code compiles without warnings
- [ ] All tests pass (`make test`)
- [ ] Linter is clean (`make lint`)
- [ ] New code has appropriate test coverage
- [ ] Documentation updated if needed
- [ ] Commit messages follow Conventional Commits

## Testing

```bash
# Run all unit tests
make test

# Run with verbose output
go test -v -race ./...

# Run a specific package
go test -v ./internal/dispatch/...

# Run integration tests (requires running services)
make test-integration
```

### Test conventions

- Unit tests live alongside the code they test (`*_test.go`)
- Integration tests are tagged: `//go:build integration`
- Use table-driven tests where appropriate
- Mock external services (OpenAI, Home Assistant) in unit tests

## Adding a New Transport

1. Create a new package under `internal/transport/<name>/`
2. Implement the `transport.Transport` interface:
   ```go
   type Transport interface {
       Name() string
       Listen(ctx context.Context, handler Handler) error
       Send(ctx context.Context, target message.Target, payload []byte) error
       Close() error
   }
   ```
3. Add configuration fields in `internal/config/config.go`
4. Register the transport in `cmd/switchyard/main.go`
5. Add tests
6. Update `configs/switchyard.yaml` with the new transport's settings
7. Update `README.md` with documentation

## Adding a New Interpreter Backend

1. Create a new package under `internal/interpreter/<name>/`
2. Implement the `interpreter.Interpreter` interface:
   ```go
   type Interpreter interface {
       Name() string
       Transcribe(ctx context.Context, audio []byte, contentType string, opts TranscribeOpts) (string, error)
       Interpret(ctx context.Context, text string, instruction message.Instruction) ([]message.Command, error)
       Close() error
   }
   ```
3. Add configuration fields in `internal/config/config.go`
4. Register the backend in `cmd/switchyard/main.go`
5. Add tests
6. Update the config and docs

## License

By contributing, you agree that your contributions will be licensed under the [Apache License 2.0](LICENSE).
