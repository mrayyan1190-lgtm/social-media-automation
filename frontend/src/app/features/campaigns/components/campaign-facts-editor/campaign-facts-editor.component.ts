import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { CampaignFact } from '../../models/campaign.model';
import { CampaignApiService } from '../../services/campaign-api.service';

/** Supporting facts: verifiable business evidence, optionally with a source. Emits `changed` so the page reloads fresh row versions. */
@Component({
  selector: 'app-campaign-facts-editor',
  imports: [ReactiveFormsModule, DatePipe, ProblemBannerComponent],
  templateUrl: './campaign-facts-editor.component.html',
})
export class CampaignFactsEditorComponent {
  private readonly api = inject(CampaignApiService);
  private readonly fb = inject(FormBuilder).nonNullable;

  readonly orgId = input.required<string>();
  readonly campaignId = input.required<string>();
  readonly facts = input.required<CampaignFact[]>();
  readonly readonly = input(false);
  readonly changed = output<void>();

  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly editingId = signal<string | null>(null);

  protected readonly addForm = this.newFactForm();
  protected readonly editForm = this.newFactForm();

  protected startEdit(fact: CampaignFact): void {
    this.editingId.set(fact.id);
    this.editForm.reset({ text: fact.text, sourceNote: fact.sourceNote ?? '' });
  }

  protected add(): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.api.addFact(this.orgId(), this.campaignId(), this.addForm.getRawValue()));
      this.addForm.reset();
    });
  }

  protected saveEdit(fact: CampaignFact): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.api.updateFact(this.orgId(), this.campaignId(), fact.id, this.editForm.getRawValue(), fact.rowVersion));
      this.editingId.set(null);
    });
  }

  protected remove(fact: CampaignFact): Promise<void> {
    return this.run(() => firstValueFrom(this.api.removeFact(this.orgId(), this.campaignId(), fact.id, fact.rowVersion)));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      await action();
      this.changed.emit();
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  private newFactForm() {
    return this.fb.group({
      text: ['', [Validators.required, Validators.maxLength(1000)]],
      sourceNote: ['', Validators.maxLength(500)],
    });
  }

  protected reload(): void {
    this.editingId.set(null);
    this.problem.set(null);
    this.changed.emit();
  }
}
