import { useCallback, useState } from "react";
import type {
  DispatchResult,
  Instruction,
  TransportKind,
} from "@/types/switchyard";

export interface UseDispatchReturn {
  result: DispatchResult | null;
  error: string | null;
  isLoading: boolean;
  /** Dispatch audio blob (multipart) or text (JSON) */
  dispatch: (opts: DispatchOpts) => Promise<void>;
  clearResult: () => void;
}

export interface DispatchOpts {
  transport: TransportKind;
  audioBlob?: Blob | null;
  text?: string;
  instruction: Instruction;
}

export function useDispatch(): UseDispatchReturn {
  const [result, setResult] = useState<DispatchResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const dispatch = useCallback(async (opts: DispatchOpts) => {
    setIsLoading(true);
    setError(null);
    setResult(null);

    const endpoint = `/api/dispatch/${opts.transport}`;

    try {
      let response: Response;

      if (opts.audioBlob) {
        // Multipart upload
        const formData = new FormData();
        formData.append("audio", opts.audioBlob, "recording.webm");
        formData.append("instruction", JSON.stringify(opts.instruction));

        response = await fetch(endpoint, {
          method: "POST",
          body: formData,
        });
      } else {
        // JSON body (text-only)
        response = await fetch(endpoint, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            id: crypto.randomUUID(),
            source: "testclient",
            text: opts.text ?? "",
            instruction: opts.instruction,
          }),
        });
      }

      const data = await response.json();

      if (!response.ok) {
        setError(data.error ?? `HTTP ${response.status}`);
        return;
      }

      if (data.error) {
        setError(data.error);
      }

      setResult(data as DispatchResult);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setIsLoading(false);
    }
  }, []);

  const clearResult = useCallback(() => {
    setResult(null);
    setError(null);
  }, []);

  return { result, error, isLoading, dispatch, clearResult };
}
