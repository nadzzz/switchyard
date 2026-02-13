import { Send, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";

interface DispatchButtonProps {
  isLoading: boolean;
  disabled: boolean;
  onClick: () => void;
}

export function DispatchButton({
  isLoading,
  disabled,
  onClick,
}: DispatchButtonProps) {
  return (
    <Button
      onClick={onClick}
      disabled={disabled || isLoading}
      size="lg"
      className="w-full bg-cyan-600 hover:bg-cyan-700 text-white font-semibold"
    >
      {isLoading ? (
        <>
          <Loader2 className="h-5 w-5 animate-spin" />
          Dispatching…
        </>
      ) : (
        <>
          <Send className="h-5 w-5" />
          Dispatch
        </>
      )}
    </Button>
  );
}
