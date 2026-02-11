# ==============================================================================
# Stage 1: Build
# ==============================================================================
FROM golang:1.25-alpine AS builder

# Install ca-certificates for HTTPS calls (OpenAI API) and git for version info.
RUN apk add --no-cache ca-certificates git

WORKDIR /src

# Cache dependencies first.
COPY go.mod go.sum ./
RUN go mod download

# Copy source and build.
COPY . .

# Normalise config line endings (Windows CRLF → LF) to avoid YAML parse errors.
RUN find configs -name '*.yaml' -exec sed -i 's/\r$//' {} +

ARG VERSION=dev
RUN CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build \
    -trimpath \
    -ldflags "-s -w -X main.version=${VERSION}" \
    -o /switchyard \
    ./cmd/switchyard

# ==============================================================================
# Stage 2: Runtime
# ==============================================================================
FROM gcr.io/distroless/static-debian12

# Copy the binary and default config.
COPY --from=builder /switchyard /usr/local/bin/switchyard
COPY --from=builder /src/configs/switchyard.yaml /etc/switchyard/switchyard.yaml

# Copy CA certificates for TLS.
COPY --from=builder /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/

# Expose ports: HTTP, Health, gRPC
EXPOSE 8080 8081 50051

ENTRYPOINT ["/usr/local/bin/switchyard"]
