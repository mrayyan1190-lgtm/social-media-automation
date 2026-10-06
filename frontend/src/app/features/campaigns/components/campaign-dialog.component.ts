import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { CAMPAIGN_COLORS, Campaign } from '../models/campaign.model';
import { CampaignApiService } from '../services/campaign-api.service';

/** Small modal to create a campaign folder, or rename/recolour an existing one. */
@Component({
  selector: 'app-campaign-dialog',
  imports: [FormsModule, ProblemBannerComponent],
  template: `
    <dialog #dialog class="modal" (close)="problem.set(null)">
      <form (submit)="$event.preventDefault(); save()">
        <header class="modal-head">
          <h2>{{ editing() ? 'Edit Campaign' : 'Create Campaign' }}</h2>
          <button type="button" class="icon-btn" aria-label="Close" (click)="close()">×</button>
        </header>
        <label>Campaign Name *
          <input name="name" required maxlength="200" placeholder="Product Launch" [(ngModel)]="name" />
        </label>
        <div class="field">
          <span class="field-label">Campaign Color *</span>
          <div class="swatches">
            @for (c of colors; track c) {
              <button type="button" class="swatch-btn" [class.on]="color.toUpperCase() === c" [style.background]="c"
                [attr.aria-label]="'Color ' + c" (click)="color = c"></button>
            }
            <input type="color" class="color-input" name="color" aria-label="Custom color" [(ngModel)]="color" />
          </div>
        </div>
        <label>Description <span class="hint">optional</span>
          <textarea name="description" rows="2" maxlength="500" placeholder="What these posts are about" [(ngModel)]="description"></textarea>
        </label>
        <app-problem-banner [problem]="problem()" />
        <footer class="modal-actions">
          <button type="button" (click)="close()">Cancel</button>
          <button type="submit" class="primary" [disabled]="busy() || !name.trim()">{{ busy() ? 'Saving…' : editing() ? 'Save' : 'Create Campaign' }}</button>
        </footer>
      </form>
    </dialog>
  `,
})
export class CampaignDialogComponent {
  readonly saved = output<Campaign>();

  private readonly api = inject(CampaignApiService);
  private readonly workspace = inject(WorkspaceService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly colors = CAMPAIGN_COLORS;
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly editing = signal<Campaign | null>(null);
  protected name = '';
  protected color = CAMPAIGN_COLORS[0];
  protected description = '';

  open(existing?: Campaign): void {
    this.editing.set(existing ?? null);
    this.name = existing?.name ?? '';
    this.color = existing?.color ?? CAMPAIGN_COLORS[0];
    this.description = existing?.description ?? '';
    this.problem.set(null);
    this.dialog().nativeElement.showModal();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  protected async save(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !this.name.trim()) return;
    this.busy.set(true);
    this.problem.set(null);
    const input = { name: this.name.trim(), color: this.color.toUpperCase(), description: this.description.trim() || null };
    try {
      const current = this.editing();
      const campaign = await firstValueFrom(current ? this.api.update(orgId, current.id, input, current.rowVersion) : this.api.create(orgId, input));
      this.close();
      this.saved.emit(campaign);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
