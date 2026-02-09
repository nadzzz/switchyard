# whisper.cpp server — built from source for multi-arch (amd64 + arm64).
#
# Default model: ggml-base.en-q5_1 (~57 MB, English-only, quantized)
# Override at build time: docker build --build-arg MODEL=small.en-q5_1 ...
#
# The server exposes an OpenAI-compatible /inference endpoint on port 8080.

# ── Stage 1: build whisper.cpp from source ──────────────────────────
FROM debian:bookworm-slim AS builder

RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential cmake git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 https://github.com/ggml-org/whisper.cpp.git /src

WORKDIR /src
RUN cmake -B build -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_EXAMPLES=ON \
    && cmake --build build --target whisper-server -j$(nproc)

# ── Stage 2: download model ─────────────────────────────────────────
FROM debian:bookworm-slim AS model

ARG MODEL=base.en-q5_1

RUN apt-get update && apt-get install -y --no-install-recommends wget ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /models \
    && wget -q -O /models/ggml-${MODEL}.bin \
       "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-${MODEL}.bin"

# ── Stage 3: minimal runtime ────────────────────────────────────────
FROM debian:bookworm-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /src/build/bin/whisper-server /usr/local/bin/whisper-server
COPY --from=model   /models /models

EXPOSE 8080

ENTRYPOINT ["whisper-server"]
CMD ["--model", "/models/ggml-base.en-q5_1.bin", "--host", "0.0.0.0", "--port", "8080", "--convert"]
