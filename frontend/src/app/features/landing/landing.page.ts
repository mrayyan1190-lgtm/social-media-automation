import { Component, DestroyRef, ElementRef, afterEveryRender, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Public marketing page at "/". Illustrative product previews only; no API calls. */
@Component({
  selector: 'app-landing-page',
  imports: [RouterLink],
  templateUrl: './landing.page.html',
  host: {
    '(window:scroll)': 'onScroll()',
    '(document:keydown.escape)': 'menuOpen.set(false)',
    '(document:click)': 'closeMenuOutside($event)',
  },
})
export class LandingPage {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly reducedMotionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');

  protected readonly menuOpen = signal(false);
  protected readonly scrolled = signal(window.scrollY > 24);
  /** Navbar tucked away while scrolling down. */
  protected readonly navHidden = signal(false);
  private lastY = window.scrollY;
  protected readonly step = signal(0);
  protected readonly reducedMotion = signal(this.reducedMotionQuery.matches);
  protected readonly paused = signal(this.reducedMotionQuery.matches);
  protected readonly year = new Date().getFullYear();
  protected readonly copies = [0, 1];

  constructor() {
    const onMotionChange = (e: MediaQueryListEvent) => this.reducedMotion.set(e.matches);
    this.reducedMotionQuery.addEventListener('change', onMotionChange);

    // Sections fade in once on screen. Checked after every render (not once), so sections that appear later, such as
    // after a live reload during development, are never left invisible.
    let observer: IntersectionObserver | undefined;
    afterEveryRender(() => {
      const fresh = this.host.nativeElement.querySelectorAll('.reveal:not(.is-visible):not([data-reveal])');
      if (!fresh.length) return;
      if (this.reducedMotion() || !('IntersectionObserver' in window)) {
        fresh.forEach((el) => el.classList.add('is-visible'));
        return;
      }
      observer ??= new IntersectionObserver((entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            observer!.unobserve(entry.target);
          }
        }
      }, { threshold: .1, rootMargin: '0px 0px -30px 0px' });
      fresh.forEach((el) => {
        el.setAttribute('data-reveal', '');
        observer!.observe(el);
      });
    });

    inject(DestroyRef).onDestroy(() => {
      observer?.disconnect();
      this.reducedMotionQuery.removeEventListener('change', onMotionChange);
    });
  }

  protected onScroll(): void {
    const y = window.scrollY;
    this.scrolled.set(y > 24);
    const delta = y - this.lastY;
    // A few pixels of tolerance so tiny jitters do not flicker it.
    if (y < 80 || this.menuOpen()) this.navHidden.set(false);
    else if (delta > 6) this.navHidden.set(true);
    else if (delta < -6) this.navHidden.set(false);
    if (Math.abs(delta) > 6 || y < 80) this.lastY = y;
  }

  protected closeMenuOutside(event: Event): void {
    if (this.menuOpen() && !(event.target as Element).closest('header')) this.menuOpen.set(false);
  }

  /** Subtle pointer-driven depth on the hero cards; the CSS float animation runs independently. */
  protected tilt(event: PointerEvent, scene: HTMLElement): void {
    if (this.reducedMotion() || !window.matchMedia('(pointer: fine)').matches) return;
    const rect = scene.getBoundingClientRect();
    const x = (event.clientX - rect.left - rect.width / 2) / rect.width;
    const y = (event.clientY - rect.top - rect.height / 2) / rect.height;
    scene.querySelectorAll<HTMLElement>('.hero-layer').forEach((layer, index) => {
      const depth = 7 + (index % 3) * 7;
      layer.style.translate = `${(x * depth).toFixed(1)}px ${(y * depth).toFixed(1)}px`;
    });
  }

  protected resetTilt(scene: HTMLElement): void {
    scene.querySelectorAll<HTMLElement>('.hero-layer').forEach((layer) => (layer.style.translate = '0px 0px'));
  }
}
