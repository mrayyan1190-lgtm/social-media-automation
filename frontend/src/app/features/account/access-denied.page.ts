import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-access-denied-page',
  imports: [RouterLink],
  template: `
    <main class="login">
      <section class="card">
        <h1>Access denied</h1>
        <p class="muted">You do not have permission to open this page.</p>
        <a routerLink="/home">Back to Home</a>
      </section>
    </main>
  `,
})
export class AccessDeniedPage {}
