import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Early feedback for absolute http(s) URLs. The API applies the authoritative rule. Empty is allowed. */
export function httpUrlValidator(control: AbstractControl<string | null>): ValidationErrors | null {
  const value = control.value?.trim();
  if (!value) {
    return null;
  }
  try {
    const url = new URL(value);
    const ok = (url.protocol === 'https:' || url.protocol === 'http:') && !!url.hostname && !url.username && !url.password;
    return ok ? null : { httpUrl: true };
  } catch {
    return { httpUrl: true };
  }
}
