import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';

@Component({
  selector: 'app-lead-progress-circle',
  standalone: true,
  templateUrl: './lead-progress-circle.component.html',
  styleUrl: './lead-progress-circle.component.scss'
})
export class LeadProgressCircleComponent implements OnChanges {
  @Input() successCount = 0;
  @Input() totalLeads = 0;

  readonly size = 140;
  readonly strokeWidth = 10;
  readonly radius = (this.size - this.strokeWidth) / 2;
  readonly circumference = 2 * Math.PI * this.radius;

  animatedPercentage = 0;

  get normalizedSuccessCount(): number {
    return Math.max(0, this.successCount || 0);
  }

  get normalizedTotalLeads(): number {
    return Math.max(0, this.totalLeads || 0);
  }

  get percentage(): number {
    if (this.normalizedTotalLeads <= 0) {
      return 0;
    }
    return Math.min(100, (this.normalizedSuccessCount / this.normalizedTotalLeads) * 100);
  }

  get displayValue(): string {
    if (this.normalizedTotalLeads > 0) {
      return `${Math.round(this.animatedPercentage)}%`;
    }
    return `${this.normalizedSuccessCount}`;
  }

  get tooltipText(): string {
    return `${this.normalizedSuccessCount} out of ${this.normalizedTotalLeads} leads converted`;
  }

  get progressColor(): string {
    if (this.percentage < 30) {
      return '#ef4444';
    }
    if (this.percentage <= 70) {
      return '#f59e0b';
    }
    return '#4CAF50';
  }

  get dashOffset(): number {
    return this.circumference * (1 - this.animatedPercentage / 100);
  }

  ngOnChanges(_changes: SimpleChanges): void {
    this.animateToCurrentPercentage();
  }

  private animateToCurrentPercentage(): void {
    this.animatedPercentage = 0;
    requestAnimationFrame(() => {
      this.animatedPercentage = this.percentage;
    });
  }
}
