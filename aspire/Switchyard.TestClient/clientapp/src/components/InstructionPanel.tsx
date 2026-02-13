import { useState } from "react";
import { Plus, Trash2, Sliders } from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import type { Instruction, Target, ResponseMode } from "@/types/switchyard";

interface InstructionPanelProps {
  instruction: Instruction;
  onChange: (instruction: Instruction) => void;
}

const defaultTarget: Target = {
  service_name: "",
  endpoint: "",
  protocol: "http",
  format_template: "",
};

export function InstructionPanel({
  instruction,
  onChange,
}: InstructionPanelProps) {
  const [expanded, setExpanded] = useState(false);

  const update = <K extends keyof Instruction>(
    key: K,
    value: Instruction[K],
  ) => {
    onChange({ ...instruction, [key]: value });
  };

  const updateTarget = (index: number, field: keyof Target, value: string) => {
    const targets = [...instruction.targets];
    const target = targets[index];
    if (target) {
      targets[index] = { ...target, [field]: value };
      update("targets", targets);
    }
  };

  const addTarget = () => {
    update("targets", [...instruction.targets, { ...defaultTarget }]);
  };

  const removeTarget = (index: number) => {
    const targets = instruction.targets.filter((_, i) => i !== index);
    update("targets", targets);
  };

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-center justify-between">
          <CardTitle className="flex items-center gap-2 text-base">
            <Sliders className="h-4 w-4" />
            Instruction
          </CardTitle>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? "Collapse" : "Expand"}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Response mode */}
        <div className="grid gap-2">
          <label className="text-xs font-medium text-muted-foreground">
            Response Mode
          </label>
          <Select
            value={instruction.response_mode}
            onValueChange={(v: ResponseMode) => update("response_mode", v)}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="text+audio">Text + Audio</SelectItem>
              <SelectItem value="text">Text only</SelectItem>
              <SelectItem value="audio">Audio only</SelectItem>
              <SelectItem value="none">None</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {/* Prompt */}
        <div className="grid gap-2">
          <label className="text-xs font-medium text-muted-foreground">
            System Prompt (optional)
          </label>
          <Textarea
            placeholder="Override the interpreter system prompt…"
            value={instruction.prompt}
            onChange={(e) => update("prompt", e.target.value)}
            rows={2}
          />
        </div>

        {expanded && (
          <>
            <Separator />

            {/* Command format */}
            <div className="grid gap-2">
              <label className="text-xs font-medium text-muted-foreground">
                Command Format (optional)
              </label>
              <Input
                placeholder='e.g. "home_assistant" or leave empty'
                value={instruction.command_format}
                onChange={(e) => update("command_format", e.target.value)}
              />
            </div>

            {/* Targets */}
            <div className="grid gap-2">
              <div className="flex items-center justify-between">
                <label className="text-xs font-medium text-muted-foreground">
                  Targets
                </label>
                <Button variant="outline" size="sm" onClick={addTarget}>
                  <Plus className="h-3 w-3 mr-1" />
                  Add Target
                </Button>
              </div>
              {instruction.targets.map((target, i) => (
                <div
                  key={i}
                  className="grid grid-cols-[1fr_1fr_auto_auto] gap-2 items-end rounded-lg border p-3"
                >
                  <div className="grid gap-1">
                    <span className="text-xs text-muted-foreground">
                      Service
                    </span>
                    <Input
                      placeholder="homeassistant"
                      value={target.service_name}
                      onChange={(e) =>
                        updateTarget(i, "service_name", e.target.value)
                      }
                    />
                  </div>
                  <div className="grid gap-1">
                    <span className="text-xs text-muted-foreground">
                      Endpoint
                    </span>
                    <Input
                      placeholder="http://ha:8123/api"
                      value={target.endpoint}
                      onChange={(e) =>
                        updateTarget(i, "endpoint", e.target.value)
                      }
                    />
                  </div>
                  <div className="grid gap-1">
                    <span className="text-xs text-muted-foreground">
                      Protocol
                    </span>
                    <Select
                      value={target.protocol}
                      onValueChange={(v) => updateTarget(i, "protocol", v)}
                    >
                      <SelectTrigger className="w-24">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="http">HTTP</SelectItem>
                        <SelectItem value="grpc">gRPC</SelectItem>
                        <SelectItem value="mqtt">MQTT</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => removeTarget(i)}
                    className="text-muted-foreground hover:text-destructive"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
