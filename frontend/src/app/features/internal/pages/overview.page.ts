import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import {
  AiLogItem, HEALTH_LABELS, InternalApiService, Overview, Publication, UserListItem, WorkspaceListItem, ago, tone, words,
} from '../internal-api.service';

const DAY = 864e5;
const ROLE_COLORS: Record<string, string> = { Admin: '#1e3a8a', Creator: '#3b62e8', Reviewer: '#9db4ff' };
const CIRC = 2 * Math.PI * 44;

/** Local yyyy-MM-dd of a UTC timestamp (timestamps without an offset are UTC). */
function dayKey(utc: string | number): string {
  const d = typeof utc === 'number' ? new Date(utc) : new Date(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`);
  return d.toLocaleDateString('en-CA');
}

/** The last n local days, oldest first. */
function lastDays(n: number): string[] {
  return Array.from({ length: n }, (_, i) => dayKey(Date.now() - (n - 1 - i) * DAY));
}

interface Kpi { label: string; value: number; icon: string; link: string; query?: Record<string, string>; danger?: boolean; trend: number | null; trendTitle: string; series: number[] }

/** Platform overview: KPIs, AI activity, users by role and the operational summary. Details are one click deeper. */
@Component({
  selector: 'app-internal-overview-page',
  imports: [RouterLink, ProblemBannerComponent],
  template: `
    <app-problem-banner [problem]="problem()" (reload)="load()" />

    @if (o(); as d) {
      <div class="kpis">
        @for (k of kpis(); track k.label) {
          <a class="kpi" [class.danger]="k.danger" [routerLink]="k.link" [queryParams]="k.query ?? null">
            <span class="ic"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="k.icon"/></svg></span>
            <span class="kc"><span class="v">{{ k.value }}</span><span class="l">{{ k.label }}</span></span>
            @if (k.trend !== null) { <span class="tr" [class.zero]="k.trend === 0" [class.down]="k.trend < 0" [title]="k.trendTitle">{{ k.trend > 0 ? '+' : '' }}{{ k.trend }}</span> }
            <svg class="sp" width="54" height="18" viewBox="0 0 54 18">
              @if (spark(k.series); as pts) { <polyline [attr.points]="pts" fill="none" stroke="#3b62e8" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/> }
              @else { <polyline points="0,15 54,15" fill="none" stroke="#c3cde9" stroke-width="1.6" stroke-linecap="round" stroke-dasharray="1 4"/> }
            </svg>
          </a>
        }
      </div>

      <div class="dash-main">
        <section class="card">
          <div class="card-h">
            <h3>AI Activity</h3>
            <div class="card-h-right">
              <div class="legend"><span><i style="background:#2a4aab"></i>Requests</span><span><i style="background:#dc2626"></i>Failed</span></div>
              <div class="range-chips">
                <button type="button" [class.active]="range() === 14" (click)="range.set(14)">14d</button>
                <button type="button" [class.active]="range() === 30" (click)="range.set(30)">30d</button>
              </div>
            </div>
          </div>
          @let c = chart();
          <div class="chart-body">
            <svg viewBox="0 0 600 210" preserveAspectRatio="none" role="img" aria-label="AI requests per day">
              <defs><linearGradient id="gNavy" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3b62e8" stop-opacity=".28"/><stop offset="1" stop-color="#3b62e8" stop-opacity="0"/></linearGradient></defs>
              <g stroke="#e6ebf6" stroke-dasharray="3 4" stroke-width="1">
                @for (t of c.ticks; track t.y) { <line x1="36" [attr.y1]="t.y" x2="588" [attr.y2]="t.y"/> }
              </g>
              <g font-size="9" fill="#93a0bd" text-anchor="end">
                @for (t of c.ticks; track t.y) { <text x="26" [attr.y]="t.y + 3">{{ t.label }}</text> }
              </g>
              <path [attr.d]="c.area" fill="url(#gNavy)"/>
              <path [attr.d]="c.requests" fill="none" stroke="#2a4aab" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke"/>
              <path [attr.d]="c.failed" fill="none" stroke="#dc2626" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round" opacity=".9" vector-effect="non-scaling-stroke"/>
              <g font-size="9" fill="#93a0bd" text-anchor="middle">
                @for (l of c.labels; track l.x) { <text [attr.x]="l.x" y="203">{{ l.text }}</text> }
              </g>
            </svg>
          </div>
        </section>

        <section class="card">
          <div class="card-h"><h3>Users by Role</h3></div>
          <div class="donut-body">
            <div class="donut-wrap">
              <svg width="118" height="118" viewBox="0 0 118 118">
                <circle cx="59" cy="59" r="44" fill="none" stroke="#eef1f7" stroke-width="15"/>
                @for (s of donut().segments; track s.role) {
                  <circle cx="59" cy="59" r="44" fill="none" [attr.stroke]="s.color" stroke-width="15" [attr.stroke-dasharray]="s.dash"
                    [attr.stroke-dashoffset]="s.offset" transform="rotate(-90 59 59)"/>
                }
              </svg>
              <div class="donut-c"><div class="n">{{ donut().total }}</div><div class="t">Users</div></div>
            </div>
            <div class="dlegend">
              @for (r of donut().rows; track r.role) { <div class="row"><i [style.background]="r.color"></i>{{ r.role }} <b>{{ r.count }}</b></div> }
            </div>
          </div>
        </section>

        <div class="dash-bottom">
          <section class="card">
            <div class="card-h"><h3>Requires Attention</h3></div>
            <div class="card-b">
              @for (a of d.attention; track $index) {
                <div class="alert" [class.info]="a.severity === 'INFO'" [class.critical]="a.severity === 'CRITICAL'">
                  <b>{{ a.title }}</b>@if (a.detail) { <span class="sep">{{ a.detail }}</span> }
                  <a class="cards-link" [routerLink]="path(a.actionRoute)" [queryParams]="query(a.actionRoute)">{{ a.actionLabel }}</a>
                </div>
              } @empty { <p class="all-ok">All systems operational</p> }
            </div>
          </section>

          <section class="card">
            <div class="card-h"><h3>System Status</h3><a class="cards-link" routerLink="/internal/system-health">Details</a></div>
            <div class="card-b tight">
              @for (s of d.status; track s.name) {
                <div class="kv" [title]="s.detail ?? ''"><span class="k">{{ s.name }}</span><span [class]="'pill ' + pill(s.status)">{{ labels[s.status] }}</span></div>
              }
            </div>
          </section>

          <section class="card">
            <div class="card-h"><h3>Operations</h3><a class="cards-link" routerLink="/internal/queues">Queues</a></div>
            <div class="card-b tight">
              <div class="mini-h">AI</div>
              <div class="kv"><span class="k">Pending</span><b>{{ d.operations.aiPending }}</b></div>
              <div class="kv"><span class="k">Processing</span><b>{{ d.operations.aiProcessing }}</b></div>
              <div class="kv"><span class="k">Failed today</span><b [class.text-danger]="d.operations.aiFailedToday > 0">{{ d.operations.aiFailedToday }}</b></div>
              <div class="mini-h">Publishing</div>
              <div class="kv"><span class="k">Scheduled</span><b>{{ d.operations.publishingScheduled }}</b></div>
              <div class="kv"><span class="k">Processing</span><b>{{ d.operations.publishingProcessing }}</b></div>
              <div class="kv"><span class="k">Failed today</span><b [class.text-danger]="d.operations.publishingFailedToday > 0">{{ d.operations.publishingFailedToday }}</b></div>
              <div class="kv"><span class="k">Needs attention</span><b [class.text-danger]="d.operations.publishingNeedsAttention > 0">{{ d.operations.publishingNeedsAttention }}</b></div>
            </div>
          </section>

          <section class="card">
            <div class="card-h"><h3>Recent Admin Activity</h3><a class="cards-link" routerLink="/internal/logs">Logs</a></div>
            <div class="card-b tight act">
              @for (a of d.recentAdminActivity; track a.id) {
                <div class="row"><i class="dt"></i><span class="tx">{{ words(a.action) }} · {{ a.target }}</span><span class="mt">{{ ago(a.atUtc) }}</span></div>
              } @empty { <p class="all-ok muted">No admin activity yet.</p> }
            </div>
          </section>
        </div>
      </div>
    } @else if (loading()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 0; }
    .kpis { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 14px; margin-bottom: 14px; }
    .kpi { background: #fff; border: 1px solid var(--a-line); border-radius: 14px; box-shadow: var(--a-shadow); padding: 13px 14px; position: relative;
      overflow: hidden; display: flex; align-items: center; gap: 11px; color: inherit; text-decoration: none; transition: transform .15s, box-shadow .15s, border-color .15s; }
    .kpi:hover { transform: translateY(-2px); border-color: #c3d0ee; box-shadow: 0 8px 18px rgba(16, 31, 73, .1); }
    .kpi::after { content: ''; position: absolute; left: 0; top: 0; bottom: 0; width: 3px; background: linear-gradient(180deg, #3b62e8, #16295e); opacity: 0; transition: opacity .15s; }
    .kpi:hover::after { opacity: 1; }
    .kpi .ic { width: 36px; height: 36px; border-radius: 10px; flex: none; background: linear-gradient(145deg, #3b62e8, #1c3479); display: flex; align-items: center;
      justify-content: center; color: #fff; box-shadow: 0 4px 9px rgba(30, 58, 138, .28), inset 0 1px 0 rgba(255, 255, 255, .2); }
    .kpi.danger .ic { background: linear-gradient(145deg, #ef4444, #b91c1c); box-shadow: 0 4px 9px rgba(185, 28, 28, .28); }
    .kpi .kc { min-width: 0; display: flex; flex-direction: column; }
    .kpi .v { font-size: 22px; font-weight: 800; letter-spacing: -.5px; line-height: 1.05; color: var(--a-ink); }
    .kpi .l { font-size: 10.5px; font-weight: 600; color: var(--a-muted); margin-top: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .kpi .tr { position: absolute; top: 11px; right: 11px; font-size: 9.5px; font-weight: 700; color: #15803d; background: #e6f7ec; border-radius: 6px; padding: 2px 6px; }
    .kpi .tr.zero { color: #6b7899; background: #eef1f7; }
    .kpi .tr.down { color: #b91c1c; background: #fdeaea; }
    .kpi .sp { position: absolute; right: 10px; bottom: 9px; }

    .dash-main { display: grid; gap: 14px; grid-template-columns: 1.85fr 1fr; grid-template-rows: 300px auto; }
    .dash-bottom { grid-column: 1 / -1; display: grid; gap: 14px; grid-template-columns: 1.02fr 1.02fr .96fr 1fr; }
    .card-h-right { display: flex; align-items: center; gap: 16px; }
    .chart-body { flex: 1; min-height: 0; padding: 8px 12px 10px; display: flex; }
    .chart-body svg { width: 100%; height: 100%; }
    .legend { display: flex; gap: 14px; align-items: center; font-size: 11px; color: var(--a-muted); }
    .legend i { width: 9px; height: 9px; border-radius: 3px; display: inline-block; margin-right: 5px; }
    .range-chips { display: flex; gap: 4px; }
    .range-chips button { height: auto; font-size: 10.5px; font-weight: 600; color: var(--a-muted); padding: 3px 9px; border-radius: 7px; border: none; background: none; cursor: pointer; min-height: 0; }
    .range-chips button.active { background: #e7eefc; color: var(--a-navy); }

    .donut-body { flex: 1; min-height: 0; display: flex; align-items: center; justify-content: center; gap: 20px; padding: 8px 14px; }
    .donut-wrap { position: relative; flex: none; }
    .donut-c { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    .donut-c .n { font-size: 21px; font-weight: 800; color: var(--a-ink); }
    .donut-c .t { font-size: 10px; font-weight: 600; color: var(--a-faint); text-transform: uppercase; letter-spacing: .6px; }
    .dlegend { display: flex; flex-direction: column; gap: 9px; }
    .dlegend .row { display: flex; align-items: center; gap: 8px; font-size: 12px; color: var(--a-muted); }
    .dlegend i { width: 9px; height: 9px; border-radius: 3px; }
    .dlegend b { margin-left: auto; color: var(--a-ink); padding-left: 14px; }

    .card-b.tight { padding-top: 6px; padding-bottom: 6px; }
    .alert { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; padding: 9px 12px; border-radius: 10px; border: 1px solid var(--a-line-soft);
      border-left: 3px solid #d97706; background: #fffdf6; margin-bottom: 8px; font-size: 12px; }
    .alert.info { border-left-color: #3b82f6; background: #f8fbff; }
    .alert.critical { border-left-color: #dc2626; background: #fff8f8; }
    .alert b { font-weight: 600; }
    .alert .sep { color: var(--a-faint); }
    .alert .cards-link { margin-left: auto; flex: none; }
    .kv { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 7.5px 2px; border-bottom: 1px solid var(--a-line-soft); font-size: 12.5px; }
    .kv:last-child { border-bottom: none; }
    .kv .k { color: var(--a-muted); }
    .mini-h { font-size: 11px; font-weight: 700; color: var(--a-faint); text-transform: uppercase; letter-spacing: .7px; margin: 6px 0 2px; }
    .act .row { display: flex; align-items: flex-start; gap: 10px; padding: 7px 2px; border-bottom: 1px solid var(--a-line-soft); }
    .act .row:last-child { border-bottom: none; }
    .act .dt { width: 6px; height: 6px; border-radius: 50%; background: #8aa5e8; margin-top: 6px; flex: none; }
    .act .tx { font-size: 12px; color: var(--a-ink); line-height: 1.4; }
    .act .mt { margin-left: auto; font-size: 11px; color: var(--a-faint); white-space: nowrap; padding-left: 10px; }
    .all-ok { margin: 6px 0; font-size: 12.5px; color: #15803d; font-weight: 600; }
    .all-ok.muted { color: var(--a-faint); font-weight: 500; }

    @media (max-width: 1200px) {
      .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); }
      .dash-main { grid-template-columns: 1fr; grid-template-rows: 300px auto auto; }
      .dash-bottom { grid-template-columns: 1fr 1fr; }
    }
    @media (max-width: 700px) { .kpis, .dash-bottom { grid-template-columns: 1fr; } }
  `,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly o = signal<Overview | null>(null);
  private readonly users = signal<UserListItem[]>([]);
  private readonly workspaces = signal<WorkspaceListItem[]>([]);
  private readonly aiCalls = signal<AiLogItem[]>([]);
  private readonly publications = signal<Publication[]>([]);
  protected readonly range = signal<14 | 30>(14);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly labels = HEALTH_LABELS;
  protected readonly ago = ago;
  protected readonly words = words;

  /** Distinct AI requests and failed requests per local day, over the last 30 days. */
  private readonly aiByDay = computed(() => {
    const requests = new Map<string, Set<string>>();
    const failed = new Map<string, Set<string>>();
    for (const c of this.aiCalls()) {
      const day = dayKey(c.startedAtUtc);
      if (!requests.has(day)) requests.set(day, new Set());
      requests.get(day)!.add(c.requestId);
      if (!c.succeeded) {
        if (!failed.has(day)) failed.set(day, new Set());
        failed.get(day)!.add(c.requestId);
      }
    }
    return { requests: (d: string) => requests.get(d)?.size ?? 0, failed: (d: string) => failed.get(d)?.size ?? 0 };
  });

  protected readonly kpis = computed<Kpi[]>(() => {
    const d = this.o();
    if (!d) return [];
    const week = lastDays(7);
    const ai = this.aiByDay();
    // Created-per-day series for tenants; "+N" is how many were added in the last 7 days.
    const created = (dates: string[]) => week.map((day) => dates.filter((c) => dayKey(c) === day).length);
    const wsNew = created(this.workspaces().map((w) => w.createdAtUtc));
    const userNew = created(this.users().map((u) => u.createdAtUtc));
    const pubs = (state: string) => week.map((day) => this.publications().filter((p) => p.state === state && dayKey(p.publishAtUtc) === day).length);
    const published = pubs('PUBLISHED');
    const failed = pubs('FAILED');
    const aiSeries = week.map(ai.requests);
    const delta = (s: number[]) => s[6] - s[5];
    const sum = (s: number[]) => s.reduce((a, b) => a + b, 0);
    const vsYesterday = 'Change since yesterday';
    return [
      { label: 'Workspaces', value: d.summary.workspaces, icon: ICON.workspaces, link: '/internal/workspaces', trend: sum(wsNew), trendTitle: 'Added in the last 7 days', series: wsNew },
      { label: 'Users', value: d.summary.activeUsers, icon: ICON.users, link: '/internal/users', trend: sum(userNew), trendTitle: 'Added in the last 7 days', series: userNew },
      { label: 'AI Requests Today', value: d.summary.aiRequestsToday, icon: ICON.ai, link: '/internal/queues', trend: delta(aiSeries), trendTitle: vsYesterday, series: aiSeries },
      { label: 'Published Today', value: d.summary.publishedToday, icon: ICON.published, link: '/internal/publications', query: { status: 'PUBLISHED' }, trend: delta(published), trendTitle: vsYesterday, series: published },
      { label: 'Failed Today', value: d.summary.failedPublicationsToday, icon: ICON.failed, link: '/internal/publications', query: { status: 'FAILED' }, danger: d.summary.failedPublicationsToday > 0, trend: delta(failed), trendTitle: vsYesterday, series: failed },
      // No history is kept for reconciliation, so this card has no trend.
      { label: 'Needs Reconciliation', value: d.summary.reconciliationRequired, icon: ICON.reconcile, link: '/internal/reconciliation', danger: d.summary.reconciliationRequired > 0, trend: null, trendTitle: '', series: [] },
    ];
  });

  /** Requests (area + line) and failures per day, plotted in a 600×210 box: x 40–588, y 186 (zero) to 24 (top tick). */
  protected readonly chart = computed(() => {
    const days = lastDays(this.range());
    const ai = this.aiByDay();
    const req = days.map(ai.requests);
    const fail = days.map(ai.failed);
    const top = Math.max(4, Math.ceil(Math.max(...req) / 4) * 4);
    const x = (i: number) => 40 + (i * 548) / (days.length - 1);
    const y = (v: number) => 186 - (v / top) * 162;
    const line = (s: number[]) => s.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
    const step = Math.ceil(days.length / 7);
    const fmt = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' });
    return {
      ticks: [0, 1, 2, 3, 4].map((i) => ({ y: 186 - i * 40.5, label: String((top / 4) * i) })),
      requests: line(req),
      failed: line(fail),
      area: `${line(req)} L588,186 L40,186 Z`,
      labels: days.map((d, i) => ({ x: x(i), text: fmt.format(new Date(`${d}T12:00:00`)), show: i % step === 0 })).filter((l) => l.show),
    };
  });

  /** Each user counted once, by their role in their first workspace; platform operators without a workspace are left out. */
  protected readonly donut = computed(() => {
    const counts = new Map<string, number>();
    for (const u of this.users()) {
      const role = u.workspaces[0]?.role;
      if (role) counts.set(role, (counts.get(role) ?? 0) + 1);
    }
    const total = [...counts.values()].reduce((a, b) => a + b, 0);
    const rows = ['Admin', 'Creator', 'Reviewer', ...[...counts.keys()].filter((r) => !ROLE_COLORS[r])]
      .map((role) => ({ role, count: counts.get(role) ?? 0, color: ROLE_COLORS[role] ?? '#c3cde9' }));
    let offset = 0;
    const segments = rows.filter((r) => r.count).map((r) => {
      const len = (r.count / total) * CIRC;
      const s = { role: r.role, color: r.color, dash: `${len.toFixed(1)} ${CIRC.toFixed(1)}`, offset: (-offset).toFixed(1) };
      offset += len;
      return s;
    });
    return { total, rows, segments };
  });

  ngOnInit(): void {
    void this.load();
  }

  /** Sparkline points for a 54×18 box, or null when every value is zero. */
  protected spark(series: number[]): string | null {
    const max = Math.max(...series, 0);
    if (!max) return null;
    return series.map((v, i) => `${((i * 54) / (series.length - 1)).toFixed(1)},${(16 - (v / max) * 13).toFixed(1)}`).join(' ');
  }

  protected pill(status: string): string {
    return { success: 'green', waiting: 'amber', danger: 'red' }[tone(status)] ?? 'gray';
  }

  protected path(route: string): string {
    return route.split('?')[0];
  }

  protected query(route: string): Record<string, string> {
    return Object.fromEntries(new URLSearchParams(route.split('?')[1] ?? ''));
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    const from = (days: number) => new Date(Date.now() - days * DAY).toISOString();
    try {
      // ponytail: the AI log returns at most 200 calls, so a very busy 30-day chart is truncated; add a daily-count endpoint if that happens.
      const [overview, users, workspaces, aiCalls, publications] = await Promise.all([
        firstValueFrom(this.api.overview()),
        firstValueFrom(this.api.users({})).catch(() => []),
        firstValueFrom(this.api.workspaces({})).catch(() => []),
        firstValueFrom(this.api.aiLog({ from: from(30) })).catch(() => []),
        firstValueFrom(this.api.publications({ from: from(7) })).catch(() => []),
      ]);
      this.o.set(overview);
      this.users.set(users);
      this.workspaces.set(workspaces);
      this.aiCalls.set(aiCalls);
      this.publications.set(publications);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}

/** KPI icons (24×24 stroke paths). */
const ICON = {
  workspaces: 'M5 8h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2zM9 8V6a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2',
  users: 'M9 4.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zM2.5 20a6.5 6.5 0 0 1 13 0',
  ai: 'M12 3l1.7 4.6L18 9.2l-4.3 1.6L12 15.5l-1.7-4.7L6 9.2l4.3-1.6L12 3z',
  published: 'M22 2 11 13M22 2 15 22l-4-9-9-4 20-7z',
  failed: 'M12 8v5M12 16.5h.01M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0',
  reconcile: 'M8 3v3M16 3v3M4 9h16M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z',
};
