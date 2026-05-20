import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { apiUrl } from '../helpers/api-base.helper';

export interface AuthUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  companyName: string;
  selectedPlan: string;
}

export interface AuthResponse {
  token: string;
  user: AuthUser;
}

const TOKEN_KEY = 'leadScoring.authToken';
const USER_KEY = 'leadScoring.authUser';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly user = signal<AuthUser | null>(this.loadUser());
  readonly isLoggedIn = signal(!!this.getToken());

  getPlans(): Observable<{ plans: string[] }> {
    return this.http.get<{ plans: string[] }>(apiUrl('/api/auth/plans'));
  }

  signup(payload: {
    firstName: string;
    lastName: string;
    email: string;
    password: string;
    confirmPassword: string;
    company: string;
    plan: string;
  }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(apiUrl('/api/auth/signup'), payload).pipe(tap((res) => this.persistSession(res)));
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(apiUrl('/api/auth/login'), { email, password }).pipe(tap((res) => this.persistSession(res)));
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this.user.set(null);
    this.isLoggedIn.set(false);
    void this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  private persistSession(response: AuthResponse): void {
    localStorage.setItem(TOKEN_KEY, response.token);
    localStorage.setItem(USER_KEY, JSON.stringify(response.user));
    this.user.set(response.user);
    this.isLoggedIn.set(true);
  }

  private loadUser(): AuthUser | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) {
      return null;
    }
    try {
      return JSON.parse(raw) as AuthUser;
    } catch {
      return null;
    }
  }
}
