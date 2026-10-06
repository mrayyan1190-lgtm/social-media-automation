import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { formatInZone } from '../publications/models/time-zones';

type Tile = 'review' | 'attention' | 'approved' | 'neutral';

/** Icon tile per notification type; publishing problems get a "Fix" button instead of "Open". */
const TILES: Record<string, Tile> = {
  REVIEW_REQUESTED: 'review',
  SCHEDULED: 'review',
  CHANGES_REQUESTED: 'attention',
  FAILED: 'attention',
  ACTION_REQUIRED: 'attention',
  REAUTHORIZATION_REQUIRED: 'attention',
  RECONCILIATION_REQUIRED: 'attention',
  APPROVED: 'approved',
  PUBLISHED: 'approved',
};
const FIX_TYPES = ['FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED', 'RECONCILIATION_REQUIRED'];

/** The signed-in user's notifications in the current workspace. */
@Component({
  selector: 'app-notifications-page',
  imports: [RouterLink, ProblemBannerComponent],
  templateUrl: './notifications.page.html',
  styleUrl: './notifications.page.css',
})
export class NotificationsPage {
  private readonly api = inject(NotificationApiService);
  private readonly workspace = inject(WorkspaceService);
  private readonly toast = inject(ToastService);

  protected readonly items = signal<AppNotification[]>([]);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly unread = computed(() => this.items().filter((n) => !n.readAtUtc).length);

  constructor() {
    effect(() => {
      if (this.workspace.orgId()) void this.load();
    });
  }

  protected tile(n: AppNotification): Tile {
    return TILES[n.type] ?? 'neutral';
  }

  protected isFix(n: AppNotification): boolean {
    return FIX_TYPES.includes(n.type);
  }

  protected link(n: AppNotification): string[] | null {
    // Creators cannot open Approvals; their review results are followed in the Planner.
    if (n.resourceType === 'ReviewRequest') return this.workspace.isAdmin() ? ['/approvals', n.resourceId] : ['/planner'];
    if (n.resourceType === 'SocialAccount') return this.workspace.isAdmin() ? ['/social-accounts'] : null;
    if (n.resourceType === 'Organization') return ['/home'];
    if (n.resourceType === 'Post') return ['/composer', n.resourceId];
    return null;
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }

  async load(): Promise<void> {
    await this.run(async (orgId) => this.items.set(await firstValueFrom(this.api.list(orgId))));
    this.loaded.set(true);
  }

  protected markRead(n: AppNotification): void {
    const orgId = this.workspace.orgId();
    if (!orgId || n.readAtUtc) return;
    void firstValueFrom(this.api.markRead(orgId, n.id)).catch(() => undefined);
    this.items.update((all) => all.map((x) => (x.id === n.id ? { ...x, readAtUtc: new Date().toISOString() } : x)));
  }

  protected async markAllRead(): Promise<void> {
    await this.run(async (orgId) => {
      await firstValueFrom(this.api.markAllRead(orgId));
      this.items.set(await firstValueFrom(this.api.list(orgId)));
      this.toast.success('All notifications marked as read');
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
