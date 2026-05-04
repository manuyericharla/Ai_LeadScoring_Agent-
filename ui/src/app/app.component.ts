import { Component, OnDestroy, OnInit } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, Subscription } from 'rxjs';
import { SidebarComponent } from './workspace/sidebar/sidebar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent],
  template: `
    <div
      class="app-frame"
      [class.drawer-open]="drawerOpen"
      [class.app-frame--no-sidebar]="!showShell">
      @if (showShell) {
        <header class="mobile-top-bar">
          <button type="button" class="mobile-menu-trigger" (click)="drawerOpen = !drawerOpen" aria-label="Menu">
            <svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.5">
              <path stroke-linecap="round" d="M3.75 6.75h16.5M3.75 12h16.5M3.75 17.25h16.5" />
            </svg>
          </button>
          <img class="mobile-top-bar-logo" src="/lsa-logo.png" alt="" width="28" height="28" decoding="async" />
          <span class="mobile-top-bar-brand">LeadScoring</span>
        </header>
        <div class="drawer-backdrop" (click)="drawerOpen = false" aria-hidden="true"></div>
        <app-sidebar [drawerOpen]="drawerOpen" (drawerClose)="drawerOpen = false" />
      }
      <main class="app-main">
        <router-outlet />
      </main>
    </div>
  `
})
export class AppComponent implements OnInit, OnDestroy {
  drawerOpen = false;
  showShell = true;
  private navSub?: Subscription;
  private readonly gateQueryKeys = new Set(['src', 'redirect']);

  constructor(private readonly router: Router) {
    this.applyShellForUrl(this.router.url);
  }

  ngOnInit(): void {
    this.ensureEmailGateRoute(this.router.url);
    this.navSub = this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(() => {
        this.ensureEmailGateRoute(this.router.url);
        this.applyShellForUrl(this.router.url);
      });
  }

  ngOnDestroy(): void {
    this.navSub?.unsubscribe();
  }

  private applyShellForUrl(fullUrl: string): void {
    const pathOnly = fullUrl.split('?')[0].split('#')[0];
    this.showShell = pathOnly !== '/email';
  }
  private ensureEmailGateRoute(fullUrl: string): void {
    const [pathOnly, queryRaw] = fullUrl.split('?');
    if (pathOnly === '/email' || !queryRaw) {
      return;
    }

    const params = new URLSearchParams(queryRaw);
    const hasAllGateKeys = [...this.gateQueryKeys].every((k) => !!params.get(k)?.trim());
    if (!hasAllGateKeys) {
      return;
    }

    void this.router.navigate(['/email'], {
      queryParams: Object.fromEntries(params.entries()),
      replaceUrl: true
    });
  }
}
