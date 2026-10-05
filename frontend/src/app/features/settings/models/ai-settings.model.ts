export type AiProvider = 'OpenAI' | 'Groq' | 'OpenAICompatible';

export const AI_PROVIDERS: { value: AiProvider; label: string }[] = [
  { value: 'OpenAI', label: 'OpenAI' },
  { value: 'Groq', label: 'Groq' },
  { value: 'OpenAICompatible', label: 'Other (OpenAI-compatible endpoint)' },
];

/** What the API returns. The key itself is never sent to the browser; only its last four characters. */
export interface AiSettings {
  provider: AiProvider;
  baseUrl: string;
  model: string;
  apiKeyHint: string;
  updatedAtUtc: string;
  rowVersion: string;
}

export interface SaveAiSettings {
  provider: AiProvider;
  baseUrl: string | null;
  model: string;
  /** Null keeps the stored key (only allowed when provider and endpoint are unchanged). */
  apiKey: string | null;
}
