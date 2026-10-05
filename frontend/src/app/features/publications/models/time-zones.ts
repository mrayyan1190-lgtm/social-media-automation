/**
 * Browser-side time zone helpers for the schedule dialog. They only preview and explain; the server resolves the
 * authoritative UTC instant and rejects nonexistent or unresolved ambiguous times itself.
 */

export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
}

export function supportedTimeZones(): string[] {
  const zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  return zones.length ? zones : ['UTC'];
}

/** UTC offset in minutes that `zone` uses at the instant `utcMs`. */
export function offsetAt(utcMs: number, zone: string): number {
  const name = new Intl.DateTimeFormat('en-US', { timeZone: zone, timeZoneName: 'longOffset' })
    .formatToParts(new Date(utcMs))
    .find((p) => p.type === 'timeZoneName')?.value ?? 'GMT';
  const match = /GMT([+-])(\d{2}):(\d{2})/.exec(name);
  return match ? (match[1] === '-' ? -1 : 1) * (Number(match[2]) * 60 + Number(match[3])) : 0;
}

/**
 * Every UTC offset under which the wall-clock time `date`+`time` exists in `zone`: none for a time skipped by a DST
 * jump, two for a time repeated when clocks go back, otherwise one.
 */
export function offsetsFor(date: string, time: string, zone: string): number[] {
  const wallAsUtc = Date.parse(`${date}T${time.slice(0, 5)}:00Z`);
  if (Number.isNaN(wallAsUtc)) return [];
  const day = 24 * 60 * 60 * 1000;
  const candidates = [...new Set([wallAsUtc - day, wallAsUtc, wallAsUtc + day].map((t) => offsetAt(t, zone)))];
  return candidates.filter((o) => offsetAt(wallAsUtc - o * 60_000, zone) === o).sort((a, b) => b - a);
}

export function toUtc(date: string, time: string, offsetMinutes: number): Date {
  return new Date(Date.parse(`${date}T${time.slice(0, 5)}:00Z`) - offsetMinutes * 60_000);
}

export function formatOffset(minutes: number): string {
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  return `UTC${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}

/** The API sends UTC timestamps without a zone designator; mark them as UTC before formatting. */
export function asUtc(timestamp: string): string {
  return /(Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : `${timestamp}Z`;
}

/** Formats a UTC timestamp in a given IANA zone (the workspace zone in lists and the calendar). */
export function formatInZone(utc: string, zone: string, options: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }): string {
  try {
    return new Intl.DateTimeFormat(undefined, { ...options, timeZone: zone }).format(new Date(asUtc(utc)));
  } catch {
    return new Intl.DateTimeFormat(undefined, { ...options, timeZone: 'UTC' }).format(new Date(asUtc(utc))) + ' UTC';
  }
}

/** Calendar day (yyyy-MM-dd) of a UTC timestamp in a zone, for grouping. */
export function dayKeyInZone(utc: string, zone: string): string {
  // en-CA formats dates as yyyy-MM-dd regardless of the user's locale.
  return new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(asUtc(utc)));
}
