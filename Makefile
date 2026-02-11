# ==============================================================================
# Switchyard Makefile
# ==============================================================================

BINARY     := switchyard
MODULE     := github.com/nadzzz/switchyard
VERSION    ?= $(shell git describe --tags --always --dirty 2>/dev/null || echo "dev")
BUILD_DIR  := bin
GO         := go
GOFLAGS    := -trimpath
LDFLAGS    := -s -w -X main.version=$(VERSION)

# Detect OS for binary extension
ifeq ($(OS),Windows_NT)
    EXE := .exe
else
    EXE :=
endif

.PHONY: all build run test lint proto aspire docker-build docker-run docker-stop clean help

all: build ## Default target: build the binary

# ------------------------------------------------------------------------------
# Build & Run
# ------------------------------------------------------------------------------

build: ## Build the switchyard binary
	$(GO) build $(GOFLAGS) -ldflags '$(LDFLAGS)' -o $(BUILD_DIR)/$(BINARY)$(EXE) ./cmd/switchyard

run: ## Run switchyard directly with go run
	$(GO) run ./cmd/switchyard

# ------------------------------------------------------------------------------
# Testing & Linting
# ------------------------------------------------------------------------------

test: ## Run all tests with race detection
	$(GO) test -race -coverprofile=coverage.out ./...

test-integration: ## Run integration tests (requires running services)
	$(GO) test -race -tags=integration -coverprofile=coverage.out ./...

lint: ## Run golangci-lint
	golangci-lint run ./...

# ------------------------------------------------------------------------------
# Code Generation
# ------------------------------------------------------------------------------

proto: ## Generate Go code from protobuf definitions
	protoc \
		--go_out=. --go_opt=paths=source_relative \
		--go-grpc_out=. --go-grpc_opt=paths=source_relative \
		api/proto/switchyard.proto

swagger: ## Regenerate OpenAPI/Swagger docs (requires swag: go install github.com/swaggo/swag/cmd/swag@latest)
	swag init -g cmd/switchyard/main.go -o docs --parseDependency --parseInternal
	@echo "Swagger UI: http://localhost:8080/swagger/index.html"

# ------------------------------------------------------------------------------
# Aspire (local dev orchestration)
# ------------------------------------------------------------------------------

aspire: ## Start the Aspire AppHost (requires .NET 9 SDK)
	dotnet run --project aspire/Switchyard.AppHost

# ------------------------------------------------------------------------------
# Docker
# ------------------------------------------------------------------------------

docker-build: ## Build the Docker image
	docker build -f build/Dockerfile -t switchyard:$(VERSION) -t switchyard:latest .

docker-run: ## Start switchyard with Docker Compose
	docker compose up -d

docker-stop: ## Stop Docker Compose services
	docker compose down

# ------------------------------------------------------------------------------
# Housekeeping
# ------------------------------------------------------------------------------

clean: ## Remove build artifacts
	rm -rf $(BUILD_DIR) coverage.out

help: ## Show this help message
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'
