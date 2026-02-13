import { Activity } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { HealthStatus } from "@/types/switchyard";

interface HeaderProps {
  health: HealthStatus;
}

export function Header({ health }: HeaderProps) {
  const variant =
    health.status === "healthy"
      ? "success"
      : health.status === "unhealthy"
        ? "destructive"
        : "secondary";

  return (
    <header className="border-b bg-card">
      <div className="mx-auto flex h-14 max-w-5xl items-center justify-between px-4">
        <div className="flex items-center gap-3">
          <Activity className="h-5 w-5 text-cyan-500" />
          <h1 className="text-lg font-bold tracking-tight">
            Switchyard
            <span className="ml-1.5 text-sm font-normal text-muted-foreground">
              Test Client
            </span>
          </h1>
        </div>
        <Badge variant={variant} className="capitalize">
          <span
            className={`mr-1.5 inline-block h-2 w-2 rounded-full ${
              health.status === "healthy"
                ? "bg-emerald-400 animate-pulse"
                : health.status === "unhealthy"
                  ? "bg-red-400"
                  : "bg-gray-400"
            }`}
          />
          {health.status}
        </Badge>
      </div>
    </header>
  );
}
