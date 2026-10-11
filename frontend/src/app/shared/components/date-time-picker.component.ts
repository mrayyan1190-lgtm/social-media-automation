import { Component, ElementRef, computed, input, model, signal, viewChild } from '@angular/core';
import { ThemeOption, ThemeSelectComponent } from './theme-select.component';

const DAY = 864e5;
const WEEKDAYS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
const pad = (n: number) => String(n).padStart(2, '0');
/** yyyy-MM-dd ↔ UTC dates, so the calendar arithmetic never depends on the browser's zone. */
const toDate = (key: string) => new Date(`${key}T00:00:00Z`);
const toKey = (d: Date) => d.toISOString().slice(0, 10);
const localToday = () => {
  const d = new Date();
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
};

/**
 * The theme's date + time picker, replacing the browser's datetime-local popup (which cannot be styled). The value is
 * the same 'yyyy-MM-ddTHH:mm' string a datetime-local input gives, so it drops into existing forms. It only collects a
 * local wall-clock time; the server still resolves the zone and validates the schedule.
 */
@Component({
  selector: 'app-date-time-picker',
  imports: [ThemeSelectComponent],
  // Outside *clicks* close it (not pointerdown): closing on pointerdown shrank the card under the pointer, so the click
  // then landed on the dialog's backdrop and closed the whole dialog.
  host: { '(document:click)': 'outside($event)', '(keydown.escape)': 'escape($event)' },
  template: `
    @if (!inline()) {
    <button #trigger type="button" class="dtp-trigger" [class.empty]="!value()" aria-haspopup="dialog" [attr.aria-expanded]="open()" [attr.aria-label]="label()" (click)="toggle()">
      <svg class="dtp-ic" viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="3"/><path d="M7 3v4M17 3v4M3 10h18"/></svg>
      <span class="dtp-text">{{ display() || placeholder() }}</span>
      <svg class="dtp-chev" viewBox="0 0 24 24" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
    </button>
    }
    @if (inline() || open()) {
      <div #pop class="dtp-pop" [class.inline]="inline()" role="group" [attr.aria-label]="label()" (keydown.escape)="escape($event)">
        <div class="dtp-head">
          <span class="dtp-month">{{ monthTitle() }}</span>
          <span class="dtp-nav-wrap"><button type="button" class="dtp-link" (click)="pickDay(today)">Today</button><span class="dtp-nav">
            <button type="button" aria-label="Previous month" [disabled]="!canGoBack()" (click)="shift(-1)"><svg viewBox="0 0 24 24"><path d="m15 18-6-6 6-6"/></svg></button>
            <button type="button" aria-label="Next month" (click)="shift(1)"><svg viewBox="0 0 24 24"><path d="m9 18 6-6-6-6"/></svg></button>
          </span></span>
        </div>
        <div class="dtp-grid dtp-week">@for (d of weekdays; track d) { <span>{{ d }}</span> }</div>
        <div class="dtp-grid">
          @for (c of cells(); track c.key) {
            <button type="button" class="dtp-day" [class.out]="!c.inMonth" [class.today]="c.key === today" [class.on]="c.key === datePart()"
              [disabled]="c.key < minDate()" [attr.aria-label]="c.key" [attr.aria-pressed]="c.key === datePart()" (click)="pickDay(c.key)">{{ c.day }}</button>
          }
        </div>
        <div class="dtp-time">
          <span class="dtp-tlabel"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>Time</span>
          <app-theme-select class="dtp-sel" label="Hour" [options]="hours" [value]="hour()" (valueChange)="setTime($event, minute())" />
          <span class="dtp-colon">:</span>
          <app-theme-select class="dtp-sel" label="Minute" [options]="minutes()" [value]="minute()" (valueChange)="setTime(hour(), $event)" />
          @if (!inline()) { <button type="button" class="dtp-done" (click)="close(true)">Done</button> }
        </div>
      </div>
    }
  `,
  styles: `
    :host { position: relative; display: block; min-width: 0; font-family: 'DM Sans', system-ui, sans-serif; }
    button { font: inherit; margin: 0; min-height: 0; cursor: pointer; }
    button:disabled { cursor: default; }
    svg { display: block; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    .dtp-trigger { display: flex; align-items: center; gap: 10px; width: 100%; height: 48px; padding: 0 14px; border: 1px solid #DCE6F3; border-radius: 14px;
      background: linear-gradient(115deg, #fbfdff 0%, #f7f8ff 68%, #fff9f6 100%); color: #3D5D7E; font-size: 12.5px; font-weight: 700; text-align: left;
      box-shadow: 0 3px 10px rgba(61, 91, 133, .035); transition: border-color .18s, box-shadow .18s, background .18s; }
    .dtp-trigger:hover { border-color: #b2c6e6; background: linear-gradient(115deg, #f0f7ff, #f3effd, #fff5ef); }
    .dtp-trigger[aria-expanded="true"], .dtp-trigger:focus-visible { outline: none; border-color: #a7a5df; box-shadow: 0 0 0 3px rgba(164, 180, 227, .21), 0 9px 20px rgba(89, 103, 158, .07); }
    .dtp-trigger.empty { color: #99ACC3; font-weight: 600; }
    .dtp-ic { width: 17px; height: 17px; flex: none; color: #7C9AC4; stroke-width: 1.8; }
    .dtp-text { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dtp-chev { width: 14px; height: 14px; flex: none; color: #8499b8; transition: transform .17s; }
    .dtp-trigger[aria-expanded="true"] .dtp-chev { transform: rotate(180deg); }

    /* Opens in place under the field, at the field's width (inside its card), pushing the content below down. */
    .dtp-pop { position: relative; width: 100%; margin-top: 8px; font-family: 'DM Sans', system-ui, sans-serif; padding: 12px 12px 10px; border: 1px solid #dbe4f3; border-radius: 20px; color: #2B4868;
      background: linear-gradient(140deg, rgba(253, 254, 255, .99), rgba(244, 245, 255, .99) 70%, rgba(255, 247, 244, .99));
      box-shadow: 0 10px 26px rgba(40, 66, 116, .1), 0 2px 7px rgba(40, 66, 116, .05); animation: dtpIn .16s ease-out; }
    @keyframes dtpIn { from { opacity: 0; transform: translateY(-4px) scale(.985); } to { opacity: 1; transform: none; } }
    .dtp-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; }
    .dtp-pop.inline { margin-top: 0; box-shadow: none; animation: none; }
    .dtp-nav-wrap { display: flex; align-items: center; gap: 6px; }
    .dtp-month { font-family: Manrope, 'DM Sans', sans-serif; font-size: 15px; font-weight: 800; letter-spacing: -.04em; color: #203B5C; }
    .dtp-nav { display: flex; gap: 4px; padding: 3px; border-radius: 999px; background: rgba(237, 243, 252, .9); }
    .dtp-nav button { display: grid; place-items: center; width: 28px; height: 28px; padding: 0; border: 0; border-radius: 999px; background: none; color: #5C7596; transition: background .15s; }
    .dtp-nav button:hover:not(:disabled) { background: #fff; }
    .dtp-nav button:disabled { opacity: .35; }
    .dtp-nav svg { width: 15px; height: 15px; stroke-width: 2.2; }
    .dtp-grid { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 2px; }
    .dtp-week span { padding: 2px 0 4px; text-align: center; font-size: 9.5px; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; color: #8094AF; }
    .dtp-day { height: 30px; padding: 0; border: 1px solid transparent; border-radius: 10px; background: none; color: #304C6D; font-size: 12px; font-weight: 700; transition: background .14s, color .14s; }
    .dtp-day:hover:not(:disabled):not(.on) { background: #e9f1fd; color: #275483; }
    .dtp-day.out { color: #A6B4C8; font-weight: 600; }
    .dtp-day.today { border-color: #A8C4EC; }
    .dtp-day.on { background: linear-gradient(135deg, #234b80, #456fbc 62%, #756ab9); color: #fff; box-shadow: 0 6px 14px rgba(54, 91, 158, .25); }
    .dtp-day:disabled { color: #C8D2DF; text-decoration: line-through; text-decoration-color: #DCE3EC; }
    .dtp-time { display: flex; align-items: center; gap: 6px; margin-top: 8px; padding-top: 10px; border-top: 1px solid #E6ECF5; }
    .dtp-tlabel { display: flex; align-items: center; gap: 6px; margin-right: auto; font-size: 11px; font-weight: 800; color: #57718F; }
    .dtp-tlabel svg { width: 15px; height: 15px; color: #7C9AC4; stroke-width: 1.9; }
    .dtp-sel { width: 84px; }
    .dtp-colon { font-weight: 800; color: #8094AF; }
    .dtp-link { padding: 6px 4px; border: 0; background: none; color: #4F81BA; font-size: 11.5px; font-weight: 800; }
    .dtp-link:hover { color: #1F568F; }
    .dtp-done { height: 36px; margin-left: 4px; padding: 0 16px; border: 0; border-radius: 999px; color: #fff; font-size: 11.5px; font-weight: 800;
      background: linear-gradient(113deg, #234b80, #456fbc 62%, #756ab9); box-shadow: 0 7px 16px rgba(54, 91, 158, .22); }
    .dtp-done:hover { filter: brightness(1.07); }
    button:focus-visible { outline: 2px solid #93BEEB; outline-offset: 2px; }
    @media (prefers-reduced-motion: reduce) { .dtp-pop { animation: none; } }
  `,
})
export class DateTimePickerComponent {
  /** 'yyyy-MM-ddTHH:mm' (as a datetime-local input), or '' when nothing is chosen. */
  readonly value = model('');
  readonly label = input('Publication date and time');
  readonly placeholder = input('Choose a date and time');
  /** First selectable day (yyyy-MM-dd); defaults to today, as only future publications can be scheduled. */
  readonly min = input<string | null>(null);
  /** Always open: just the calendar and time, no field to expand (used as the whole content of a schedule dialog). */
  readonly inline = input(false);

  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
  private readonly pop = viewChild<ElementRef<HTMLElement>>('pop');
  protected readonly open = signal(false);
  protected readonly today = localToday();
  protected readonly weekdays = WEEKDAYS;
  protected readonly hours: ThemeOption[] = Array.from({ length: 24 }, (_, h) => ({ value: pad(h), label: pad(h) }));
  /** The month on screen (yyyy-MM). */
  protected readonly month = signal(this.today.slice(0, 7));
  private synced = false;

  protected readonly minDate = computed(() => this.min() ?? this.today);
  protected readonly datePart = computed(() => this.value().slice(0, 10));
  protected readonly hour = computed(() => this.value().slice(11, 13) || '09');
  protected readonly minute = computed(() => this.value().slice(14, 16) || '00');
  /** Five-minute steps, plus the current minute when it is off the grid (e.g. an existing schedule). */
  protected readonly minutes = computed<ThemeOption[]>(() => {
    const steps = Array.from({ length: 12 }, (_, i) => pad(i * 5));
    if (!steps.includes(this.minute())) steps.push(this.minute());
    return steps.sort().map((m) => ({ value: m, label: m }));
  });
  protected readonly canGoBack = computed(() => this.month() > this.minDate().slice(0, 7));
  protected readonly monthTitle = computed(() =>
    new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(toDate(`${this.month()}-01`)));
  /** Six weeks, Monday first (as the Planner). */
  protected readonly cells = computed(() => {
    if (!this.synced && this.datePart()) {
      this.synced = true;
      queueMicrotask(() => this.month.set(this.datePart().slice(0, 7)));
    }
    const first = toDate(`${this.month()}-01`);
    const start = first.getTime() - ((first.getUTCDay() + 6) % 7) * DAY;
    return Array.from({ length: 42 }, (_, i) => {
      const key = toKey(new Date(start + i * DAY));
      return { key, day: Number(key.slice(8)), inMonth: key.startsWith(this.month()) };
    });
  });
  protected readonly display = computed(() => {
    if (!this.datePart()) return '';
    const day = new Intl.DateTimeFormat('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' }).format(toDate(this.datePart()));
    return `${day} · ${this.hour()}:${this.minute()}`;
  });

  protected toggle(): void {
    if (this.open()) return this.close();
    this.month.set((this.datePart() || this.minDate()).slice(0, 7));
    this.open.set(true);
  }

  close(focus = false): void {
    if (!this.open()) return;
    this.open.set(false);
    if (focus) this.trigger()?.nativeElement.focus();
  }

  protected escape(event: Event): void {
    if (!this.open()) return;
    // Close only the picker, not the dialog around it (preventDefault also stops a native <dialog> from cancelling).
    event.stopPropagation();
    event.preventDefault();
    this.close(true);
  }

  protected outside(event: PointerEvent): void {
    const target = event.target as Node;
    if (this.open() && !this.trigger()?.nativeElement.contains(target) && !this.pop()?.nativeElement.contains(target)) this.close();
  }

  protected shift(delta: number): void {
    const [y, m] = this.month().split('-').map(Number);
    this.month.set(new Date(Date.UTC(y, m - 1 + delta, 1)).toISOString().slice(0, 7));
  }

  protected pickDay(key: string): void {
    this.month.set(key.slice(0, 7));
    this.value.set(`${key}T${this.hour()}:${this.minute()}`);
  }

  protected setTime(hour: string, minute: string): void {
    this.value.set(`${this.datePart() || this.minDate()}T${hour}:${minute}`);
  }
}
