import { Component, input, output } from '@angular/core';
import { ApiProblem } from '../../core/http/api-problem';
import { ErrorCodes } from '../models/error-codes';

/** Shows an API problem; for a concurrency conflict it offers to reload the latest server state. */
@Component({
  selector: 'app-problem-banner',
  template: `
    @if (problem(); as p) {
      <div class="banner error" role="alert">
        <span>{{ p.detail }}</span>
        @if (p.code === conflict) {
          <button type="button" (click)="reload.emit()">Reload latest</button>
        }
      </div>
    }
  `,
})
export class ProblemBannerComponent {
  readonly problem = input<ApiProblem | null>(null);
  readonly reload = output<void>();
  protected readonly conflict = ErrorCodes.ConcurrencyConflict;
}
