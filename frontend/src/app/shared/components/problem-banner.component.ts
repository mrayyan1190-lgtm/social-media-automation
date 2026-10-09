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
      this.toasts.error(popupText(p), p.code === ErrorCodes.ConcurrencyConflict ? { label: 'Reload latest', run: () => this.reload.emit() } : undefined);
    });
  }
}

const MAX = 140;

/** One short sentence for the popup; the API's detail can list several reasons with quoted post text. */
export function popupText(p: ApiProblem): string {
  if (p.code === ErrorCodes.ConcurrencyConflict) return 'This was changed by someone else. Reload to see the latest.';
  let detail = p.detail.trim();
  if (p.code === ErrorCodes.SubmissionBlocked) {
    const flagged = detail.match(/Critical issue:/g)?.length ?? 0;
    if (flagged) return `Can't submit yet: ${flagged} flagged ${flagged === 1 ? 'issue' : 'issues'} in the text. Edit or rewrite the post, then submit again.`;
    detail = detail.replace(/^Cannot submit for review:\s*/i, '');
  }
  const first = detail.split(/(?<=[.!?])\s+/)[0];
  return first.length <= MAX ? first : `${first.slice(0, MAX - 1).trimEnd()}…`;
}
