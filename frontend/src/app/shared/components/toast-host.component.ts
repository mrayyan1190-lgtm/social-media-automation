import { Component, inject } from '@angular/core';
import { ToastService } from '../../core/services/toast.service';

/** Renders popups at the top of every page, above the page content. */
@Component({
  selector: 'app-toast-host',
  template: `
    <div class="toast-stack" aria-live="assertive">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast" [class.success]="t.tone === 'success'" role="alert">
          <span class="toast-icon" aria-hidden="true">
            @if (t.tone === 'error') {
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9.5"/><path d="M12 7.5v5.2M12 16.3v.1"/></svg>
            } @else {
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9.5"/><path d="m8 12.3 2.7 2.7L16 9.6"/></svg>
            }
          </span>
          <span class="toast-text">{{ t.message }}</span>
          @if (t.action; as a) { <button type="button" class="toast-action" (click)="a.run(); toasts.dismiss(t.id)">{{ a.label }}</button> }
          <button type="button" class="toast-close" aria-label="Close" (click)="toasts.dismiss(t.id)">×</button>
        </div>
      }
    </div>
  `,
  styles: `
    .toast-stack { position: fixed; top: 16px; left: 50%; transform: translateX(-50%); z-index: 2000;
      display: flex; flex-direction: column; gap: 10px; width: min(560px, calc(100vw - 32px)); pointer-events: none; }
    .toast { pointer-events: auto; display: flex; align-items: center; gap: 12px; padding: 12px 14px; border-radius: 12px;
      background: #fff; border: 1px solid #f3c7c3; border-left: 4px solid #d92d20; color: #1c2430;
      box-shadow: 0 12px 32px rgba(16, 24, 40, 0.18); font: 500 14px/1.45 system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
      animation: toast-in 0.18s ease-out; }
    .toast.success { border-color: #bfe3cb; border-left-color: #16a34a; }
    .toast-icon { flex: none; width: 22px; height: 22px; color: #d92d20; }
    .toast.success .toast-icon { color: #16a34a; }
    .toast-icon svg { width: 22px; height: 22px; }
    .toast-text { flex: 1; min-width: 0; }
    .toast-action { flex: none; border: 1px solid #d5dce7; background: #fff; border-radius: 8px; padding: 5px 10px; font: inherit; font-size: 13px; cursor: pointer; }
    .toast-close { flex: none; border: none; background: none; font-size: 20px; line-height: 1; color: #6b778a; cursor: pointer; padding: 0 2px; }
    @keyframes toast-in { from { opacity: 0; transform: translateY(-8px); } to { opacity: 1; transform: none; } }
  `,
})
export class ToastHostComponent {
  protected readonly toasts = inject(ToastService);
}
