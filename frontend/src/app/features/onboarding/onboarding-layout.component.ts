import { Component, ViewEncapsulation, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OnboardingMenuComponent } from './onboarding-menu.component';

/** The onboarding page frame: header with account menu, progress bar, and the step's intro text beside the step form. */
@Component({
  selector: 'app-onboarding-layout',
  imports: [RouterLink, OnboardingMenuComponent],
  templateUrl: './onboarding-layout.component.html',
  styleUrl: './onboarding.css',
  // The design's surface classes are used by the projected step forms and the social cards too.
  encapsulation: ViewEncapsulation.None,
})
export class OnboardingLayoutComponent {
  readonly step = input.required<1 | 2 | 3>();
}
