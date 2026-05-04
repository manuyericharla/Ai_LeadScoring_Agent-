import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from './workspace/sidebar/sidebar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent],
  template: `
    <div class="app-frame" [class.drawer-open]="drawerOpen">
      <header class="mobile-top-bar">
        <button type="button" class="mobile-menu-trigger" (click)="drawerOpen = !drawerOpen" aria-label="Menu">
          <svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.5">
            <path stroke-linecap="round" d="M3.75 6.75h16.5M3.75 12h16.5M3.75 17.25h16.5" />
          </svg>
        </button>
        <span class="mobile-top-bar-brand">LeadScoring</span>
      </header>
      <div class="drawer-backdrop" (click)="drawerOpen = false" aria-hidden="true"></div>
      <app-sidebar [drawerOpen]="drawerOpen" (drawerClose)="drawerOpen = false" />
      <main class="app-main">
        <router-outlet />
      </main>
    </div>
  `
})
export class AppComponent {
  drawerOpen = false;
}
