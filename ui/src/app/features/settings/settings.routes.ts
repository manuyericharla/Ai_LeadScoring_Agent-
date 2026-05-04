import type { Routes } from '@angular/router';
import { WorkspaceComponent } from '../../workspace/workspace.component';

export const settingsChildRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'company-config' },
  {
    path: 'company-config',
    component: WorkspaceComponent,
    data: { workspaceTab: 'company-config' as const }
  },
  {
    path: 'tracking-links',
    component: WorkspaceComponent,
    data: { workspaceTab: 'tracking-links' as const }
  }
];
