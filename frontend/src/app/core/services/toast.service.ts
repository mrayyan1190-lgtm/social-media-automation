import { Injectable, signal, untracked } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
  tone: 'error' | 'success';
  /** Optional button, e.g. "Reload latest" after a concurrency conflict. */
  action?: { label: string; run: () => void };
}

/** Popups shown above every page (errors and short confirmations). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  error(message: string, action?: Toast['action']): void {
    this.show({ message, tone: 'error', action }, 8000);
  }

  success(message: string): void {
    this.show({ message, tone: 'success' }, 4000);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private show(toast: Omit<Toast, 'id'>, ms: number): void {
    // The same message twice in a row (e.g. a retried request) is shown once. Read untracked: callers run inside effects
    // (problem banners), and tracking the list this method writes would re-run that effect forever and freeze the page.
    const existing = untracked(this.toasts).find((t) => t.message === toast.message);
    if (existing) this.dismiss(existing.id);
    const id = this.nextId++;
    this.toasts.update((list) => [...list.slice(-2), { ...toast, id }]);
    setTimeout(() => this.dismiss(id), ms);
  }
}
