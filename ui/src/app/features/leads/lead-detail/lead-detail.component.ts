import { DatePipe, DecimalPipe, NgIf } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AppBadgeComponent } from '../../../shared/components/badge/app-badge.component';
import { AppButtonComponent } from '../../../shared/components/button/app-button.component';
import { AppCardComponent } from '../../../shared/components/card/app-card.component';
import { AppComboboxComponent } from '../../../shared/components/combobox/app-combobox.component';
import { AppTableComponent } from '../../../shared/components/table/app-table.component';
import { WorkspaceTopBarComponent } from '../../../workspace/workspace-top-bar/workspace-top-bar.component';

@Component({
  selector: 'app-lead-detail',
  standalone: true,
  imports: [
    DatePipe,
    DecimalPipe,
    FormsModule,
    NgIf,
    AppBadgeComponent,
    AppButtonComponent,
    AppCardComponent,
    AppComboboxComponent,
    AppTableComponent,
    WorkspaceTopBarComponent
  ],
  templateUrl: './lead-detail.component.html',
  styleUrl: './lead-detail.component.scss'
})
export class LeadDetailComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  apiBase = this.resolveApiBase();
  loading = false;
  error = '';
  payload?: LeadEventsResponse;

  /** `desc` = newest first, `asc` = oldest first (matches Leads list filter control). */
  timeSortOrder: 'desc' | 'asc' = 'desc';

  /** Empty string = all sources (same semantics as Leads list). */
  eventsSourceFilter = '';

  /** Empty string = all campaigns; use `eventsCampaignNoneSentinel` for events with no campaign. */
  eventsCampaignFilter = '';

  readonly knownEventSourceLabels = ['Unknown', 'Email', 'Website', 'LinkedIn', 'Direct', 'Organic'] as const;

  readonly eventsCampaignNoneSentinel = '__no_campaign__';

  readonly timeSortOptions: ('desc' | 'asc')[] = ['desc', 'asc'];

  readonly timeSortLabelFn = (v: string): string => {
    switch (v) {
      case 'desc':
        return 'Newest first';
      case 'asc':
        return 'Oldest first';
      default:
        return v;
    }
  };

  get eventsSourceComboboxOptions(): string[] {
    return ['', ...this.eventsSourceSelectOptions];
  }

  get eventsCampaignComboboxOptions(): string[] {
    return ['', this.eventsCampaignNoneSentinel, ...this.eventsCampaignFilterSelectOptions];
  }

  readonly eventsSourceComboboxLabelFn = (v: string): string => {
    if (v === '') {
      return 'All sources';
    }
    return this.sourceOptionLabel(v);
  };

  readonly eventsCampaignComboboxLabelFn = (v: string): string => {
    const counts = this.campaignCountsBySourceFilteredEvents();
    let n: number;
    if (v === '') {
      n = this.eventsMatchingSourceOnly.length;
    } else {
      n = counts.get(v) ?? 0;
    }
    if (v === '') {
      return `All campaigns (${n})`;
    }
    if (v === this.eventsCampaignNoneSentinel) {
      return `None (${n})`;
    }
    return `${v} (${n})`;
  };

  onEventsSourceFilterChange(): void {
    this.eventsCampaignFilter = '';
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('leadId');
    if (!id) {
      this.error = 'Missing lead id.';
      return;
    }
    this.load(id);
  }

  stageBadge(stage: string): 'cold' | 'warm' | 'mql' | 'hot' {
    switch (stage) {
      case 'Cold':
        return 'cold';
      case 'Warm':
        return 'warm';
      case 'Mql':
        return 'mql';
      case 'Hot':
        return 'hot';
      default:
        return 'cold';
    }
  }

  goBack(): void {
    void this.router.navigate(['/leads']);
  }

  /** Source labels for the filter (known list plus any extra values present in this lead’s events). */
  get eventsSourceSelectOptions(): string[] {
    const known: string[] = [...this.knownEventSourceLabels];
    const extra = new Set<string>();
    for (const ev of this.payload?.events ?? []) {
      const v = (ev.source ?? '').trim();
      const label = v || 'Unknown';
      if (!known.includes(label)) {
        extra.add(label);
      }
    }
    return [...known, ...Array.from(extra).sort((a, b) => a.localeCompare(b))];
  }

  /** Distinct non-empty campaign values from events that match the current source filter. */
  get eventsCampaignFilterSelectOptions(): string[] {
    const set = new Set<string>();
    for (const ev of this.eventsMatchingSourceOnly) {
      const c = (ev.campaign ?? '').trim();
      if (c) {
        set.add(c);
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }

  /** Events matching the source filter only (used for campaign dropdown and counts). */
  private get eventsMatchingSourceOnly(): LeadEventRow[] {
    if (!this.payload?.events) {
      return [];
    }
    const sf = this.eventsSourceFilter.trim();
    if (!sf) {
      return this.payload.events;
    }
    return this.payload.events.filter((ev) => {
      const v = (ev.source ?? '').trim();
      if (sf === 'Unknown') {
        return !v || v === 'Unknown';
      }
      return v === sf;
    });
  }

  private campaignCountsBySourceFilteredEvents(): Map<string, number> {
    const map = new Map<string, number>();
    for (const ev of this.eventsMatchingSourceOnly) {
      const c = (ev.campaign ?? '').trim();
      const key = c ? c : this.eventsCampaignNoneSentinel;
      map.set(key, (map.get(key) ?? 0) + 1);
    }
    return map;
  }

  /** Events after source + campaign filters, sorted by time. */
  get displayedEvents(): LeadEventRow[] {
    let list = this.eventsMatchingSourceOnly;
    const cf = this.eventsCampaignFilter.trim();
    if (cf) {
      list = list.filter((ev) => {
        const c = (ev.campaign ?? '').trim();
        if (cf === this.eventsCampaignNoneSentinel) {
          return !c;
        }
        return c === cf;
      });
    }
    const dir = this.timeSortOrder === 'desc' ? -1 : 1;
    return [...list].sort((a, b) => dir * (Date.parse(a.timestampUtc) - Date.parse(b.timestampUtc)));
  }

  private sourceOptionLabel(value: string): string {
    return value === 'Unknown' ? 'Others' : value;
  }

  private describeLoadError(err: unknown): string {
    if (!(err instanceof HttpErrorResponse)) {
      return 'Could not load this lead or their events.';
    }

    if (err.status === 0) {
      return 'Cannot reach the API. Start or restart LeadScoring.Api (for example on http://localhost:5221) and try again.';
    }

    if (err.status === 404) {
      const body = err.error as { message?: string } | undefined;
      if (body?.message === 'Lead not found.') {
        return 'This lead is not in the database (it may have been removed).';
      }
      return 'The server returned 404 for this request. If you recently updated the code, stop and rebuild LeadScoring.Api so GET /api/leads/{id}/events is available, then try again.';
    }

    return 'Could not load this lead or their events.';
  }

  private load(leadId: string): void {
    this.loading = true;
    this.error = '';
    this.http.get<LeadEventsResponse>(`${this.apiBase}/api/leads/${leadId}/events`).subscribe({
      next: (res) => {
        this.payload = res;
        this.eventsSourceFilter = '';
        this.eventsCampaignFilter = '';
        this.loading = false;
      },
      error: (err: unknown) => {
        this.error = this.describeLoadError(err);
        this.loading = false;
      }
    });
  }

  private resolveApiBase(): string {
    if (typeof window === 'undefined') {
      return 'http://localhost:5221';
    }

    const host = window.location.hostname;
    if (host === 'localhost' || host === '127.0.0.1') {
      return 'http://localhost:5221';
    }

    if (this.isPrivateIpv4Host(host)) {
      return `http://${host}:5221`;
    }

    return '';
  }

  private isPrivateIpv4Host(host: string): boolean {
    const parts = host.split('.').map((x) => Number(x));
    if (parts.length !== 4 || parts.some((x) => Number.isNaN(x) || x < 0 || x > 255)) {
      return false;
    }

    const [a, b] = parts;
    return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168);
  }
}

interface LeadEventsResponse {
  leadId: string;
  email: string;
  totalScore: number;
  stage: string;
  events: LeadEventRow[];
}

interface LeadEventRow {
  id: string;
  timestampUtc: string;
  eventScore: number;
  eventType: string;
  source: string;
  campaign?: string | null;
  what: string;
}
