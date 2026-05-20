import { Routes } from '@angular/router';
import { dashboardRoute } from './features/dashboard/dashboard.routes';
import { leadsRoute } from './features/leads/leads.routes';
import { settingsChildRoutes } from './features/settings/settings.routes';
import { authGuard, guestGuard } from './shared/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/auth-page.component').then((m) => m.AuthPageComponent)
  },
  {
    path: 'signup',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/auth-page.component').then((m) => m.AuthPageComponent)
  },
  {
    path: 'email',
    loadComponent: () =>
      import('./email-capture/email-capture.component').then((m) => m.EmailCaptureComponent)
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { ...dashboardRoute, canActivate: [authGuard] },
  {
    path: 'leads/:leadId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/leads/lead-detail/lead-detail.component').then((m) => m.LeadDetailComponent)
  },
  { ...leadsRoute, canActivate: [authGuard] },
  { path: 'settings', canActivate: [authGuard], children: settingsChildRoutes },
  { path: '**', redirectTo: 'dashboard' }
];
