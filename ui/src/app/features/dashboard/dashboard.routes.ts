import type { Route } from '@angular/router';
import { WorkspaceComponent } from '../../workspace/workspace.component';

export const dashboardRoute: Route = {
  path: 'dashboard',
  component: WorkspaceComponent,
  data: { workspaceTab: 'dashboard' as const }
};
