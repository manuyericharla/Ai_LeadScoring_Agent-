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
  Tooltip
} from 'chart.js';

export type StageCountsInput = Partial<Record<'Cold' | 'Warm' | 'Mql' | 'Hot', number>>;

let stageChartRegistered = false;

function registerStageBarChart(): void {
  if (!stageChartRegistered) {
    Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend);
    stageChartRegistered = true;
  }
}

const STAGE_ORDER: Array<{ key: keyof StageCountsInput; label: string; color: string }> = [
  { key: 'Cold', label: 'Cold', color: '#3b82f6' },
  { key: 'Warm', label: 'Warm', color: '#f59e0b' },
  { key: 'Mql', label: 'MQL', color: '#7c3aed' },
  { key: 'Hot', label: 'Hot', color: '#e11d48' }
];

@Component({
  selector: 'app-stage-pie-chart',
  standalone: true,
  templateUrl: './stage-pie-chart.component.html',
  styleUrl: './stage-pie-chart.component.scss'
})
export class StagePieChartComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvas') canvasRef?: ElementRef<HTMLCanvasElement>;

  @Input({ required: true }) stageCounts!: StageCountsInput;

  private chart?: Chart;
  private renderHandle?: ReturnType<typeof setTimeout>;

  get hasData(): boolean {
    const s = this.stageCounts;
    if (!s) {
      return false;
    }
    return (Number(s.Cold) || 0) + (Number(s.Warm) || 0) + (Number(s.Mql) || 0) + (Number(s.Hot) || 0) > 0;
  }

  /** When one stage dominates, a pie hides smaller segments; the bar still helps—this adds plain-language context. */
  get stageInsight(): string | null {
    if (!this.hasData) {
      return null;
    }
    const entries = STAGE_ORDER.map((o) => ({
      label: o.label,
      value: Number(this.stageCounts[o.key as 'Cold' | 'Warm' | 'Mql' | 'Hot']) || 0
    }));
    const total = entries.reduce((a, e) => a + e.value, 0);
    if (!total) {
      return null;
    }
    const sorted = [...entries].sort((a, b) => b.value - a.value);
    const top = sorted[0];
    const pct = Math.round((top.value / total) * 100);
    if (pct >= 75) {
      return `${top.label} is ${pct}% of leads—smaller stages stay visible as bars on the right.`;
    }
    return null;
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
    if (!this.hasData) {
      this.chart?.destroy();
      this.chart = undefined;
      return;
    }

    const el = this.canvasRef?.nativeElement;
    if (!el) {
      return;
    }

    registerStageBarChart();
    const ctx = el.getContext('2d');
    if (!ctx) {
      return;
    }

    const rows = STAGE_ORDER.map((o) => ({
      label: o.label,
      value: Number(this.stageCounts[o.key as 'Cold' | 'Warm' | 'Mql' | 'Hot']) || 0,
      color: o.color
    }));

    const maxVal = Math.max(...rows.map((r) => r.value), 1);

    this.chart?.destroy();

    this.chart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: rows.map((r) => r.label),
        datasets: [
          {
            data: rows.map((r) => r.value),
            backgroundColor: rows.map((r) => r.color),
            borderRadius: 6,
            borderSkipped: false,
            barThickness: 24,
            maxBarThickness: 32
          }
        ]
      },
      options: {
        indexAxis: 'y',
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (item) => {
                const vs = item.dataset.data as number[];
                const n = typeof item.raw === 'number' ? item.raw : Number(item.raw);
                const total = vs.reduce((a, b) => a + b, 0);
                const pct = total ? Math.round((n / total) * 100) : 0;
                return `${n.toLocaleString()} (${pct}%)`;
              }
            }
          }
        },
        scales: {
          x: {
            beginAtZero: true,
            suggestedMax: maxVal * 1.08,
            grid: {
              color: 'rgba(15, 23, 42, 0.06)'
            },
            ticks: {
              precision: 0,
              font: { size: 11, family: 'DM Sans, sans-serif' }
            }
          },
          y: {
            grid: { display: false },
            ticks: {
              font: { size: 12, family: 'DM Sans, sans-serif' }
            }
          }
        }
      }
    });
  }
}
