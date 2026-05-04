import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-badge',
  standalone: true,
  template: '<span [class]="klass"><ng-content /></span>',
  styleUrl: './app-badge.component.scss'
})
export class AppBadgeComponent {
  @Input() stage: 'cold' | 'warm' | 'mql' | 'hot' | 'neutral' = 'neutral';
  @Input() score?: number;

  get klass(): string {
    if (this.score !== undefined && this.score !== null) {
      return this.score > 0 ? 'badge score-pos' : 'badge neutral';
    }
    return `badge ${this.stage}`;
  }
}
