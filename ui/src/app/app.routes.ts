import { Routes } from '@angular/router';
import { dashboardRoute } from './features/dashboard/dashboard.routes';
import { leadsRoute } from './features/leads/leads.routes';
import { settingsChildRoutes } from './features/settings/settings.routes';

export const routes: Routes = [
  {
    path: 'email',
    loadComponent: () =>
      import('./email-capture/email-capture.component').then((m) => m.EmailCaptureComponent)
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  dashboardRoute,
  leadsRoute,
  { path: 'settings', children: settingsChildRoutes },
  { path: '**', redirectTo: 'dashboard' }
];
