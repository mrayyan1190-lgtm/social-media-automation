import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { formatInZone } from '../publications/models/time-zones';

/** The signed-in user's notifications in the current workspace. */
@Component({
  selector: 'app-notifications-page',
  imports: [RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div><h1>Notifications</h1><p class="muted">Reviews, approvals, publishing results and team updates.</p></div>
      <button type="button" (click)="markAllRead()" [disabled]="busy() || !hasUnread()">Mark all as read</button>
    </div>
    <app-problem-banner [problem]="problem()" (reload)="load()" />
    <ul class="list">
      @for (n of items(); track n.id) {
        <li>
          <span>
            @if (!n.readAtUtc) { <span class="badge">New</span> }
            <strong>{{ n.title }}</strong> {{ n.message }}
            @if (link(n); as url) { · <a [routerLink]="url" (click)="markRead(n)">Open</a> }
          </span>
          <span class="hint">{{ time(n.createdAtUtc) }}</span>
        </li>
      } @empty {
        <li class="hint">{{ busy() ? 'Loading…' : 'You are all caught up. No notifications yet.' }}</li>
      }
    </ul>
  `,
})
export class NotificationsPage {
  private readonly api = inject(NotificationApiService);
  private readonly workspace = inject(WorkspaceService);

  protected readonly items = signal<AppNotification[]>([]);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);

  constructor() {
    effect(() => {
      if (this.workspace.orgId()) void this.load();
    });
  }

  protected hasUnread(): boolean {
    return this.items().some((n) => !n.readAtUtc);
  }

  protected link(n: AppNotification): string[] | null {
    if (n.resourceType === 'ReviewRequest') return ['/publisher/approvals', n.resourceId];
    if (n.resourceType === 'SocialAccount') return ['/social-accounts'];
    if (n.resourceType === 'Organization') return ['/home'];
    if (n.resourceType === 'Post') return ['/posts', n.resourceId];
    return null;
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }

  async load(): Promise<void> {
    await this.run(async (orgId) => this.items.set(await firstValueFrom(this.api.list(orgId))));
  }

  protected markRead(n: AppNotification): void {
    const orgId = this.workspace.orgId();
    if (orgId && !n.readAtUtc) void firstValueFrom(this.api.markRead(orgId, n.id)).catch(() => undefined);
  }

  protected markAllRead(): Promise<void> {
    return this.run(async (orgId) => {
      await firstValueFrom(this.api.markAllRead(orgId));
      this.items.set(await firstValueFrom(this.api.list(orgId)));
    });
  }

  private async run(action: (orgId: string) => Promise<void>): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await action(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
