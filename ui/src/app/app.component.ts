import { Component, OnInit, inject } from '@angular/core';
import { DecimalPipe, DatePipe, NgClass, NgFor, NgIf } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

@Component({
  selector: 'app-root',
  imports: [DecimalPipe, DatePipe, NgIf, NgFor, NgClass, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);
  apiBase = 'http://localhost:5221';
  loading = false;
  error = '';
  data?: DashboardResponse;
  selectedFile?: File;
  source = 'hubspot';
  importMessage = '';
  importError = '';
  importing = false;
  pageSize = 10;
  currentPage = 1;
  activeTab: LeftTab = 'dashboard';
  companyNameFilter = '';
  companyConfigs: CompanyProductConfig[] = [];
  configLoading = false;
  configError = '';
  configSuccess = '';
  savingConfig = false;
  editingConfigId?: string;
  companyProductForm: CompanyProductForm = {
    companyName: '',
    productName: '',
    productId: 1,
    items: [{ eventName: '', score: 0 }]
  };

  ngOnInit(): void {
    this.loadDashboard();
    this.loadCompanyConfigs();
  }

  setActiveTab(tab: LeftTab): void {
    this.activeTab = tab;
    if (tab === 'dashboard' && !this.data && !this.loading) {
      this.loadDashboard();
    }
    if (tab === 'company-config' && this.companyConfigs.length === 0 && !this.configLoading) {
      this.loadCompanyConfigs();
    }
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

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('source', this.source.trim() || 'external');

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

  getStageClass(stage: StageName): string {
    switch (stage) {
      case 'Cold':
        return 'stage-cold';
      case 'Warm':
        return 'stage-warm';
      case 'Mql':
        return 'stage-mql';
      case 'Hot':
        return 'stage-hot';
      default:
        return '';
    }
  }

  get apiConnected(): boolean {
    return !this.loading && !this.error;
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
    this.companyProductForm.items.push({ eventName: '', score: 0 });
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
    if (!companyName || !productName || this.companyProductForm.productId <= 0) {
      this.configError = 'Company name, product name, and Product ID are required.';
      return;
    }

    const eventPairs = this.companyProductForm.items
      .map((x) => ({ eventName: x.eventName.trim(), score: Number(x.score) }))
      .filter((x) => x.eventName.length > 0);

    if (eventPairs.length === 0) {
      this.configError = 'Add at least one event item with score.';
      return;
    }

    const productEventConfig: Record<string, number> = {};
    for (const item of eventPairs) {
      productEventConfig[item.eventName] = Math.max(0, item.score);
    }

    const payload: UpsertCompanyProductConfigRequest = {
      companyName,
      productName,
      productId: Number(this.companyProductForm.productId),
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
      productId: config.productId,
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
      productId: 1,
      items: [{ eventName: '', score: 0 }]
    };
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
  productId: number;
  items: CompanyProductEventItem[];
}

interface CompanyProductEventItem {
  eventName: string;
  score: number;
}

interface UpsertCompanyProductConfigRequest {
  companyName: string;
  productName: string;
  productId: number;
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

type LeftTab = 'dashboard' | 'company-config';
