#!/usr/bin/env bash
# Build switchyard for the current platform.
set -euo pipefail

VERSION="${VERSION:-$(git describe --tags --always --dirty 2>/dev/null || echo dev)}"
BUILD_DIR="bin"

mkdir -p "$BUILD_DIR"

echo "Building switchyard ${VERSION}..."
go build \
    -trimpath \
    -ldflags "-s -w -X main.version=${VERSION}" \
    -o "${BUILD_DIR}/switchyard" \
    ./cmd/switchyard

echo "Built: ${BUILD_DIR}/switchyard"
