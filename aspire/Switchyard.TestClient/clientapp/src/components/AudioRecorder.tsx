import { Mic, Square, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Waveform } from "@/components/Waveform";
import type { AudioRecorderState } from "@/hooks/useAudioRecorder";

interface AudioRecorderProps {
  recorder: AudioRecorderState;
}

function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}

export function AudioRecorder({ recorder }: AudioRecorderProps) {
  const {
    isSupported,
    isRecording,
    audioBlob,
    audioUrl,
    duration,
    analyserData,
    start,
    stop,
    clear,
  } = recorder;

  if (!isSupported) {
    return (
      <Card>
        <CardContent className="p-6 text-center text-muted-foreground">
          Your browser does not support audio recording.
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <Mic className="h-4 w-4" />
          Audio Input
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Waveform visualiser */}
        <div className="flex items-center justify-center rounded-lg bg-muted/50 p-3">
          {isRecording ? (
            <div className="flex flex-col items-center gap-2 w-full">
              <Waveform
                data={analyserData}
                width={400}
                height={60}
                color="#ef4444"
                className="w-full max-w-md"
              />
              <span className="text-sm font-mono text-red-400 animate-pulse">
                Recording {formatDuration(duration)}
              </span>
            </div>
          ) : audioUrl ? (
            <div className="flex flex-col items-center gap-2 w-full">
              <audio
                controls
                src={audioUrl}
                className="w-full max-w-md"
              />
              <span className="text-xs text-muted-foreground">
                {audioBlob
                  ? `${(audioBlob.size / 1024).toFixed(1)} KB · ${audioBlob.type}`
                  : ""}
              </span>
            </div>
          ) : (
            <span className="text-sm text-muted-foreground py-4">
              Click Record to capture audio from your microphone
            </span>
          )}
        </div>

        {/* Controls */}
        <div className="flex items-center gap-2">
          {!isRecording ? (
            <Button
              onClick={() => void start()}
              variant="default"
              className="bg-red-600 hover:bg-red-700"
            >
              <Mic className="h-4 w-4" />
              Record
            </Button>
          ) : (
            <Button onClick={stop} variant="destructive">
              <Square className="h-4 w-4" />
              Stop
            </Button>
          )}
          {audioBlob && !isRecording && (
            <Button onClick={clear} variant="outline" size="sm">
              <Trash2 className="h-4 w-4" />
              Clear
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
