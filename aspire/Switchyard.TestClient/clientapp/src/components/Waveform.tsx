import { useRef, useEffect, useCallback, memo } from "react";

interface WaveformProps {
  data: Uint8Array;
  width?: number;
  height?: number;
  color?: string;
  className?: string;
}

/** Renders a real-time audio waveform on a <canvas> element. */
export const Waveform = memo(function Waveform({
  data,
  width = 300,
  height = 60,
  color = "#22d3ee",
  className,
}: WaveformProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const dpr = window.devicePixelRatio || 1;
    canvas.width = width * dpr;
    canvas.height = height * dpr;
    ctx.scale(dpr, dpr);

    ctx.clearRect(0, 0, width, height);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = color;
    ctx.beginPath();

    const sliceWidth = width / data.length;
    let x = 0;

    for (let i = 0; i < data.length; i++) {
      const v = (data[i] ?? 128) / 128.0;
      const y = (v * height) / 2;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
      x += sliceWidth;
    }

    ctx.lineTo(width, height / 2);
    ctx.stroke();
  }, [data, width, height, color]);

  useEffect(() => {
    draw();
  }, [draw]);

  return (
    <canvas
      ref={canvasRef}
      className={className}
      style={{ width, height }}
    />
  );
});
