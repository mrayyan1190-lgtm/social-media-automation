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
    <dialog #dialog>
      <form class="form-grid" (submit)="$event.preventDefault(); confirm()">
        <h2>{{ title() }}</h2>
        <fieldset>
          <label>Date <input type="date" required [value]="date()" (input)="date.set($any($event.target).value)" /></label>
          <label>Time <input type="time" required [value]="time()" (input)="time.set($any($event.target).value)" /></label>
          <label class="wide">Time zone
            <select [value]="zone()" (change)="zone.set($any($event.target).value)">
              @for (z of zones; track z) { <option [value]="z" [selected]="z === zone()">{{ z }}</option> }
            </select>
          </label>
        </fieldset>
        @switch (offsets().length) {
          @case (0) { <p class="banner error">This time does not exist in {{ zone() }} (clocks skip it for daylight saving). Choose another time.</p> }
          @case (1) { <p class="hint">= {{ utcPreview() | date: 'medium' : 'UTC' }} UTC ({{ formatOffset(offsets()[0]) }})</p> }
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
        <div class="actions">
          <button type="button" (click)="close()">Close</button>
          <button class="primary" type="submit" [disabled]="busy() || !canConfirm()">{{ busy() ? 'Saving…' : confirmLabel() }}</button>
        </div>
      </form>
    </dialog>
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
