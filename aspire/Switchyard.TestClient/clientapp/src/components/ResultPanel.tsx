import { useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  MessageSquare,
  Languages,
  Terminal,
  Route,
  Code2,
  AlertCircle,
  Volume2,
} from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Button } from "@/components/ui/button";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { AudioPlayer } from "@/components/AudioPlayer";
import type { DispatchResult } from "@/types/switchyard";

interface ResultPanelProps {
  result: DispatchResult | null;
  error: string | null;
}

export function ResultPanel({ result, error }: ResultPanelProps) {
  const [showRaw, setShowRaw] = useState(false);

  if (!result && !error) {
    return (
      <Card className="border-dashed">
        <CardContent className="flex items-center justify-center p-12 text-muted-foreground">
          <span className="text-sm">
            Send a dispatch to see results here
          </span>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-center justify-between">
          <CardTitle className="flex items-center gap-2 text-base">
            <Terminal className="h-4 w-4" />
            Result
          </CardTitle>
          {result?.message_id && (
            <Badge variant="outline" className="font-mono text-xs">
              {result.message_id.slice(0, 8)}…
            </Badge>
          )}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Error */}
        {error && (
          <div className="flex items-start gap-2 rounded-lg border border-destructive/50 bg-destructive/10 p-3">
            <AlertCircle className="h-4 w-4 text-destructive mt-0.5 shrink-0" />
            <span className="text-sm text-destructive">{error}</span>
          </div>
        )}

        {result && (
          <>
            {/* Transcript */}
            {result.transcript && (
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <MessageSquare className="h-4 w-4 text-muted-foreground" />
                  <span className="text-xs font-medium text-muted-foreground">
                    Transcript
                  </span>
                  {result.language && (
                    <Badge variant="secondary" className="text-xs">
                      <Languages className="h-3 w-3 mr-1" />
                      {result.language}
                    </Badge>
                  )}
                </div>
                <p className="text-sm leading-relaxed pl-6">
                  {result.transcript}
                </p>
              </div>
            )}

            {/* Response text */}
            {result.response_text && (
              <>
                <Separator />
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <MessageSquare className="h-4 w-4 text-cyan-500" />
                    <span className="text-xs font-medium text-muted-foreground">
                      Response
                    </span>
                  </div>
                  <p className="text-sm leading-relaxed pl-6">
                    {result.response_text}
                  </p>
                </div>
              </>
            )}

            {/* Response audio */}
            {result.response_audio && (
              <>
                <Separator />
                <div className="space-y-2">
                  <div className="flex items-center gap-2">
                    <Volume2 className="h-4 w-4 text-cyan-500" />
                    <span className="text-xs font-medium text-muted-foreground">
                      Response Audio
                    </span>
                    {result.response_content_type && (
                      <Badge variant="outline" className="text-xs">
                        {result.response_content_type}
                      </Badge>
                    )}
                  </div>
                  <div className="pl-6">
                    <AudioPlayer
                      audioBase64={result.response_audio}
                      contentType={result.response_content_type}
                      autoPlay
                    />
                  </div>
                </div>
              </>
            )}

            {/* Commands */}
            {result.commands && result.commands.length > 0 && (
              <>
                <Separator />
                <div className="space-y-2">
                  <div className="flex items-center gap-2">
                    <Code2 className="h-4 w-4 text-muted-foreground" />
                    <span className="text-xs font-medium text-muted-foreground">
                      Commands ({result.commands.length})
                    </span>
                  </div>
                  <div className="space-y-2 pl-6">
                    {result.commands.map((cmd, i) => (
                      <CommandItem key={i} action={cmd.action} raw={cmd.raw} params={cmd.params} />
                    ))}
                  </div>
                </div>
              </>
            )}

            {/* Routed to */}
            {result.routed_to && result.routed_to.length > 0 && (
              <>
                <Separator />
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <Route className="h-4 w-4 text-muted-foreground" />
                    <span className="text-xs font-medium text-muted-foreground">
                      Routed To
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-1.5 pl-6">
                    {result.routed_to.map((r, i) => (
                      <Badge key={i} variant="secondary" className="text-xs">
                        {r}
                      </Badge>
                    ))}
                  </div>
                </div>
              </>
            )}

            {/* Raw JSON toggle */}
            <Separator />
            <div>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setShowRaw(!showRaw)}
                className="text-xs text-muted-foreground"
              >
                {showRaw ? "Hide" : "Show"} Raw JSON
              </Button>
              {showRaw && (
                <pre className="mt-2 max-h-80 overflow-auto rounded-lg bg-muted p-3 text-xs font-mono">
                  {JSON.stringify(result, null, 2)}
                </pre>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

// ---

function CommandItem({
  action,
  raw,
  params,
}: {
  action: string;
  raw?: string;
  params?: Record<string, unknown>;
}) {
  const [open, setOpen] = useState(false);
  const jsonContent = raw ?? (params ? JSON.stringify(params, null, 2) : null);

  return (
    <Collapsible open={open} onOpenChange={setOpen}>
      <CollapsibleTrigger asChild>
        <button className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm hover:bg-accent transition-colors">
          {open ? (
            <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
          ) : (
            <ChevronRight className="h-3.5 w-3.5 text-muted-foreground" />
          )}
          <Badge variant="default" className="font-mono text-xs">
            {action}
          </Badge>
        </button>
      </CollapsibleTrigger>
      {jsonContent && (
        <CollapsibleContent>
          <pre className="ml-6 mt-1 rounded-md bg-muted p-2 text-xs font-mono overflow-auto max-h-40">
            {typeof jsonContent === "string"
              ? tryFormatJson(jsonContent)
              : JSON.stringify(jsonContent, null, 2)}
          </pre>
        </CollapsibleContent>
      )}
    </Collapsible>
  );
}

function tryFormatJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
