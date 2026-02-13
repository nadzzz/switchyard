import { useEffect, useRef, useState, useCallback } from "react";
import { Play, Pause, Volume2 } from "lucide-react";
import { Button } from "@/components/ui/button";

interface AudioPlayerProps {
  /** Base64-encoded audio (from DispatchResult.response_audio) */
  audioBase64: string;
  /** MIME content type (from DispatchResult.response_content_type) */
  contentType: string;
  /** Auto-play on mount */
  autoPlay?: boolean;
}

export function AudioPlayer({
  audioBase64,
  contentType,
  autoPlay = false,
}: AudioPlayerProps) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [duration, setDuration] = useState(0);
  const [audioUrl, setAudioUrl] = useState<string | null>(null);

  // Convert base64 → blob URL
  useEffect(() => {
    if (!audioBase64) return;
    try {
      const binary = atob(audioBase64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
      }
      const blob = new Blob([bytes], { type: contentType || "audio/wav" });
      const url = URL.createObjectURL(blob);
      setAudioUrl(url);
      return () => URL.revokeObjectURL(url);
    } catch {
      setAudioUrl(null);
    }
  }, [audioBase64, contentType]);

  // Auto-play
  useEffect(() => {
    if (autoPlay && audioUrl && audioRef.current) {
      void audioRef.current.play();
    }
  }, [autoPlay, audioUrl]);

  const togglePlay = useCallback(() => {
    const el = audioRef.current;
    if (!el) return;
    if (el.paused) {
      void el.play();
    } else {
      el.pause();
    }
  }, []);

  if (!audioUrl) return null;

  return (
    <div className="flex items-center gap-3 rounded-lg bg-muted/50 p-3">
      <Button
        variant="ghost"
        size="icon"
        onClick={togglePlay}
        className="shrink-0"
      >
        {isPlaying ? (
          <Pause className="h-5 w-5" />
        ) : (
          <Play className="h-5 w-5" />
        )}
      </Button>

      {/* Progress bar */}
      <div className="flex-1 space-y-1">
        <div className="h-1.5 w-full rounded-full bg-muted">
          <div
            className="h-full rounded-full bg-cyan-500 transition-all"
            style={{ width: `${duration > 0 ? (progress / duration) * 100 : 0}%` }}
          />
        </div>
        <div className="flex justify-between text-xs text-muted-foreground">
          <span>{formatTime(progress)}</span>
          <span>{formatTime(duration)}</span>
        </div>
      </div>

      <Volume2 className="h-4 w-4 text-muted-foreground shrink-0" />

      <audio
        ref={audioRef}
        src={audioUrl}
        onPlay={() => setIsPlaying(true)}
        onPause={() => setIsPlaying(false)}
        onEnded={() => setIsPlaying(false)}
        onTimeUpdate={(e) => setProgress(e.currentTarget.currentTime)}
        onLoadedMetadata={(e) => setDuration(e.currentTarget.duration)}
        className="hidden"
      />
    </div>
  );
}

function formatTime(seconds: number): string {
  if (!isFinite(seconds)) return "0:00";
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}
