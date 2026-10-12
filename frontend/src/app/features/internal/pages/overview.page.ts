import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import {
  AiLogItem, HEALTH_LABELS, InternalApiService, Overview, Publication, UserListItem, WorkspaceListItem, ago, tone, words,
} from '../internal-api.service';

const DAY = 864e5;
const ROLE_COLORS: Record<string, string> = { Admin: '#3e75bf', Creator: '#9279d0', Reviewer: '#e6a1a6' };
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
              <div class="legend"><span><i style="background:#537bc5"></i>Requests</span><span><i style="background:#d17889"></i>Failed</span></div>
              <div class="range-chips">
                <button type="button" [class.active]="range() === 14" (click)="range.set(14)">14d</button>
                <button type="button" [class.active]="range() === 30" (click)="range.set(30)">30d</button>
              </div>
            </div>
          </div>
          @let c = chart();
          <div class="chart-body">
            <svg viewBox="0 0 600 210" preserveAspectRatio="none" role="img" aria-label="AI requests per day">
              <defs><linearGradient id="gNavy" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7198e5" stop-opacity=".32"/><stop offset="1" stop-color="#7198e5" stop-opacity=".01"/></linearGradient></defs>
              <g stroke="#e4ebf5" stroke-dasharray="4 7" stroke-width="1">
                @for (t of c.ticks; track t.y) { <line x1="36" [attr.y1]="t.y" x2="588" [attr.y2]="t.y"/> }
              </g>
              <g font-size="9" fill="#9baec7" text-anchor="end">
                @for (t of c.ticks; track t.y) { <text x="26" [attr.y]="t.y + 3">{{ t.label }}</text> }
              </g>
              <path [attr.d]="c.area" fill="url(#gNavy)"/>
              <path [attr.d]="c.requests" fill="none" stroke="#436ec0" stroke-width="3" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke"/>
              <path [attr.d]="c.failed" fill="none" stroke="#d07785" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round" opacity=".9" vector-effect="non-scaling-stroke"/>
              <g font-size="9" fill="#9baec7" text-anchor="middle">
                @for (l of c.labels; track l.x) { <text [attr.x]="l.x" y="203">{{ l.text }}</text> }
              </g>
            </svg>
          </div>
        </section>

        <section class="card">
          <div class="card-h"><h3>Users by Role</h3><span class="soft-tag">{{ donut().total }} total</span></div>
          <div class="donut-body">
            <div class="donut-wrap">
              <svg width="118" height="118" viewBox="0 0 118 118">
                <circle cx="59" cy="59" r="44" fill="none" stroke="#edf2fd" stroke-width="15"/>
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
            <div class="card-h"><h3>Requires Attention</h3>@if (d.attention.length) { <span class="pill amber">{{ d.attention.length }} open</span> } @else { <span class="pill green">Clear</span> }</div>
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
    /* KPI cards (the design's metric tiles): icon and change at the top, value and sparkline at the bottom. */
    .kpis { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 12px; margin-bottom: 12px; }
    .kpi { position: relative; overflow: hidden; min-height: 112px; padding: 14px 15px; display: flex; flex-direction: column; justify-content: space-between; gap: 10px;
      color: inherit; text-decoration: none; border: 1px solid #dce6f3; border-radius: 18px; background: linear-gradient(145deg, #fff 4%, #f1f7ff 130%);
      box-shadow: 0 12px 28px #5a75921b; transition: transform .2s, box-shadow .2s; }
    .kpi:nth-child(3n+2) { background: linear-gradient(145deg, #fff 4%, #f1eefb 130%); }
    .kpi:nth-child(3n) { background: linear-gradient(145deg, #fff 4%, #fff0ea 130%); }
    .kpi::before { content: ''; position: absolute; right: -25px; top: -53px; width: 118px; height: 118px; border-radius: 50%; background: radial-gradient(circle, #cadfff7a, transparent 72%); }
    .kpi:nth-child(3n+2)::before { background: radial-gradient(circle, #e5d8f67c, transparent 72%); }
    .kpi:nth-child(3n)::before { background: radial-gradient(circle, #f4d8d27a, transparent 72%); }
    .kpi::after { content: ''; position: absolute; left: 0; right: 0; bottom: 0; height: 3px; background: linear-gradient(90deg, #a9cef0, #c8bee8, #ecc1bc); }
    .kpi:hover { transform: translateY(-2px); box-shadow: 0 18px 30px #5a759229; }
    .kpi .ic { position: relative; width: 34px; height: 34px; border-radius: 11px; flex: none; display: grid; place-items: center; color: #447bba;
      background: linear-gradient(145deg, #d7e9ff, #e9f2ff); box-shadow: inset 0 1px 0 #fff, 0 4px 12px #7397bf20; }
    .kpi:nth-child(2) .ic, .kpi:nth-child(6) .ic { color: #8c77c4; background: linear-gradient(145deg, #e8dffc, #f3f1ff); }
    .kpi:nth-child(3) .ic, .kpi:nth-child(5) .ic, .kpi.danger .ic { color: #ca887f; background: linear-gradient(145deg, #fce2de, #fff1eb); }
    .kpi:nth-child(4) .ic { color: #438c77; background: linear-gradient(145deg, #d7f6eb, #eefbf4); }
    .kpi .ic svg { width: 17px; height: 17px; }
    .kpi .kc { position: relative; min-width: 0; display: flex; flex-direction: column; padding-right: 58px; }
    .kpi .v { font-family: Manrope, sans-serif; font-size: 27px; font-weight: 800; letter-spacing: -.07em; line-height: 1; color: #213957; }
    .kpi.danger .v { color: #c85e71; }
    .kpi .l { font-size: 10px; font-weight: 700; color: #627c9c; margin-top: 5px; line-height: 1.3; }
    .kpi .tr { position: absolute; top: 14px; right: 14px; border-radius: 999px; background: #e3f6ec; color: #25866d; padding: 4px 7px; font-size: 9px; font-weight: 800; }
    .kpi .tr.zero { background: #edf0f8; color: #8192ad; }
    .kpi .tr.down { background: #ffebec; color: #c85e71; }
    .kpi .sp { position: absolute; right: 14px; bottom: 16px; }
    .kpi:nth-child(1) .sp polyline:not([stroke-dasharray]) { stroke: #5687d1; }
    .kpi:nth-child(2) .sp polyline:not([stroke-dasharray]), .kpi:nth-child(6) .sp polyline:not([stroke-dasharray]) { stroke: #8a7cc9; }
    .kpi:nth-child(3) .sp polyline:not([stroke-dasharray]), .kpi:nth-child(5) .sp polyline:not([stroke-dasharray]) { stroke: #d99796; }
    .kpi:nth-child(4) .sp polyline:not([stroke-dasharray]) { stroke: #64a99c; }
    .kpi .sp polyline[stroke-dasharray] { stroke: #d7d7e6; stroke-dasharray: 2 4; }

    .dash-main { display: grid; gap: 12px; grid-template-columns: minmax(0, 1.75fr) minmax(290px, 1fr); grid-template-rows: 312px auto; }
    .dash-bottom { grid-column: 1 / -1; display: grid; gap: 12px; grid-template-columns: repeat(4, minmax(0, 1fr)); }
    .dash-bottom .card { min-height: 252px; }
    .dash-bottom .card:nth-child(1) { background: linear-gradient(160deg, #fff 25%, #effbf6 140%); }
    .dash-bottom .card:nth-child(2) { background: linear-gradient(160deg, #fff 25%, #eef5ff 140%); }
    .dash-bottom .card:nth-child(3) { background: linear-gradient(160deg, #fff 25%, #f6f0ff 140%); }
    .dash-bottom .card:nth-child(4) { background: linear-gradient(160deg, #fff 25%, #fff0ec 140%); }
    .card-h-right { display: flex; align-items: center; gap: 9px; }
    .soft-tag { font-size: 10px; font-weight: 700; border-radius: 9px; padding: 6px 9px; color: #6283ac; background: linear-gradient(110deg, #eaf3ff, #f0ebfb); border: 1px solid #e5e7f4; }
    .card-h .cards-link::after { content: ' ↗'; }
    .chart-body { flex: 1; min-height: 0; padding: 8px 4px 4px; display: flex; }
    .chart-body svg { width: 100%; height: 100%; overflow: visible; }
    .legend { display: flex; gap: 10px; align-items: center; font-size: 10px; font-weight: 700; color: #7c8fad; }
    .legend i { width: 7px; height: 7px; border-radius: 50%; display: inline-block; margin-right: 5px; }
    /* 14d / 30d: the design's tiny switch. */
    .range-chips { display: flex; gap: 2px; background: #f0f4fb; padding: 3px; border-radius: 9px; }
    .range-chips button { height: auto; min-height: 0; padding: 5px 8px; border: 0; border-radius: 7px; background: none; box-shadow: none; font-size: 10px; font-weight: 800; color: #7d91ab; cursor: pointer; }
    .range-chips button.active { background: #fff; color: #446db1; box-shadow: 0 1px 4px #95a5bf40; }

    .donut-body { flex: 1; min-height: 0; display: flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: 20px 40px; padding: 8px 14px; }
    .donut-wrap { position: relative; flex: none; border-radius: 50%; box-shadow: 0 0 0 9px #edf2fd7a, 0 13px 30px #7886c62b; }
    .donut-wrap svg { display: block; width: 150px; height: 150px; }
    .donut-c { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    .donut-c .n { font-family: Manrope, sans-serif; font-size: 30px; font-weight: 800; letter-spacing: -.07em; color: #23395a; }
    .donut-c .t { font-size: 10px; font-weight: 800; color: #90a2b8; text-transform: uppercase; letter-spacing: .12em; }
    .dlegend { display: flex; flex-direction: column; min-width: 140px; }
    .dlegend .row { display: flex; align-items: center; gap: 8px; padding: 10px 0; font-size: 11px; font-weight: 700; color: #607896; }
    .dlegend i { width: 9px; height: 9px; border-radius: 3px; }
    .dlegend b { margin-left: auto; color: #294263; padding-left: 28px; }

    .card-b.tight { padding-top: 4px; padding-bottom: 4px; }
    .alert { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; padding: 10px 12px; border-radius: 12px; border: 1px solid #f6e4d2;
      border-left: 3px solid #d9a46b; background: #fffaf3; margin-bottom: 8px; font-size: 11px; color: #5f7494; }
    .alert.info { border-color: #d9e8fb; border-left-color: #7da6dd; background: #f6f9ff; }
    .alert.critical { border-color: #f3dfe7; border-left-color: #d17889; background: #fff6f7; }
    .alert b { font-weight: 800; color: #344d6b; }
    .alert .sep { color: #98aac0; }
    .alert .cards-link { margin-left: auto; flex: none; }
    .kv { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 7px 0; border-bottom: 1px solid #ecf0f6; font-size: 11px; color: #5f7494; }
    .kv:last-child { border-bottom: none; }
    .kv .k { color: #5f7494; }
    .kv b { color: #233b5b; }
    .mini-h { margin: 6px 0 2px; font-size: 9px; font-weight: 800; color: #9b89b5; text-transform: uppercase; letter-spacing: .11em; }
    .act .row { display: flex; align-items: flex-start; gap: 10px; padding: 8px 0; border-bottom: 1px solid #ecf0f6; }
    .act .row:last-child { border-bottom: none; }
    .act .dt { width: 6px; height: 6px; border-radius: 50%; background: #a3b8e8; margin-top: 6px; flex: none; }
    .act .tx { font-size: 11px; color: #344d6b; line-height: 1.45; }
    .act .mt { margin-left: auto; font-size: 10px; color: #98aac0; white-space: nowrap; padding-left: 10px; }
    /* "All systems operational": the design's green note. Empty activity: the design's soft icon tile. */
    .all-ok { display: flex; align-items: flex-start; gap: 8px; margin: 2px 0; padding: 16px 12px; border-radius: 12px; border: 1px solid #d8f3e5; background: #eaf9f2;
      font-size: 11px; font-weight: 700; color: #287f67; }
    .all-ok::before { content: '✓'; display: grid; place-items: center; width: 17px; height: 17px; flex: none; border: 1.6px solid currentColor; border-radius: 50%; font-size: 9px; line-height: 1; }
    .all-ok.muted { flex-direction: column; align-items: center; justify-content: center; gap: 12px; min-height: 150px; padding: 0; border: 0; background: none; color: #8ea0b7; font-weight: 600; }
    .all-ok.muted::before { content: ''; width: 48px; height: 48px; border: 0; border-radius: 15px; background: linear-gradient(140deg, #e5f3ff, #f0ebfa, #fff0ed); }

    @media (max-width: 1190px) {
      /* Five cards as 3 + 2, both rows full width. */
      .kpis { grid-template-columns: repeat(6, minmax(0, 1fr)); }
      .kpi { grid-column: span 2; }
      .kpi:nth-child(n+4) { grid-column: span 3; }
      .dash-main { grid-template-columns: minmax(0, 1.25fr) minmax(0, .85fr); }
      .dash-bottom { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (max-width: 900px) { .dash-main { grid-template-columns: 1fr; grid-template-rows: 300px auto auto; } }
    @media (max-width: 700px) { .kpis, .dash-bottom { grid-template-columns: repeat(2, minmax(0, 1fr)); } .kpi, .kpi:nth-child(n+4) { grid-column: auto; min-height: 101px; } .kpi:last-child:nth-child(odd) { grid-column: 1 / -1; } .kpi .v { font-size: 22px; } }
    @media (max-width: 350px) { .kpis, .dash-bottom { grid-template-columns: 1fr; } .kpi:last-child:nth-child(odd) { grid-column: auto; } }
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
};
