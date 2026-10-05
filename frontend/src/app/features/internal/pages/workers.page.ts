import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { HEALTH_LABELS, InternalApiService, Worker, ago, tone, when } from '../internal-api.service';

/** Worker processes from their heartbeats. Read-only: no restart or kill actions. */
@Component({
  selector: 'app-internal-workers-page',
  imports: [ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div><h1>Workers</h1><p class="muted">Healthy: heartbeat within 2 minutes · Stale: within 10 minutes · Offline: silent longer.</p></div>
      <button type="button" (click)="load()">Refresh</button>
    </div>
    <nav class="tabs">
      <button type="button" [class.active]="kind() === 'AI'" (click)="kind.set('AI')">AI Workers</button>
      <button type="button" [class.active]="kind() === 'PUBLISHING'" (click)="kind.set('PUBLISHING')">Publishing Workers</button>
    </nav>
    <app-problem-banner [problem]="problem()" />
    <div class="table-wrap">
      <table class="table">
        <thead><tr><th>Worker ID</th><th>Instance</th><th>Status</th><th>Last Heartbeat</th><th>Current Jobs</th><th>Started At</th><th>Uptime</th><th>Version</th></tr></thead>
        <tbody>
          @for (w of shown(); track w.workerId) {
            <tr>
              <td><code>{{ w.workerId }}</code></td><td>{{ w.instance }}</td>
              <td><span [class]="'badge delivery ' + tone(w.status)">{{ labels[w.status] }}</span></td>
              <td>{{ ago(w.lastSeenAtUtc) }}</td><td>{{ w.currentJobs }}</td><td>{{ when(w.startedAtUtc) }}</td>
              <td>{{ uptime(w.uptimeSeconds) }}</td><td>{{ w.version ?? '—' }}</td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty">{{ loading() ? 'Loading…' : 'No workers have reported.' }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class WorkersPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly kind = signal<'AI' | 'PUBLISHING'>('AI');
  protected readonly workers = signal<Worker[]>([]);
  protected readonly shown = computed(() => this.workers().filter((w) => w.kind === this.kind()));
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly labels = HEALTH_LABELS;
  protected readonly tone = tone;
  protected readonly ago = ago;
  protected readonly when = when;

  ngOnInit(): void {
    void this.load();
  }

  protected uptime(seconds: number): string {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return h >= 24 ? `${Math.floor(h / 24)} d ${h % 24} h` : h > 0 ? `${h} h ${m} min` : `${m} min`;
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.workers.set(await firstValueFrom(this.api.workers()));
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
