import { Component, effect, inject, input } from '@angular/core';
import { ToastService } from '../../core/services/toast.service';

/** Shows a page's confirmation (or a local error) as a popup instead of an inline box, like app-problem-banner does for API errors. */
@Component({
  selector: 'app-notice',
  template: ``,
})
export class NoticeComponent {
  readonly message = input<string | null>(null);
  readonly tone = input<'success' | 'error'>('success');
  private readonly toasts = inject(ToastService);

  constructor() {
    effect(() => {
      const message = this.message();
      if (!message) return;
      if (this.tone() === 'error') this.toasts.error(message);
      else this.toasts.success(message);
    });
  }
}
