import type { Route } from '@angular/router';
import { WorkspaceComponent } from '../../workspace/workspace.component';

export const leadsRoute: Route = {
  path: 'leads',
  component: WorkspaceComponent,
  data: { workspaceTab: 'leads' as const }
};
