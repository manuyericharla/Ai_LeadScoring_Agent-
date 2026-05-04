import {
  AfterViewInit,
  Component,
  ElementRef,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  ViewChild
} from '@angular/core';
import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  Legend,
  LinearScale,
  Tooltip,
  TooltipItem
} from 'chart.js';

export type EventsByTypeInput = Partial<Record<'Open' | 'EmailClick' | 'WebsiteActivity', number>>;

let barRegistered = false;

function registerBarChart(): void {
  if (!barRegistered) {
    Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend);
    barRegistered = true;
  }
}

@Component({
  selector: 'app-events-bar-chart',
  standalone: true,
  templateUrl: './events-bar-chart.component.html',
  styleUrl: './events-bar-chart.component.scss'
})
export class EventsBarChartComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvas') canvasRef?: ElementRef<HTMLCanvasElement>;

  @Input({ required: true }) eventsByType!: EventsByTypeInput;

  private chart?: Chart;
  private renderHandle?: ReturnType<typeof setTimeout>;

  /** Names of channels with zero events, for empty-state copy. */
  get dormantChannelsHint(): string | null {
    if (!this.eventsByType) {
      return null;
    }
    const nOpen = Number(this.eventsByType.Open) || 0;
    const nClick = Number(this.eventsByType.EmailClick) || 0;
    const nWeb = Number(this.eventsByType.WebsiteActivity) || 0;
    if (nOpen + nClick + nWeb === 0) {
      return 'No engagement events recorded for these channels yet.';
    }
    const dormant: string[] = [];
    if (!nOpen) {
      dormant.push('Opens');
    }
    if (!nClick) {
      dormant.push('Email clicks');
    }
    if (!nWeb) {
      dormant.push('Website activity');
    }
    if (dormant.length === 0) {
      return null;
    }
    if (dormant.length === 1) {
      return `No ${dormant[0].toLowerCase()} recorded yet—other channels drive this view.`;
    }
    return `No ${dormant.map((d) => d.toLowerCase()).join(' or ')} yet—concentration below is expected.`;
  }

  ngAfterViewInit(): void {
    this.scheduleRender();
  }

  ngOnChanges(_changes: SimpleChanges): void {
    this.scheduleRender();
  }

  ngOnDestroy(): void {
    if (this.renderHandle !== undefined) {
      clearTimeout(this.renderHandle);
    }
    this.chart?.destroy();
    this.chart = undefined;
  }

  private scheduleRender(): void {
    if (this.renderHandle !== undefined) {
      clearTimeout(this.renderHandle);
    }
    this.renderHandle = setTimeout(() => {
      this.renderHandle = undefined;
      this.render();
    }, 0);
  }

  private render(): void {
    const el = this.canvasRef?.nativeElement;
    if (!this.eventsByType || !el) {
      return;
    }

    registerBarChart();
    const ctx = el.getContext('2d');
    if (!ctx) {
      return;
    }

    const labels = ['Opens', 'Email clicks', 'Website activity'];
    const values = [
      Number(this.eventsByType.Open) || 0,
      Number(this.eventsByType.EmailClick) || 0,
      Number(this.eventsByType.WebsiteActivity) || 0
    ];
    const colors = ['rgba(59, 130, 246, 0.9)', 'rgba(124, 58, 237, 0.9)', 'rgba(34, 189, 82, 0.9)'];

    this.chart?.destroy();

    const maxVal = Math.max(1, ...values);
    const maxIndex = values.indexOf(Math.max(...values));
    /** Slight emphasis on the largest bar so a single dominant channel does not look like an error. */
    const backgroundColor = values.map((_, i) =>
      i === maxIndex && values.some((v) => v > 0) ? colors[i] : colors[i].replace('0.9)', '0.72)')
    );

    this.chart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          {
            label: 'Events',
            data: values,
            backgroundColor,
            borderRadius: 6,
            borderSkipped: false
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (item: TooltipItem<'bar'>) =>
                `${item.label}: ${typeof item.raw === 'number' ? item.raw.toLocaleString() : String(item.raw)}`
            }
          }
        },
        scales: {
          x: {
            grid: { display: false },
            ticks: { font: { size: 11, family: 'DM Sans, sans-serif' } }
          },
          y: {
            beginAtZero: true,
            suggestedMax: maxVal * 1.12,
            ticks: {
              precision: 0,
              font: { size: 11, family: 'DM Sans, sans-serif' }
            },
            grid: {
              color: 'rgba(15, 23, 42, 0.06)'
            }
          }
        }
      }
    });
  }
}
