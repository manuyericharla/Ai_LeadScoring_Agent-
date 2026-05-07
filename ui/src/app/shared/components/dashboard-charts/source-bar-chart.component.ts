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

export type SourceCountsInput = Record<string, number>;

let sourceChartRegistered = false;

function registerSourceChart(): void {
  if (!sourceChartRegistered) {
    Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend);
    sourceChartRegistered = true;
  }
}

@Component({
  selector: 'app-source-bar-chart',
  standalone: true,
  templateUrl: './source-bar-chart.component.html',
  styleUrl: './source-bar-chart.component.scss'
})
export class SourceBarChartComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvas') canvasRef?: ElementRef<HTMLCanvasElement>;

  @Input({ required: true }) sourceCounts!: SourceCountsInput;

  private chart?: Chart;
  private renderHandle?: ReturnType<typeof setTimeout>;

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
    if (!this.sourceCounts || !el) {
      return;
    }

    registerSourceChart();
    const ctx = el.getContext('2d');
    if (!ctx) {
      return;
    }

    const entries = Object.entries(this.sourceCounts ?? {})
      .map(([label, value]) => ({
        label,
        value: Number(value) || 0
      }))
      .sort((a, b) => b.value - a.value || a.label.localeCompare(b.label));

    const labels = entries.map((x) => x.label);
    const values = entries.map((x) => x.value);
    const colors = [
      'rgba(59,130,246,0.85)',
      'rgba(14,165,233,0.85)',
      'rgba(239,68,68,0.85)',
      'rgba(168,85,247,0.85)',
      'rgba(34,197,94,0.85)',
      'rgba(100,116,139,0.85)',
      'rgba(245,158,11,0.85)',
      'rgba(236,72,153,0.85)'
    ];

    this.chart?.destroy();
    this.chart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          {
            label: 'Leads',
            data: values,
            backgroundColor: values.map((_, idx) => colors[idx % colors.length]),
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
