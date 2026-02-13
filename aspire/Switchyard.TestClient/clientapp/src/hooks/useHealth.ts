import { useCallback, useEffect, useState } from "react";
import type { HealthStatus } from "@/types/switchyard";

export function useHealth(intervalMs = 10_000): HealthStatus {
  const [status, setStatus] = useState<HealthStatus>({ status: "unreachable" });

  const check = useCallback(async () => {
    try {
      const resp = await fetch("/api/switchyard/health");
      const data: HealthStatus = await resp.json();
      setStatus(data);
    } catch {
      setStatus({ status: "unreachable", error: "Fetch failed" });
    }
  }, []);

  useEffect(() => {
    void check();
    const id = setInterval(() => void check(), intervalMs);
    return () => clearInterval(id);
  }, [check, intervalMs]);

  return status;
}
