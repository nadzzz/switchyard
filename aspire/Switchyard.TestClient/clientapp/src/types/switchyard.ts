/** Mirrors switchyard message.ResponseMode */
export type ResponseMode = "none" | "text" | "audio" | "text+audio";

/** Mirrors switchyard message.Target */
export interface Target {
  service_name: string;
  endpoint: string;
  protocol: "http" | "grpc" | "mqtt";
  format_template: string;
}

/** Mirrors switchyard message.Instruction */
export interface Instruction {
  targets: Target[];
  command_format: string;
  prompt: string;
  response_mode: ResponseMode;
}

/** Mirrors switchyard message.Command */
export interface Command {
  action: string;
  params?: Record<string, unknown>;
  raw?: string;
}

/** Mirrors switchyard message.DispatchResult */
export interface DispatchResult {
  message_id: string;
  transcript: string;
  language: string;
  commands: Command[];
  routed_to: string[];
  response_text: string;
  response_audio: string; // base64
  response_content_type: string;
  error?: string;
}

/** Transport kind for the BFF proxy */
export type TransportKind = "http" | "grpc";

/** BFF dispatch request (JSON body variant) */
export interface DispatchRequest {
  id?: string;
  source?: string;
  audio?: string; // base64
  content_type?: string;
  text?: string;
  instruction?: Instruction;
}

/** Health check response */
export interface HealthStatus {
  status: "healthy" | "unhealthy" | "unreachable";
  error?: string;
}
