import { Globe, Server } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { TransportKind } from "@/types/switchyard";

interface TransportSelectorProps {
  value: TransportKind;
  onChange: (transport: TransportKind) => void;
}

export function TransportSelector({
  value,
  onChange,
}: TransportSelectorProps) {
  const options: { id: TransportKind; label: string; icon: React.ReactNode; warning?: string }[] = [
    {
      id: "http",
      label: "HTTP REST",
      icon: <Globe className="h-4 w-4" />,
    },
    {
      id: "grpc",
      label: "gRPC",
      icon: <Server className="h-4 w-4" />,
      warning: "May be unimplemented",
    },
  ];

  return (
    <div className="flex items-center gap-2">
      <span className="text-xs font-medium text-muted-foreground mr-1">
        Transport:
      </span>
      {options.map((opt) => (
        <button
          key={opt.id}
          onClick={() => onChange(opt.id)}
          className={`inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
            value === opt.id
              ? "bg-primary text-primary-foreground shadow"
              : "bg-muted text-muted-foreground hover:bg-accent hover:text-accent-foreground"
          }`}
        >
          {opt.icon}
          {opt.label}
          {opt.warning && value === opt.id && (
            <Badge variant="warning" className="ml-1 text-[10px] px-1.5 py-0">
              !
            </Badge>
          )}
        </button>
      ))}
    </div>
  );
}
