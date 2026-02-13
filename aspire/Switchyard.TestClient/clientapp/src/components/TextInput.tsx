import { Keyboard } from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";

interface TextInputProps {
  value: string;
  onChange: (value: string) => void;
}

export function TextInput({ value, onChange }: TextInputProps) {
  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <Keyboard className="h-4 w-4" />
          Text Input
        </CardTitle>
      </CardHeader>
      <CardContent>
        <Textarea
          placeholder="Type a command instead of recording audio… e.g. 'Turn on the living room lights'"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          rows={3}
        />
      </CardContent>
    </Card>
  );
}
