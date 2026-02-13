import { useState } from "react";
import { Header } from "@/components/Header";
import { AudioRecorder } from "@/components/AudioRecorder";
import { TextInput } from "@/components/TextInput";
import { InstructionPanel } from "@/components/InstructionPanel";
import { TransportSelector } from "@/components/TransportSelector";
import { DispatchButton } from "@/components/DispatchButton";
import { ResultPanel } from "@/components/ResultPanel";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Separator } from "@/components/ui/separator";
import { useAudioRecorder } from "@/hooks/useAudioRecorder";
import { useDispatch } from "@/hooks/useDispatch";
import { useHealth } from "@/hooks/useHealth";
import type { Instruction, TransportKind } from "@/types/switchyard";

const defaultInstruction: Instruction = {
  targets: [],
  command_format: "",
  prompt: "",
  response_mode: "text+audio",
};

export default function App() {
  const health = useHealth();
  const recorder = useAudioRecorder();
  const { result, error, isLoading, dispatch, clearResult } = useDispatch();

  const [text, setText] = useState("");
  const [transport, setTransport] = useState<TransportKind>("http");
  const [instruction, setInstruction] = useState<Instruction>(defaultInstruction);
  const [inputMode, setInputMode] = useState<"audio" | "text">("audio");

  const canDispatch =
    (inputMode === "audio" && !!recorder.audioBlob) ||
    (inputMode === "text" && text.trim().length > 0);

  const handleDispatch = () => {
    clearResult();
    void dispatch({
      transport,
      audioBlob: inputMode === "audio" ? recorder.audioBlob : undefined,
      text: inputMode === "text" ? text : undefined,
      instruction,
    });
  };

  return (
    <div className="min-h-screen bg-background">
      <Header health={health} />

      <main className="mx-auto max-w-5xl px-4 py-6">
        <div className="grid gap-6 lg:grid-cols-[1fr_1fr]">
          {/* ---- Left column: Input ---- */}
          <div className="space-y-4">
            {/* Transport selector */}
            <TransportSelector value={transport} onChange={setTransport} />

            {/* Audio / Text tabs */}
            <Tabs
              value={inputMode}
              onValueChange={(v) => setInputMode(v as "audio" | "text")}
            >
              <TabsList className="w-full">
                <TabsTrigger value="audio" className="flex-1">
                  Audio
                </TabsTrigger>
                <TabsTrigger value="text" className="flex-1">
                  Text
                </TabsTrigger>
              </TabsList>
              <TabsContent value="audio">
                <AudioRecorder recorder={recorder} />
              </TabsContent>
              <TabsContent value="text">
                <TextInput value={text} onChange={setText} />
              </TabsContent>
            </Tabs>

            {/* Instruction config */}
            <InstructionPanel
              instruction={instruction}
              onChange={setInstruction}
            />

            <Separator />

            {/* Dispatch */}
            <DispatchButton
              isLoading={isLoading}
              disabled={!canDispatch}
              onClick={handleDispatch}
            />
          </div>

          {/* ---- Right column: Result ---- */}
          <div>
            <ResultPanel result={result} error={error} />
          </div>
        </div>
      </main>

      <footer className="border-t py-4 text-center text-xs text-muted-foreground">
        Switchyard Test Client &middot; HTTP REST &amp; gRPC
      </footer>
    </div>
  );
}
