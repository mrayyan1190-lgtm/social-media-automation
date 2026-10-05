import { Component, input } from '@angular/core';
import { SocialPlatform } from '../../features/campaigns/models/campaign.model';

/** Small platform mark with an accessible name, optionally followed by the platform label. */
@Component({
  selector: 'app-platform-icon',
  template: `
    <span class="platform-mark" [class]="'platform-mark ' + platform().toLowerCase()" [attr.aria-label]="platform()" role="img">
      @switch (platform()) {
        @case ('LinkedIn') { in }
        @case ('Facebook') { f }
        @default { <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6"><rect x="3" y="3" width="18" height="18" rx="5"/><circle cx="12" cy="12" r="4"/></svg> }
      }
    </span>
    @if (label()) { <span class="platform-label">{{ platform() }}</span> }
  `,
  styles: `:host { display: inline-flex; align-items: center; gap: 0.35rem; }`,
})
export class PlatformIconComponent {
  readonly platform = input.required<SocialPlatform>();
  readonly label = input(false);
}
