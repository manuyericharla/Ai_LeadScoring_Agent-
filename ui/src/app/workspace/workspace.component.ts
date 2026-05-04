import { Component, OnInit, inject } from '@angular/core';
import { DecimalPipe, DatePipe, NgFor, NgIf } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { AppBadgeComponent } from '../shared/components/badge/app-badge.component';
import { AppButtonComponent } from '../shared/components/button/app-button.component';
import { AppCardComponent } from '../shared/components/card/app-card.component';
import { AppComboboxComponent } from '../shared/components/combobox/app-combobox.component';
import { AppInputComponent } from '../shared/components/input/app-input.component';
import { AppTableComponent } from '../shared/components/table/app-table.component';
import { EventsBarChartComponent } from '../shared/components/dashboard-charts/events-bar-chart.component';
import { StagePieChartComponent } from '../shared/components/dashboard-charts/stage-pie-chart.component';
import { WorkspaceTopBarComponent } from './workspace-top-bar/workspace-top-bar.component';

@Component({
  selector: 'app-workspace',
  standalone: true,
  imports: [
    DecimalPipe,
    DatePipe,
    NgIf,
    NgFor,
    FormsModule,
    AppBadgeComponent,
    AppButtonComponent,
    AppCardComponent,
    AppComboboxComponent,
    AppInputComponent,
    AppTableComponent,
    EventsBarChartComponent,
    StagePieChartComponent,
    WorkspaceTopBarComponent
  ],
  templateUrl: './workspace.component.html',
  styleUrl: './workspace.component.scss'
})
export class WorkspaceComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly trackingBaseStorageKey = 'leadScoring.trackingBaseUrl';
  private copyFlashTimer?: ReturnType<typeof setTimeout>;
  apiBase = this.resolveApiBase();
  loading = false;
  error = '';
  data?: DashboardResponse;
  selectedFile?: File;
  source = '';
  importMessage = '';
  importError = '';
  importing = false;
  pageSize = 10;
  currentPage = 1;
  activeTab: LeftTab = 'dashboard';
  trackingBaseUrl = this.resolveTrackingBase();
  /** Labels for Universal URL `src`; stored value matches option text; URL uses lowercase. */
  readonly trackingSourceOptions = ['LinkedIn', 'Twitter', 'Facebook', 'Google', 'Other'];
  universalLinkSource = '';
  universalLinkCampaign = '';
  universalLinkRedirect = '';
  trackingLinkCopyStatus = '';
  linkCopiedFlash = false;
  companyNameFilter = '';
  companyConfigs: CompanyProductConfig[] = [];
  /** Unfiltered list for combobox suggestions (unaffected by table filter). */
  companyProductAll: CompanyProductConfig[] = [];
  configLoading = false;
  configError = '';
  configSuccess = '';
  savingConfig = false;
  editingConfigId?: string;
  companyProductForm: CompanyProductForm = {
    companyName: '',
    productName: '',
    items: [{ eventName: '', score: '' }]
  };

  /** Unique company names from saved configs (datalist suggestions). */
  get distinctCompanyNames(): string[] {
    const set = new Set<string>();
    for (const c of this.companyProductAll) {
      const n = c.companyName?.trim();
      if (n) {
        set.add(n);
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }

  /** Product names for the typed/selected company (trimmed names, case-insensitive). */
  get productsForSelectedCompany(): string[] {
    const target = this.companyProductForm.companyName.trim().toLowerCase();
    if (!target) {
      return [];
    }
    const set = new Set<string>();
    for (const c of this.companyProductAll) {
      if (c.companyName.trim().toLowerCase() === target) {
        const p = c.productName?.trim();
        if (p) {
          set.add(p);
        }
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }

  ngOnInit(): void {
    const savedBase = typeof localStorage !== 'undefined' ? localStorage.getItem(this.trackingBaseStorageKey)?.trim() : '';
    if (savedBase) {
      this.trackingBaseUrl = savedBase;
    }
    this.syncTabFromRoute();
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe(() => this.syncTabFromRoute());
    this.loadDashboard();
    this.loadCompanyConfigs();
    this.refreshCompanyProductIndex();
  }

  /** Maps API stage enums to badge token keys (CSS classes). */
  stageBadge(stage: StageName): 'cold' | 'warm' | 'mql' | 'hot' {
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

  private syncTabFromRoute(): void {
    let r: ActivatedRoute | null = this.route;
    while (r?.firstChild) {
      r = r.firstChild;
    }
    const tab = r?.snapshot.data['workspaceTab'] as LeftTab | undefined;
    if (!tab || tab === this.activeTab) {
      return;
    }
    this.activeTab = tab;
    if ((tab === 'dashboard' || tab === 'leads') && !this.data && !this.loading) {
      this.loadDashboard();
    }
    if (tab === 'company-config' && this.companyConfigs.length === 0 && !this.configLoading) {
      this.loadCompanyConfigs();
    }
    if (tab === 'company-config') {
      this.refreshCompanyProductIndex();
    }
  }

  get builtUniversalTrackingLink(): string {
    const base = this.trackingBaseUrl.trim().replace(/\/$/, '');
    if (!base) {
      return '';
    }

    const params = new URLSearchParams();
    const src = this.universalLinkSource.trim().toLowerCase();
    if (src) {
      params.set('src', src);
    }
    const cmp = this.universalLinkCampaign.trim();
    if (cmp) {
      params.set('cmp', cmp);
    }

    const redirect = this.universalLinkRedirect.trim();
    if (redirect) {
      params.set('redirect', redirect);
    }

    const q = params.toString();
    return q ? `${base}/r?${q}` : `${base}/r`;
  }

  /** Empty uses API default redirect; invalid blocks copy/test. */
  get universalRedirectValidation(): 'ok' | 'empty' | 'invalid' {
    const r = this.universalLinkRedirect.trim();
    if (!r) {
      return 'empty';
    }
    try {
      const u = new URL(r);
      if (u.protocol !== 'http:' && u.protocol !== 'https:') {
        return 'invalid';
      }
      return 'ok';
    } catch {
      return 'invalid';
    }
  }

  get canCopyOrTestUniversalLink(): boolean {
    const url = this.builtUniversalTrackingLink;
    return !!url && this.universalRedirectValidation !== 'invalid';
  }

  onTrackingBaseUrlChange(value: unknown): void {
    this.trackingBaseUrl = String(value ?? '');
    this.persistTrackingBaseUrl();
  }

  persistTrackingBaseUrl(): void {
    const v = this.trackingBaseUrl.trim();
    if (typeof localStorage !== 'undefined') {
      if (v) {
        localStorage.setItem(this.trackingBaseStorageKey, v);
      } else {
        localStorage.removeItem(this.trackingBaseStorageKey);
      }
    }
  }

  async copyUniversalTrackingLink(): Promise<void> {
    this.trackingLinkCopyStatus = '';
    this.linkCopiedFlash = false;
    clearTimeout(this.copyFlashTimer);
    const url = this.builtUniversalTrackingLink;
    if (!url) {
      this.trackingLinkCopyStatus = 'Set the tracking server URL first.';
      return;
    }
    if (this.universalRedirectValidation === 'invalid') {
      this.trackingLinkCopyStatus = 'Fix the redirect URL before copying.';
      return;
    }

    const flashCopied = (): void => {
      this.linkCopiedFlash = true;
      this.copyFlashTimer = setTimeout(() => (this.linkCopiedFlash = false), 2000);
    };

    try {
      await navigator.clipboard.writeText(url);
      flashCopied();
      return;
    } catch {
      /* fallback below */
    }

    const ta = document.getElementById('builtUniversalLink') as HTMLTextAreaElement | null;
    if (ta) {
      ta.focus();
      ta.select();
      ta.setSelectionRange(0, ta.value.length);
      try {
        const legacyOk = document.execCommand('copy');
        if (legacyOk) {
          flashCopied();
          return;
        }
        this.trackingLinkCopyStatus = 'Select the link above and press Ctrl+C.';
      } catch {
        this.trackingLinkCopyStatus = 'Select the link above and press Ctrl+C.';
      }
      return;
    }

    this.trackingLinkCopyStatus = 'Could not copy automatically.';
  }

  openUniversalTrackingLinkTest(): void {
    const url = this.builtUniversalTrackingLink;
    if (!url || this.universalRedirectValidation === 'invalid') {
      return;
    }
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  loadDashboard(): void {
    this.loading = true;
    this.error = '';
    this.http.get<DashboardResponse>(`${this.apiBase}/api/dashboard`).subscribe({
      next: (value) => {
        this.data = value;
        this.currentPage = 1;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = `Failed to load dashboard from ${this.apiBase}. Check API URL and CORS.`;
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0];
  }

  importLeads(): void {
    this.importMessage = '';
    this.importError = '';

    if (!this.selectedFile) {
      this.importError = 'Choose a CSV or XLSX file first.';
      return;
    }

    const src = this.source.trim();
    if (!src) {
      this.importError = 'Enter an import source label (how you refer to this file or vendor).';
      return;
    }

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('source', src);

    this.importing = true;
    this.http.post<LeadImportResult>(`${this.apiBase}/api/leads/import-file`, formData).subscribe({
      next: (result) => {
        this.importing = false;
        this.importMessage = `Processed ${result.processed}. Imported ${result.imported}, updated ${result.updated}, skipped ${result.skipped}.`;
        this.loadDashboard();
      },
      error: () => {
        this.importing = false;
        this.importError = 'Import failed. Verify API is running and file format is valid.';
      }
    });
  }

  get totalLeads(): number {
    return this.data?.leads.length ?? 0;
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalLeads / this.pageSize));
  }

  get paginatedLeads(): DashboardLead[] {
    if (!this.data) {
      return [];
    }

    const start = (this.currentPage - 1) * this.pageSize;
    return this.data.leads.slice(start, start + this.pageSize);
  }

  previousPage(): void {
    if (this.currentPage > 1) {
      this.currentPage--;
    }
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
    }
  }

  onPageSizeChange(): void {
    this.currentPage = 1;
  }

  downloadPdfReport(): void {
    if (!this.data || this.data.leads.length === 0) {
      this.error = 'No leads available to generate report.';
      return;
    }

    const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
    const generatedAt = new Date();
    doc.setFontSize(16);
    doc.text('Lead Scoring Report', 40, 40);
    doc.setFontSize(10);
    doc.text(`Generated: ${generatedAt.toLocaleString()}`, 40, 58);
    doc.text(`Total Leads: ${this.data.totalLeads}`, 40, 74);

    const body = this.data.leads.map((lead) => [
      lead.email,
      String(lead.score),
      lead.stage,
      new Date(lead.lastActivityUtc).toLocaleString(),
      lead.lastScoredAtUtc ? new Date(lead.lastScoredAtUtc).toLocaleString() : '-'
    ]);

    autoTable(doc, {
      startY: 90,
      head: [['Email', 'Score', 'Stage', 'Last Activity', 'Last Scored']],
      body,
      styles: {
        fontSize: 9,
        cellPadding: 6
      },
      headStyles: {
        fillColor: [173, 216, 230],
        textColor: [17, 24, 39],
        fontStyle: 'bold'
      },
      didParseCell: (hookData) => {
        if (hookData.section === 'body' && hookData.column.index === 2) {
          const stage = String(hookData.cell.raw);
          if (stage === 'Cold') {
            hookData.cell.styles.textColor = [30, 64, 175];
          } else if (stage === 'Warm') {
            hookData.cell.styles.textColor = [180, 83, 9];
          } else if (stage === 'Mql') {
            hookData.cell.styles.textColor = [3, 105, 161];
          } else if (stage === 'Hot') {
            hookData.cell.styles.textColor = [185, 28, 28];
          }
        }
      }
    });

    const filename = `lead-scoring-report-${generatedAt.toISOString().slice(0, 10)}.pdf`;
    doc.save(filename);
  }

  addEventItem(): void {
    this.companyProductForm.items.push({ eventName: '', score: '' });
  }

  removeEventItem(index: number): void {
    if (this.companyProductForm.items.length === 1) {
      return;
    }

    this.companyProductForm.items.splice(index, 1);
  }

  saveCompanyConfig(): void {
    this.configError = '';
    this.configSuccess = '';

    const companyName = this.companyProductForm.companyName.trim();
    const productName = this.companyProductForm.productName.trim();
    if (!companyName || !productName) {
      this.configError = 'Company name and product name are required.';
      return;
    }

    const eventPairs: { eventName: string; score: number }[] = [];
    for (const item of this.companyProductForm.items) {
      const eventName = item.eventName.trim();
      if (!eventName) {
        continue;
      }
      const num = item.score === '' ? NaN : Number(item.score);
      if (!Number.isFinite(num) || num < 0) {
        this.configError = 'Enter a score (0 or greater) for each event that has a name.';
        return;
      }
      eventPairs.push({ eventName, score: num });
    }

    if (eventPairs.length === 0) {
      this.configError = 'Add at least one event with a name and score.';
      return;
    }

    const productEventConfig: Record<string, number> = {};
    for (const item of eventPairs) {
      productEventConfig[item.eventName] = Math.max(0, item.score);
    }

    const payload: UpsertCompanyProductConfigRequest = {
      companyName,
      productName,
      productEventConfig
    };

    this.savingConfig = true;
    const request$ = this.editingConfigId
      ? this.http.put<CompanyProductConfig>(`${this.apiBase}/api/company-product-configs/${this.editingConfigId}`, payload)
      : this.http.post<CompanyProductConfig>(`${this.apiBase}/api/company-product-configs`, payload);

    request$.subscribe({
      next: () => {
        this.savingConfig = false;
        this.configSuccess = this.editingConfigId ? 'Company product config updated.' : 'Company product config saved.';
        this.resetCompanyConfigForm(companyName);
        this.companyNameFilter = companyName;
        this.loadCompanyConfigs();
        this.refreshCompanyProductIndex();
      },
      error: () => {
        this.savingConfig = false;
        this.configError = this.editingConfigId
          ? 'Failed to update config. Check API and values.'
          : 'Failed to save config. Check API and values.';
      }
    });
  }

  editCompanyConfig(config: CompanyProductConfig): void {
    this.editingConfigId = config.id;
    this.companyProductForm = {
      companyName: config.companyName,
      productName: config.productName,
      items: this.eventConfigEntries(config.productEventConfig).map((x) => ({
        eventName: x.key,
        score: x.value
      }))
    };
    this.configError = '';
    this.configSuccess = 'Editing selected config.';
  }

  cancelCompanyConfigEdit(): void {
    this.resetCompanyConfigForm();
    this.configError = '';
    this.configSuccess = 'Edit canceled.';
  }

  loadCompanyConfigs(): void {
    this.configLoading = true;
    this.configError = '';
    const filter = this.companyNameFilter.trim();
    const query = filter ? `?companyName=${encodeURIComponent(filter)}` : '';
    this.http.get<CompanyProductConfig[]>(`${this.apiBase}/api/company-product-configs${query}`).subscribe({
      next: (records) => {
        this.configLoading = false;
        this.companyConfigs = records;
      },
      error: () => {
        this.configLoading = false;
        this.configError = 'Failed to load company product configs.';
      }
    });
  }

  eventConfigEntries(config: Record<string, number>): Array<{ key: string; value: number }> {
    return Object.entries(config).map(([key, value]) => ({ key, value }));
  }

  private resetCompanyConfigForm(defaultCompanyName = ''): void {
    this.editingConfigId = undefined;
    this.companyProductForm = {
      companyName: defaultCompanyName,
      productName: '',
      items: [{ eventName: '', score: '' }]
    };
  }

  /** Full config list for combobox suggestions (unaffected by table filter). */
  private refreshCompanyProductIndex(): void {
    this.http.get<CompanyProductConfig[]>(`${this.apiBase}/api/company-product-configs`).subscribe({
      next: (rows) => {
        this.companyProductAll = rows;
      },
      error: () => {
        /* keep previous suggestions on failure */
      }
    });
  }

  private resolveTrackingBase(): string {
    if (typeof window === 'undefined') {
      return 'http://localhost:8211';
    }

    const host = window.location.hostname;
    if (host === 'localhost' || host === '127.0.0.1') {
      return 'http://localhost:8211';
    }

    if (this.isPrivateIpv4Host(host)) {
      return `http://${host}:8211`;
    }

    return window.location.origin;
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

    // In deployed environments, use same-origin and let nginx proxy /api.
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

type StageName = 'Cold' | 'Warm' | 'Mql' | 'Hot';
type EventName = 'Open' | 'EmailClick' | 'WebsiteActivity';

interface DashboardLead {
  id: string;
  email: string;
  score: number;
  stage: StageName;
  lastActivityUtc: string;
  lastScoredAtUtc?: string | null;
  lastEvent?: string | null;
  lastEmailName?: string | null;
  nextEmailName?: string | null;
  nextDateTimeUtc?: string | null;
  nextStage: StageName;
  signupCompleted: boolean;
  profileCompletion: boolean;
  selectedPlan?: string | null;
  planRenewalDate?: string | null;
}

interface DashboardResponse {
  totalLeads: number;
  stageCounts: Record<StageName, number>;
  eventsByType: Record<EventName, number>;
  leads: DashboardLead[];
}

interface LeadImportResult {
  processed: number;
  imported: number;
  updated: number;
  skipped: number;
  errors: string[];
}

interface CompanyProductForm {
  companyName: string;
  productName: string;
  items: CompanyProductEventItem[];
}

interface CompanyProductEventItem {
  eventName: string;
  score: number | '';
}

interface UpsertCompanyProductConfigRequest {
  companyName: string;
  productName: string;
  productEventConfig: Record<string, number>;
}

interface CompanyProductConfig {
  id: string;
  companyName: string;
  productName: string;
  productId: number;
  productEventConfig: Record<string, number>;
  createdAtUtc: string;
}

type LeftTab = 'dashboard' | 'leads' | 'company-config' | 'tracking-links';
