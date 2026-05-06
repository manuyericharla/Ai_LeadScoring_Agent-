import { DatePipe, DecimalPipe, NgIf } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AppBadgeComponent } from '../../../shared/components/badge/app-badge.component';
import { AppButtonComponent } from '../../../shared/components/button/app-button.component';
import { AppCardComponent } from '../../../shared/components/card/app-card.component';
import { AppTableComponent } from '../../../shared/components/table/app-table.component';
import { WorkspaceTopBarComponent } from '../../../workspace/workspace-top-bar/workspace-top-bar.component';

@Component({
  selector: 'app-lead-detail',
  standalone: true,
  imports: [
    DatePipe,
    DecimalPipe,
    NgIf,
    AppBadgeComponent,
    AppButtonComponent,
    AppCardComponent,
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
