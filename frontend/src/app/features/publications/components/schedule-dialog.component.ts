import { DatePipe } from '@angular/common';
import { Component, ElementRef, computed, input, signal, viewChild } from '@angular/core';
import { ApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { LocalSchedule } from '../models/publication.model';
import { browserTimeZone, formatOffset, offsetsFor, supportedTimeZones, toUtc } from '../models/time-zones';

/**
 * Local date + time + IANA zone picker. It previews the UTC instant, explains skipped DST times and asks which
 * occurrence of a repeated one is meant; the server still resolves and validates the time authoritatively.
 */
@Component({
  selector: 'app-schedule-dialog',
  imports: [DatePipe, ProblemBannerComponent],
  template: `
    <dialog #dialog class="sd">
      <form class="sd-form" (submit)="$event.preventDefault(); confirm()">
        <div class="sd-head">
          <h2 class="sd-title">{{ title() }}</h2>
          <button type="button" class="sd-x" aria-label="Close" (click)="close()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="M6 6l12 12M18 6 6 18"/></svg>
          </button>
        </div>
        <div class="sd-fields">
          <label class="sd-field">Date <input class="sd-input" type="date" required [value]="date()" (input)="date.set($any($event.target).value)" /></label>
          <label class="sd-field">Time <input class="sd-input" type="time" required [value]="time()" (input)="time.set($any($event.target).value)" /></label>
          <label class="sd-field sd-wide">Time zone
            <select class="sd-input" [value]="zone()" (change)="zone.set($any($event.target).value)">
              @for (z of zones; track z) { <option [value]="z" [selected]="z === zone()">{{ z }}</option> }
            </select>
          </label>
        </div>
        @switch (offsets().length) {
          @case (0) { <p class="banner error">This time does not exist in {{ zone() }} (clocks skip it for daylight saving). Choose another time.</p> }
          @case (1) { <p class="sd-utc">Publishes at {{ utcPreview() | date: 'medium' : 'UTC' }} UTC ({{ formatOffset(offsets()[0]) }})</p> }
          @default {
            <p class="banner info">This time occurs twice in {{ zone() }} because clocks go back. Which one?</p>
            <div class="checkbox-row">
              @for (o of offsets(); track o; let first = $first) {
                <label>
                  <input type="radio" name="occurrence" [checked]="chosenOffset() === o" (change)="chosenOffset.set(o)" />
                  {{ first ? 'First' : 'Second' }} occurrence ({{ formatOffset(o) }}) = {{ toUtc(date(), time(), o) | date: 'medium' : 'UTC' }} UTC
                </label>
              }
            </div>
          }
        }
        <app-problem-banner [problem]="problem()" />
        <div class="sd-actions">
          <button type="button" class="sd-btn" (click)="close()">Cancel</button>
          <button class="sd-btn sd-primary" type="submit" [disabled]="busy() || !canConfirm()">{{ busy() ? 'Saving…' : confirmLabel() }}</button>
        </div>
      </form>
    </dialog>
  `,
  // Self-contained (encapsulated, class-based) so pages with their own resets, like AI Studio, cannot restyle it.
  styles: `
    .sd { width: min(480px, 94vw); max-width: none; padding: 0; border: none; border-radius: 16px; background: #fff; color: #101828;
      box-shadow: 0 24px 60px rgba(15, 23, 42, .28); }
    .sd::backdrop { background: rgba(15, 23, 42, .45); }
    .sd-form { display: flex; flex-direction: column; gap: 16px; padding: 20px 22px 18px; margin: 0; }
    .sd-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .sd-title { margin: 0; font-size: 17px; font-weight: 700; color: #101828; }
    .sd-x { display: grid; place-items: center; width: 30px; height: 30px; min-height: 0; padding: 0; border: none; border-radius: 50%;
      background: #f1f4f9; color: #475467; cursor: pointer; }
    .sd-x:hover { background: #e4e9f2; color: #101828; }
    .sd-fields { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin: 0; padding: 0; border: none; }
    .sd-field { display: flex; flex-direction: column; gap: 6px; font-size: 11px; font-weight: 600; letter-spacing: .05em;
      text-transform: uppercase; color: #667085; }
    .sd-wide { grid-column: 1 / -1; }
    .sd-input { box-sizing: border-box; width: 100%; height: 40px; padding: 0 12px; border: 1px solid #e3e8ef; border-radius: 10px;
      background: #fff; font: inherit; font-size: 14px; font-weight: 500; letter-spacing: 0; text-transform: none; color: #101828; outline: none; }
    .sd-input:focus { border-color: #93b4fd; box-shadow: 0 0 0 3px rgba(37, 99, 235, .12); }
    .sd-utc { margin: 0; padding: 9px 12px; border-radius: 10px; background: #f5f8ff; color: #344054; font-size: 12.5px; }
    .sd-actions { display: flex; justify-content: flex-end; gap: 10px; padding-top: 14px; margin-top: 2px; border-top: 1px solid #eef1f5; }
    .sd-btn { height: 38px; min-height: 0; padding: 0 16px; border: 1px solid #e3e8ef; border-radius: 10px; background: #fff; color: #344054;
      font: inherit; font-size: 13.5px; font-weight: 600; cursor: pointer; display: inline-flex; align-items: center; }
    .sd-btn:hover { background: #f8fafc; }
    .sd-primary { border-color: #2563eb; background: #2563eb; color: #fff; }
    .sd-primary:hover { background: #1d4ed8; }
    .sd-btn:disabled { opacity: .55; cursor: not-allowed; }
    @media (max-width: 480px) { .sd-fields { grid-template-columns: 1fr; } }
  `,
})
export class ScheduleDialogComponent {
  /** Performs the server call; resolves to a problem to show, or null on success (the dialog then closes). */
  readonly submit = input.required<(when: LocalSchedule) => Promise<ApiProblem | null>>();

  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly zones = supportedTimeZones();
  protected readonly formatOffset = formatOffset;
  protected readonly toUtc = toUtc;

  protected readonly title = signal('Schedule');
  protected readonly confirmLabel = signal('Schedule');
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly date = signal('');
  protected readonly time = signal('09:00');
  protected readonly zone = signal(browserTimeZone());
  protected readonly chosenOffset = signal<number | null>(null);

  protected readonly offsets = computed(() => (this.date() && this.time() ? offsetsFor(this.date(), this.time(), this.zone()) : [-1]));
  protected readonly utcPreview = computed(() => toUtc(this.date(), this.time(), this.offsets()[0]));
  protected readonly canConfirm = computed(() => {
    const offsets = this.offsets();
    return !!this.date() && !!this.time() && (offsets.length === 1 || (offsets.length === 2 && offsets.includes(this.chosenOffset() ?? NaN)));
  });

  /** Opens with an existing local time (reschedule) or tomorrow 09:00 in the default zone. */
  open(title: string, confirmLabel: string, defaultZone: string, existing?: { localPublishAt: string | null; timeZoneId: string | null }): void {
    this.title.set(title);
    this.confirmLabel.set(confirmLabel);
    this.problem.set(null);
    this.chosenOffset.set(null);
    if (existing?.localPublishAt && existing.timeZoneId) {
      this.date.set(existing.localPublishAt.slice(0, 10));
      this.time.set(existing.localPublishAt.slice(11, 16));
      this.zone.set(existing.timeZoneId);
    } else {
      const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000);
      this.date.set(`${tomorrow.getFullYear()}-${String(tomorrow.getMonth() + 1).padStart(2, '0')}-${String(tomorrow.getDate()).padStart(2, '0')}`);
      this.time.set('09:00');
      this.zone.set(this.zones.includes(defaultZone) ? defaultZone : browserTimeZone());
    }
    this.dialog().nativeElement.showModal();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  protected async confirm(): Promise<void> {
    if (!this.canConfirm() || this.busy()) return;
    const offsets = this.offsets();
    this.busy.set(true);
    this.problem.set(null);
    try {
      const problem = await this.submit()({
        localDate: this.date(),
        localTime: `${this.time().slice(0, 5)}:00`,
        timeZone: this.zone(),
        utcOffsetMinutes: offsets.length === 2 ? this.chosenOffset() : null,
      });
      if (problem) this.problem.set(problem);
      else this.close();
    } finally {
      this.busy.set(false);
    }
  }
}
