import { Component, ViewEncapsulation } from '@angular/core';

@Component({
  selector: 'app-table',
  standalone: true,
  template: `
    <div class="app-table-shell">
      <ng-content />
    </div>
  `,
  styles: [
    `
      .app-table-shell {
        overflow-x: auto;
        background: var(--color-bg-card);
        border-radius: var(--radius-lg);
        border: 1px solid var(--color-border);
        box-shadow: var(--shadow-card);
      }

      .app-table-shell table {
        width: 100%;
        border-collapse: collapse;
      }

      .app-table-shell thead {
        background: var(--table-head-bg);
      }

      .app-table-shell thead th {
        position: sticky;
        top: 0;
        z-index: 1;
        text-align: left;
        padding: var(--table-cell-padding-y) var(--table-cell-padding-x);
        font-family: var(--font-body);
        font-size: var(--text-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: var(--tracking-label);
        line-height: var(--leading-snug);
        color: var(--color-text-muted);
        border-bottom: 1px solid var(--color-border-light);
      }

      .app-table-shell tbody td {
        padding: var(--table-cell-padding-y) var(--table-cell-padding-x);
        font-family: var(--font-body);
        font-size: var(--text-base);
        font-weight: var(--font-weight-regular);
        line-height: var(--leading-snug);
        border-bottom: 1px solid var(--color-border-light);
        vertical-align: middle;
      }

      .app-table-shell tbody tr {
        transition: background 0.1s ease;
      }

      .app-table-shell tbody tr:hover {
        background: var(--color-bg-hover);
      }

      .app-table-shell tbody tr:last-child td {
        border-bottom: none;
      }
    `
  ],
  encapsulation: ViewEncapsulation.None
})
export class AppTableComponent {}
