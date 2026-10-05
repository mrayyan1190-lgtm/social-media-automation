import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { HEALTH_LABELS, HealthComponent, InternalApiService, ago, tone } from '../internal-api.service';

/** Component health from live probes, heartbeats and recent traffic. Nothing is shown as Healthy without a real signal. */
@Component({
  selector: 'app-internal-system-health-page',
  imports: [ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div><h1>System Health</h1><p class="muted">Checked when this page loads. "Not Monitored" means there is no probe or no recent signal.</p></div>
      <button type="button" (click)="load()" [disabled]="loading()">{{ loading() ? 'Checking…' : 'Check Again' }}</button>
    </div>
    <app-problem-banner [problem]="problem()" />
    <div class="table-wrap">
      <table class="table">
        <thead><tr><th>Component</th><th>Status</th><th>Latency</th><th>Last Check</th><th>Last Successful Check</th><th>Last Error</th><th>Measured By</th></tr></thead>
        <tbody>
          @for (c of components(); track c.component) {
            <tr>
              <td><strong>{{ c.component }}</strong></td>
              <td><span [class]="'badge delivery ' + tone(c.status)">{{ labels[c.status] }}</span></td>
              <td>{{ c.latencyMs !== null ? c.latencyMs + ' ms' : '—' }}</td>
              <td>{{ ago(c.lastCheckAtUtc) }}</td>
              <td>{{ ago(c.lastSuccessAtUtc) }}</td>
              <td class="problem-text">{{ c.lastError ?? '—' }}</td>
              <td class="hint">{{ c.monitoring }}</td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">{{ loading() ? 'Checking…' : 'No data.' }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class SystemHealthPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly components = signal<HealthComponent[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly labels = HEALTH_LABELS;
  protected readonly tone = tone;
  protected readonly ago = ago;

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.components.set(await firstValueFrom(this.api.systemHealth()));
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
