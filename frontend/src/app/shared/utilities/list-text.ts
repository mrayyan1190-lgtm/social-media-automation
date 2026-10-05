/** Splits a textarea value into items: one per line or comma-separated. The API does the authoritative cleanup. */
export function toList(text: string): string[] {
  return text
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

export function fromList(items: readonly string[] | null | undefined): string {
  return (items ?? []).join('\n');
}
