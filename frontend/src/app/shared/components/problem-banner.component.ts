import { Component, effect, inject, input, output } from '@angular/core';
import { ApiProblem } from '../../core/http/api-problem';
import { ToastService } from '../../core/services/toast.service';
import { ErrorCodes } from '../models/error-codes';

/**
 * Shows an API problem as a popup at the top of the page (pages keep using this component as before). For a
 * concurrency conflict the popup offers to reload the latest server state.
 */
@Component({
  selector: 'app-problem-banner',
  template: ``,
})
export class ProblemBannerComponent {
  readonly problem = input<ApiProblem | null>(null);
  readonly reload = output<void>();
  private readonly toasts = inject(ToastService);

  constructor() {
    effect(() => {
      const p = this.problem();
      if (!p) return;
      this.toasts.error(p.detail, p.code === ErrorCodes.ConcurrencyConflict ? { label: 'Reload latest', run: () => this.reload.emit() } : undefined);
    });
  }
}
