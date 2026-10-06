import { Injectable, signal } from '@angular/core';

export interface TopbarAction {
  label: string;
  disabled: boolean;
  title?: string;
  run: () => void;
}

/** Lets the open page put its primary action in the shell navbar (e.g. the Composer's Submit for Review). */
@Injectable({ providedIn: 'root' })
export class TopbarActionService {
  readonly action = signal<TopbarAction | null>(null);
}
