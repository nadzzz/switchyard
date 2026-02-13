import { useCallback, useEffect, useRef, useState } from "react";

export interface AudioRecorderState {
  /** Whether the browser supports MediaRecorder */
  isSupported: boolean;
  /** Currently recording */
  isRecording: boolean;
  /** Recorded audio blob (after stop) */
  audioBlob: Blob | null;
  /** URL for playback of recorded audio */
  audioUrl: string | null;
  /** Duration in seconds while recording */
  duration: number;
  /** Live analyser data for waveform visualisation (128 bins) */
  analyserData: Uint8Array;
  /** Start recording via microphone */
  start: () => Promise<void>;
  /** Stop recording */
  stop: () => void;
  /** Clear the recorded audio */
  clear: () => void;
}

export function useAudioRecorder(): AudioRecorderState {
  const [isSupported] = useState(
    () => typeof navigator !== "undefined" && !!navigator.mediaDevices?.getUserMedia && !!window.MediaRecorder,
  );
  const [isRecording, setIsRecording] = useState(false);
  const [audioBlob, setAudioBlob] = useState<Blob | null>(null);
  const [audioUrl, setAudioUrl] = useState<string | null>(null);
  const [duration, setDuration] = useState(0);
  const [analyserData, setAnalyserData] = useState<Uint8Array>(
    () => new Uint8Array(128),
  );

  const mediaRecorder = useRef<MediaRecorder | null>(null);
  const audioCtx = useRef<AudioContext | null>(null);
  const analyserNode = useRef<AnalyserNode | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunks = useRef<Blob[]>([]);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const rafRef = useRef<number>(0);
  const startTime = useRef(0);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      cancelAnimationFrame(rafRef.current);
      if (timerRef.current) clearInterval(timerRef.current);
      streamRef.current?.getTracks().forEach((t) => t.stop());
      void audioCtx.current?.close();
      if (audioUrl) URL.revokeObjectURL(audioUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const pumpAnalyser = useCallback(() => {
    if (!analyserNode.current) return;
    const buf = new Uint8Array(analyserNode.current.frequencyBinCount);
    analyserNode.current.getByteTimeDomainData(buf);
    setAnalyserData(buf);
    rafRef.current = requestAnimationFrame(pumpAnalyser);
  }, []);

  const start = useCallback(async () => {
    if (!isSupported) return;
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    streamRef.current = stream;

    // Web Audio analyser for waveform
    const ctx = new AudioContext();
    audioCtx.current = ctx;
    const source = ctx.createMediaStreamSource(stream);
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 256;
    source.connect(analyser);
    analyserNode.current = analyser;

    // Prefer webm/opus, fallback to whatever the browser supports
    const mimeType = MediaRecorder.isTypeSupported("audio/webm;codecs=opus")
      ? "audio/webm;codecs=opus"
      : MediaRecorder.isTypeSupported("audio/webm")
        ? "audio/webm"
        : "";

    const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    chunks.current = [];

    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) chunks.current.push(e.data);
    };

    recorder.onstop = () => {
      const blob = new Blob(chunks.current, {
        type: recorder.mimeType || "audio/webm",
      });
      setAudioBlob(blob);
      const url = URL.createObjectURL(blob);
      setAudioUrl(url);
      // Stop stream tracks
      stream.getTracks().forEach((t) => t.stop());
      cancelAnimationFrame(rafRef.current);
      if (timerRef.current) clearInterval(timerRef.current);
    };

    mediaRecorder.current = recorder;
    recorder.start(250); // collect data every 250ms
    setIsRecording(true);
    setAudioBlob(null);
    if (audioUrl) URL.revokeObjectURL(audioUrl);
    setAudioUrl(null);
    setDuration(0);
    startTime.current = Date.now();

    // Duration timer
    timerRef.current = setInterval(() => {
      setDuration((Date.now() - startTime.current) / 1000);
    }, 100);

    // Start analyser pump
    pumpAnalyser();
  }, [isSupported, audioUrl, pumpAnalyser]);

  const stop = useCallback(() => {
    if (mediaRecorder.current?.state === "recording") {
      mediaRecorder.current.stop();
    }
    setIsRecording(false);
    void audioCtx.current?.close();
    audioCtx.current = null;
  }, []);

  const clear = useCallback(() => {
    setAudioBlob(null);
    if (audioUrl) URL.revokeObjectURL(audioUrl);
    setAudioUrl(null);
    setDuration(0);
    chunks.current = [];
  }, [audioUrl]);

  return {
    isSupported,
    isRecording,
    audioBlob,
    audioUrl,
    duration,
    analyserData,
    start,
    stop,
    clear,
  };
}
