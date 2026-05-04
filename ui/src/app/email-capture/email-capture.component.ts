import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  AfterViewInit,
  inject
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { NgIf } from '@angular/common';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-email-capture',
  standalone: true,
  imports: [FormsModule, NgIf],
  templateUrl: './email-capture.component.html',
  styleUrl: './email-capture.component.scss'
})
export class EmailCaptureComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly http = inject(HttpClient);

  @ViewChild('emailGate', { read: ElementRef }) private emailGateEl?: ElementRef<HTMLInputElement>;

  private static readonly prefillGlobalKey = 'leadScoring.prefillEmail';
  private static readonly lastVisitorKey = 'leadScoring.lastVisitorId';

  visitorId = '';
  sourceToken = '';
  redirectTarget = '';
  campaign = '';

  email = '';
  submitting = false;
  errorMsg = '';
  paramError = '';

  private pageEnteredMs = Date.now();
  private alreadyCaptured = false;
  private hintEmail: string | null = null;

  /**
   * Chrome often defers autofill until focus; a one-time readonly shim helps autofill apply on first tap.
   * See https://stackoverflow.com/questions/35049599/chrome-autofill-autocomplete-on-input-field
   */
  autofillReadonlyShim = true;

  private autofillTimerIds: ReturnType<typeof setTimeout>[] = [];
  private autofillListenersAttached = false;
  private readonly onWindowMaybeAutofill = (): void => {
    this.syncEmailFromNativeInput();
  };

  ngOnInit(): void {
    this.pageEnteredMs = Date.now();
    const params = typeof window !== 'undefined' ? this.readQueryThenStripLegacyVisitor() : new URLSearchParams();
    void this.bootstrapFromParams(params.get('src'), params.get('redirect'), params.get('cmp'));
  }

  ngAfterViewInit(): void {
    this.scheduleAutofillReconciliation();
  }

  ngOnDestroy(): void {
    for (const id of this.autofillTimerIds) {
      clearTimeout(id);
    }
    this.autofillTimerIds = [];
    if (typeof window !== 'undefined' && this.autofillListenersAttached) {
      window.removeEventListener('focus', this.onWindowMaybeAutofill, true);
      window.removeEventListener('pointerdown', this.onWindowMaybeAutofill, true);
      this.autofillListenersAttached = false;
    }
  }

  clearAutofillReadonlyShim(): void {
    this.autofillReadonlyShim = false;
  }

  /** Password managers and Chrome autofill often bypass Angular's ngModel; copy native value into the model. */
  syncEmailFromNativeInput(): void {
    const el = this.emailGateEl?.nativeElement;
    if (!el) {
      return;
    }
    const native = el.value?.trim() ?? '';
    if (!native.includes('@')) {
      return;
    }
    if (native !== this.email.trim()) {
      this.email = native;
    }
  }

  onAutofillAnimationStart(ev: AnimationEvent): void {
    if (ev.animationName === 'onAutoFillStart') {
      this.syncEmailFromNativeInput();
    }
  }

  /** Continue is allowed without a prior visitor session; the API mints a visitor when needed. */
  emailLooksValid(): boolean {
    const t = (this.emailGateEl?.nativeElement?.value?.trim() || this.email).trim();
    const at = t.indexOf('@');
    return at > 0 && at < t.length - 1;
  }

  private scheduleAutofillReconciliation(): void {
    if (typeof window === 'undefined') {
      return;
    }
    for (const id of this.autofillTimerIds) {
      clearTimeout(id);
    }
    this.autofillTimerIds = [];
    const delays = [0, 50, 150, 400, 1000, 2500];
    for (const ms of delays) {
      const id = setTimeout(() => this.syncEmailFromNativeInput(), ms);
      this.autofillTimerIds.push(id);
    }
    if (!this.autofillListenersAttached) {
      this.autofillListenersAttached = true;
      window.addEventListener('focus', this.onWindowMaybeAutofill, true);
      window.addEventListener('pointerdown', this.onWindowMaybeAutofill, true);
    }
  }

  submit(): void {
    this.errorMsg = '';
    this.syncEmailFromNativeInput();
    const trimmed = (this.emailGateEl?.nativeElement?.value?.trim() || this.email.trim());
    if (!trimmed.includes('@')) {
      this.errorMsg = 'Enter a valid email.';
      return;
    }

    if (
      this.alreadyCaptured &&
      this.hintEmail &&
      trimmed.toLowerCase() === this.hintEmail.toLowerCase()
    ) {
      this.submitting = true;
      const reqUrl = this.mergedDestinationRequestUrl(true);
      firstValueFrom(this.http.get<RedirectMergeResponse>(reqUrl))
        .then((merged) => {
          this.persistKnownEmail(trimmed);
          window.location.replace(merged.redirectUrl);
        })
        .catch(() => {
          this.persistKnownEmail(trimmed);
          window.location.replace(this.buildClientLandingUrl(trimmed));
        });
      return;
    }

    this.submitting = true;
    const dwellMs = Math.max(0, Date.now() - this.pageEnteredMs);
    const url = this.apiUrl('/capture-email');

    firstValueFrom(
      this.http.post<CaptureEmailResponse>(
        url,
        {
          visitorId: this.visitorId.trim() || null,
          email: trimmed,
          source: this.sourceToken,
          redirect: this.redirectTarget,
          campaign: this.campaign || null,
          dwellMs
        }
      )
    )
      .then((res) => {
        if (res.visitorId?.trim()) {
          this.visitorId = res.visitorId.trim();
          this.syncVisitorStorage(this.visitorId);
        }
        this.persistKnownEmail(trimmed);
        window.location.replace(res.redirectUrl);
      })
      .catch(() => {
        this.persistKnownEmail(trimmed);
        window.location.replace(this.buildClientLandingUrl(trimmed));
      });
  }

  /** Skip: always open destination with only source, email=unknown, campaign (no leadId or other redirect query carry-over). */
  skip(): void {
    this.errorMsg = '';
    if (!this.redirectTarget) {
      return;
    }

    this.submitting = true;
    const dest = this.buildMinimalSkipLandingUrl();
    const dwellMs = Math.max(0, Date.now() - this.pageEnteredMs);
    const url = this.apiUrl('/skip-email-gate');
    firstValueFrom(
      this.http.post<CaptureEmailResponse>(
        url,
        {
          visitorId: this.visitorId.trim() || null,
          source: this.sourceToken,
          redirect: this.redirectTarget,
          campaign: this.campaign || null,
          dwellMs
        }
      )
    )
      .then((res) => {
        if (res.visitorId?.trim()) {
          this.visitorId = res.visitorId.trim();
          this.syncVisitorStorage(this.visitorId);
        }
      })
      .catch(() => {
        /* analytics best-effort */
      })
      .finally(() => {
        window.location.replace(dest);
      });
  }

  private persistKnownEmail(mail: string): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    try {
      if (this.visitorId) {
        localStorage.setItem(`leadScoring.prefill.${this.visitorId}`, mail);
        localStorage.setItem(EmailCaptureComponent.lastVisitorKey, this.visitorId);
      }
      localStorage.setItem(EmailCaptureComponent.prefillGlobalKey, mail);
    } catch {
      /* ignore quota */
    }
  }

  /** Removes visitorId from the visible URL (legacy links) and keeps it in local storage for this device. */
  private readQueryThenStripLegacyVisitor(): URLSearchParams {
    const u = new URL(window.location.href);
    const sp = u.searchParams;
    const legacyVid = sp.get('visitorId')?.trim();
    if (legacyVid) {
      try {
        localStorage.setItem(EmailCaptureComponent.lastVisitorKey, legacyVid);
      } catch {
        /* ignore */
      }
      sp.delete('visitorId');
    }
    const qs = sp.toString();
    const path = `${u.pathname}${qs ? `?${qs}` : ''}${u.hash}`;
    window.history.replaceState(null, '', path);
    return sp;
  }

  private async bootstrapFromParams(
    src: string | null,
    redirect: string | null,
    cmp: string | null
  ): Promise<void> {
    this.pageEnteredMs = Date.now();
    this.paramError = '';

    if (!redirect?.trim()) {
      this.paramError = 'This link is incomplete. Go back and use your campaign link.';
      return;
    }

    try {
      this.redirectTarget = decodeURIComponent(redirect);
    } catch {
      this.redirectTarget = redirect!;
    }

    this.sourceToken = src?.trim() || 'unknown';
    this.campaign = cmp?.trim() || '';

    const hint = await this.loadEmailHint();
    if (!hint?.visitorId?.trim()) {
      this.visitorId = '';
    } else {
      this.visitorId = hint.visitorId.trim();
      this.syncVisitorStorage(this.visitorId);
    }

    this.applyEmailPrefill(hint);
    this.alreadyCaptured = hint?.alreadyCaptured ?? false;
    this.hintEmail = hint?.email?.trim() ? hint!.email!.trim() : null;
    this.scheduleAutofillReconciliation();
  }

  private applyEmailPrefill(hint: EmailHint | null): void {
    const domFirst = this.emailGateEl?.nativeElement?.value?.trim() ?? '';
    if (domFirst.includes('@')) {
      this.email = domFirst;
      return;
    }

    const vid = this.visitorId;
    const fromHint = hint?.email?.trim();
    const perVisitor =
      typeof localStorage !== 'undefined' && vid
        ? localStorage.getItem(`leadScoring.prefill.${vid}`)
        : null;
    const globalLaptop =
      typeof localStorage !== 'undefined'
        ? localStorage.getItem(EmailCaptureComponent.prefillGlobalKey)
        : null;

    if (fromHint) {
      this.email = fromHint;
      return;
    }
    if (perVisitor?.trim()) {
      this.email = perVisitor.trim();
      return;
    }
    if (globalLaptop?.trim()) {
      this.email = globalLaptop.trim();
    }
  }

  /**
   * Optional: restores visitor + known email from the API when a cookie or saved id exists.
   * Form submit uses POST /capture-email to persist the lead and get redirectUrl for the destination site.
   */
  private async loadEmailHint(): Promise<EmailHint | null> {
    const base = this.apiUrl('/track/email-hint');
    try {
      const viaCookie = await firstValueFrom(this.http.get<EmailHint>(base));
      if (viaCookie?.visitorId?.trim()) {
        return viaCookie;
      }
    } catch {
      /* network / server error — try saved id */
    }

    const ls =
      typeof localStorage !== 'undefined'
        ? localStorage.getItem(EmailCaptureComponent.lastVisitorKey)?.trim()
        : '';
    if (!ls) {
      return null;
    }
    try {
      return await firstValueFrom(
        this.http.get<EmailHint>(`${base}?visitorId=${encodeURIComponent(ls)}`)
      );
    } catch {
      return null;
    }
  }

  private syncVisitorStorage(vid: string): void {
    if (typeof localStorage === 'undefined') {
      return;
    }
    try {
      localStorage.setItem(EmailCaptureComponent.lastVisitorKey, vid);
    } catch {
      /* ignore */
    }
  }

  /** Build GET URL against merged-destination; API expects query-string params including full redirect URL. */
  private mergedDestinationRequestUrl(emailCaptured: boolean): string {
    const p = new URLSearchParams();
    p.set('redirect', this.redirectTarget);
    p.set('src', this.sourceToken);
    if (this.campaign) {
      p.set('cmp', this.campaign);
    }
    if (emailCaptured) {
      p.set('emailCaptured', 'true');
    }

    const apiBase = `${this.apiUrl('/track/merged-destination')}`;
    return `${apiBase}?${p.toString()}`;
  }

  /**
   * Mirrors the API redirect query: source, optional email, optional campaign.
   * Used when the API is unreachable so the user still reaches the destination site.
   */
  private buildClientLandingUrl(emailForQuery: string | null): string {
    try {
      const u = new URL(this.redirectTarget);
      const p = new URLSearchParams(u.search);
      const src = (this.sourceToken || 'unknown').trim().toLowerCase();
      p.set('source', src);
      if (this.campaign?.trim()) {
        p.set('campaign', this.campaign.trim());
      }
      if (emailForQuery?.trim()) {
        p.set('email', emailForQuery.trim());
      }
      u.search = p.toString();
      return u.href;
    } catch {
      return this.redirectTarget;
    }
  }

  /** Skip control: same base URL as redirect, query is only source + email=unknown + optional campaign. */
  private buildMinimalSkipLandingUrl(): string {
    try {
      const u = new URL(this.redirectTarget);
      const p = new URLSearchParams();
      p.set('source', (this.sourceToken || 'unknown').trim().toLowerCase());
      p.set('email', 'unknown');
      if (this.campaign?.trim()) {
        p.set('campaign', this.campaign.trim());
      }
      u.search = p.toString();
      return u.href;
    } catch {
      return this.redirectTarget;
    }
  }

  private apiOrigin(): string {
    if (typeof window === 'undefined') {
      return 'http://localhost:5221';
    }

    const host = window.location.hostname;
    if (
      host === 'localhost' ||
      host === '127.0.0.1' ||
      host === '::1' ||
      host === '[::1]'
    ) {
      return 'http://localhost:5221';
    }

    if (this.isPrivateIpv4Host(host)) {
      return `http://${host}:5221`;
    }

    return '';
  }

  private apiUrl(path: string): string {
    const origin = this.apiOrigin().replace(/\/$/, '');
    const p = path.startsWith('/') ? path : `/${path}`;
    return origin ? `${origin}${p}` : p;
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

interface CaptureEmailResponse {
  redirectUrl: string;
  visitorId?: string | null;
}

interface EmailHint {
  email?: string | null;
  alreadyCaptured: boolean;
  visitorId?: string | null;
}

interface RedirectMergeResponse {
  redirectUrl: string;
}
