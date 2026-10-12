import { Component, DestroyRef, ElementRef, afterNextRender, computed, effect, forwardRef, inject, input, model, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface ThemeOption {
  value: string;
  label: string;
  /** Shown as the trigger text when selected (a placeholder) but not offered in the list. */
  disabled?: boolean;
}

/**
 * The design's themed dropdown (no OS-blue popup): a trigger button and a fixed listbox placed under it (above it when
 * there is no room). Closes on outside press, Escape, Tab, scroll and resize; arrow keys move between options.
 * Options come from [options], or from native <option> elements written inside it (a drop-in for <select>), and it
 * works with [(value)] or ngModel / form validation.
 */
@Component({
  selector: 'app-theme-select',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => ThemeSelectComponent), multi: true }],
  host: {
    '(document:pointerdown)': 'outside($event)',
    '(window:resize)': 'close()',
  },
  template: `
    <span #slot hidden><ng-content /></span>
    <button #trigger type="button" class="theme-select-trigger" [attr.aria-label]="label()" aria-haspopup="listbox" [attr.aria-expanded]="!!place()"
      [disabled]="disabled()" (click)="toggle()" (keydown)="key($event)">
      <span class="truncate">{{ current() }}</span>
      <svg class="theme-select-chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
    </button>
    @if (place(); as p) {
      <div #menu class="theme-select-menu" role="listbox" [attr.aria-label]="label()" [style.left.px]="p.left" [style.top.px]="p.top" [style.width.px]="p.width" (keydown)="key($event)">
        @for (o of items(); track o.value) { @if (!o.disabled) {
          <button type="button" class="theme-select-option" role="option" [attr.aria-selected]="o.value === value()" [attr.data-value]="o.value" (click)="pick(o.value)">
            <span>{{ o.label }}</span>
            <svg class="theme-select-check" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 5 5L20 7"/></svg>
          </button>
        } }
      </div>
    }
  `,
  styles: `
    /* No min-width here: the page sizes each dropdown with Tailwind classes on the host. */
    :host { position: relative; display: block; }
    .truncate { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .theme-select-trigger { display: flex; align-items: center; justify-content: space-between; gap: 11px; width: 100%; height: 42px; padding: 0 12px 0 15px; border: 1px solid #dbe6f2;
      border-radius: 14px; background: linear-gradient(115deg, #fbfdff 0%, #f7f8ff 68%, #fff9f6 100%); font: inherit; font-size: 12px; font-weight: 700; color: #385778;
      box-shadow: 0 3px 10px rgba(61, 91, 133, .035); text-align: left; cursor: pointer; transition: border-color .18s, background .18s, box-shadow .18s; }
    .theme-select-trigger:disabled { opacity: .6; cursor: not-allowed; }
    .theme-select-trigger:hover:not(:disabled) { border-color: #b2c6e6; background: linear-gradient(115deg, #f0f7ff, #f3effd, #fff5ef); }
    .theme-select-trigger[aria-expanded="true"], .theme-select-trigger:focus-visible { outline: none; border-color: #a7a5df; box-shadow: 0 0 0 3px rgba(164, 180, 227, .21), 0 9px 20px rgba(89, 103, 158, .07); }
    .theme-select-chevron { flex-shrink: 0; width: 14px; height: 14px; color: #8499b8; transition: transform .17s ease; }
    .theme-select-trigger[aria-expanded="true"] .theme-select-chevron { transform: rotate(180deg); }
    .theme-select-menu { position: fixed; z-index: 2000; font-family: 'DM Sans', system-ui, sans-serif; overflow-y: auto; max-height: min(320px, 55vh); padding: 6px; border: 1px solid #dbe4f3; border-radius: 17px;
      background: linear-gradient(140deg, rgba(253, 254, 255, .99), rgba(244, 245, 255, .99) 70%, rgba(255, 247, 244, .99));
      box-shadow: 0 17px 42px rgba(40, 66, 116, .17), 0 2px 9px rgba(40, 66, 116, .06); animation: themeSelectIn .15s ease-out; scrollbar-width: thin; scrollbar-color: #c2cdea transparent; }
    .theme-select-option { display: flex; align-items: center; justify-content: space-between; gap: 12px; width: 100%; min-height: 38px; margin: 1px 0; padding: 8px 11px;
      border-radius: 11px; border: 1px solid transparent; background: transparent; font: inherit; font-size: 12px; font-weight: 600; color: #516b8b; text-align: left;
      white-space: nowrap; cursor: pointer; transition: background .14s, color .14s; }
    .theme-select-option:hover, .theme-select-option:focus-visible { background: #e9f1fd; color: #275483; outline: none; }
    .theme-select-option[aria-selected="true"] { border-color: #dce4f7; background: linear-gradient(100deg, #e4efff, #f0eafd 74%, #fff0e9); color: #2e5689; font-weight: 800; }
    .theme-select-check { width: 15px; height: 15px; flex: none; opacity: 0; stroke: #567ac4; stroke-width: 2.5; fill: none; }
    .theme-select-option[aria-selected="true"] .theme-select-check { opacity: 1; }
    @keyframes themeSelectIn { from { opacity: 0; transform: translateY(-4px) scale(.98); } to { opacity: 1; transform: translateY(0) scale(1); } }
    @media (prefers-reduced-motion: reduce) { .theme-select-menu { animation: none; } }
  `,
})
export class ThemeSelectComponent implements ControlValueAccessor {
  readonly options = input<ThemeOption[] | null>(null);
  readonly label = input('Choose option');
  readonly value = model('');

  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly slot = viewChild.required<ElementRef<HTMLElement>>('slot');
  /** Native <option>s projected inside, kept in sync as @for / bindings change them. */
  private readonly projected = signal<ThemeOption[]>([]);
  protected readonly items = computed(() => this.options() ?? this.projected());
  protected readonly disabled = signal(false);
  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};
  private readonly menu = viewChild<ElementRef<HTMLElement>>('menu');
  protected readonly place = signal<{ left: number; top: number; width: number } | null>(null);
  protected readonly current = computed(() => this.items().find((o) => o.value === this.value())?.label ?? 'Select');

  constructor() {
    // Capture: any scrolling container (the page sits in one) would leave the fixed menu behind.
    const onScroll = (event: Event) => {
      if (!this.menu()?.nativeElement.contains(event.target as Node)) this.close();
    };
    document.addEventListener('scroll', onScroll, true);
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => document.removeEventListener('scroll', onScroll, true));
    afterNextRender(() => {
      const slot = this.slot().nativeElement;
      const read = () => this.projected.set([...slot.querySelectorAll('option')].map((o) => ({ value: o.value, label: o.textContent?.trim() ?? '', disabled: o.disabled })));
      read();
      const observer = new MutationObserver(read);
      observer.observe(slot, { subtree: true, childList: true, characterData: true, attributes: true });
      destroyRef.onDestroy(() => observer.disconnect());
    });
    // The open menu moves to <body>: a page ancestor with backdrop-filter/transform would otherwise become the fixed
    // menu's containing block (shifting it off the trigger) and a later card would paint over it.
    effect(() => {
      const el = this.menu()?.nativeElement;
      if (el && el.parentElement !== document.body) document.body.appendChild(el);
    });
  }

  protected toggle(): void {
    if (this.place()) return this.close();
    const rect = this.trigger().nativeElement.getBoundingClientRect();
    const width = Math.max(rect.width, Math.min(220, innerWidth - 24));
    // ~41px per option plus padding; open upwards when the list would run off the bottom.
    const height = Math.min(this.items().filter((o) => !o.disabled).length * 41 + 14, 320, innerHeight * 0.55);
    let top = rect.bottom + 7;
    if (top + height > innerHeight - 8) top = rect.top - height - 7 > 8 ? rect.top - height - 7 : Math.max(8, innerHeight - height - 8);
    this.place.set({ left: Math.max(8, Math.min(rect.left, innerWidth - width - 8)), top, width });
  }

  close(focus = false): void {
    if (!this.place()) return;
    this.place.set(null);
    if (focus) this.trigger().nativeElement.focus();
  }

  protected pick(value: string): void {
    this.value.set(value);
    this.onChange(value);
    this.onTouched();
    this.close(true);
  }

  writeValue(value: unknown): void {
    this.value.set(value == null ? '' : String(value));
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  protected outside(event: PointerEvent): void {
    const target = event.target as Node;
    if (this.place() && !this.trigger().nativeElement.contains(target) && !this.menu()?.nativeElement.contains(target)) this.close();
  }

  protected key(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.place()) {
      event.preventDefault();
      event.stopPropagation();
      return this.close(true);
    }
    if (event.key === 'Tab') return this.close();
    const step = event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0;
    if (!step && !(event.target === this.trigger().nativeElement && (event.key === 'Enter' || event.key === ' '))) return;
    event.preventDefault();
    if (!this.place()) this.toggle();
    // The menu renders on the next change detection; move focus once it exists.
    setTimeout(() => {
      const items = [...(this.menu()?.nativeElement.querySelectorAll<HTMLElement>('[role="option"]') ?? [])];
      if (!items.length) return;
      const at = items.indexOf(document.activeElement as HTMLElement);
      const from = at >= 0 ? at : items.findIndex((el) => el.dataset['value'] === this.value());
      items[(from + (step || 1) + items.length) % items.length]?.focus();
    });
  }
}
