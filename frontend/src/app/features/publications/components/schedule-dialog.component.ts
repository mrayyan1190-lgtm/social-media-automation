import { DatePipe } from '@angular/common';
import { Component, ElementRef, computed, input, signal, viewChild } from '@angular/core';
import { ApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { DateTimePickerComponent } from '../../../shared/components/date-time-picker.component';
import { LocalSchedule } from '../models/publication.model';
import { browserTimeZone, formatOffset, offsetsFor, supportedTimeZones, toUtc } from '../models/time-zones';

/**
 * Local date + time + IANA zone picker. It previews the UTC instant, explains skipped DST times and asks which
 * occurrence of a repeated one is meant; the server still resolves and validates the time authoritatively.
 */
@Component({
  selector: 'app-schedule-dialog',
  imports: [DatePipe, ProblemBannerComponent, DateTimePickerComponent],
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
          <div class="sd-field sd-wide">Date and time
            <app-date-time-picker [inline]="true" [value]="date() && time() ? date() + 'T' + time().slice(0, 5) : ''" (valueChange)="setWhen($event)" />
          </div>
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
    /* margin:auto keeps the dialog centred (Tailwind's base resets every margin to 0). */
    .sd { margin: auto; width: min(480px, 94vw); max-width: none; padding: 0; border: 1px solid #fff; border-radius: 26px; background: #FBFCFF; color: #203B59; font-family: 'DM Sans', system-ui, sans-serif;
      box-shadow: 0 26px 80px rgba(32, 54, 84, .30); }
    .sd::backdrop { background: linear-gradient(135deg, rgba(16, 38, 56, .82) 0%, rgba(33, 36, 66, .8) 50%, rgba(56, 32, 46, .8) 100%); backdrop-filter: blur(8px); }
    .sd-form { display: flex; flex-direction: column; gap: 16px; padding: 20px 22px 18px; margin: 0; }
    .sd-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .sd-title { margin: 0; font-family: Manrope, 'DM Sans', sans-serif; font-size: 21px; font-weight: 800; letter-spacing: -.04em; color: #203B59; }
    .sd-x { display: grid; place-items: center; width: 30px; height: 30px; min-height: 0; padding: 0; border: none; border-radius: 50%;
      background: #EEF3FB; color: #69809F; cursor: pointer; }
    .sd-x:hover { background: #E0ECF8; color: #203B59; }
    .sd-fields { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin: 0; padding: 0; border: none; }
    .sd-field { display: flex; flex-direction: column; gap: 6px; font-size: 11px; font-weight: 600; letter-spacing: .05em;
      text-transform: uppercase; color: #8095B1; }
    .sd-wide { grid-column: 1 / -1; }
    .sd-input { box-sizing: border-box; width: 100%; height: 40px; padding: 0 12px; border: 1px solid light-dark(#e3e8ef, #283546); border-radius: 10px;
      background: var(--surface); font: inherit; font-size: 14px; font-weight: 500; letter-spacing: 0; text-transform: none; color: light-dark(#101828, #e0e5ee); outline: none; }
    .sd-input:focus { border-color: light-dark(#93b4fd, #223d77); box-shadow: 0 0 0 3px rgba(37, 99, 235, .12); }
    .sd-utc { margin: 0; padding: 10px 13px; border-radius: 13px; background: #F3F7FD; color: #58718E; font-size: 12px; font-weight: 600; }
    .sd-actions { display: flex; justify-content: flex-end; gap: 10px; padding-top: 14px; margin-top: 2px; border-top: 1px solid #E7EDF5; }
    .sd-btn { height: 40px; min-height: 0; padding: 0 18px; border: 1px solid #DBE6F3; border-radius: 999px; background: #fff; color: #6A7F9B;
      font: inherit; font-size: 12px; font-weight: 800; cursor: pointer; display: inline-flex; align-items: center; }
    .sd-btn:hover { background: #F5F9FF; }
    .sd-primary { border-color: transparent; background: linear-gradient(113deg, #234b80, #456fbc 62%, #756ab9); color: #fff; box-shadow: 0 9px 22px rgba(54, 91, 158, .2); }
    .sd-primary:hover { background: linear-gradient(113deg, #234b80, #456fbc 62%, #756ab9); filter: brightness(1.07); }
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

  /** From the picker: 'yyyy-MM-ddTHH:mm'. */
  protected setWhen(value: string): void {
    this.date.set(value.slice(0, 10));
    this.time.set(value.slice(11, 16));
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
