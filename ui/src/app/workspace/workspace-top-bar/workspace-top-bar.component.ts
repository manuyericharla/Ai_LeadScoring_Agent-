import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-workspace-top-bar',
  standalone: true,
  templateUrl: './workspace-top-bar.component.html',
  styleUrl: './workspace-top-bar.component.scss'
})
export class WorkspaceTopBarComponent {
  @Input({ required: true }) title!: string;
  @Input() subtitle = '';
}
