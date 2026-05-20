import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../shared/services/auth.service';

type AuthMode = 'login' | 'signup';
type SignupStep = 1 | 2 | 3;

@Component({
  selector: 'app-auth-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './auth-page.component.html',
  styleUrl: './auth-page.component.scss'
})
export class AuthPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  mode: AuthMode = 'login';
  signupStep: SignupStep = 1;

  firstName = '';
  lastName = '';
  email = '';
  password = '';
  confirmPassword = '';
  company = '';
  plan = '';
  plans: string[] = [];
  rememberMe = false;
  showPassword = false;
  showConfirmPassword = false;

  submitting = false;
  error = '';

  readonly features = [
    {
      icon: 'chart',
      title: 'Lead scoring & stages',
      desc: 'Track Cold → Warm → MQL → Hot with automated scoring.'
    },
    {
      icon: 'mail',
      title: 'Email nurture campaigns',
      desc: 'Batch follow-ups and welcome sequences per product.'
    },
    {
      icon: 'users',
      title: 'Company-isolated data',
      desc: 'Each company gets its own dedicated lead database.'
    },
    {
      icon: 'link',
      title: 'Universal tracking links',
      desc: 'Attribute traffic from email, social, and your website.'
    }
  ];

  readonly planDetails: Record<string, { price: string; blurb: string }> = {
    Starter: { price: 'Free trial', blurb: 'Up to 500 leads · 1 product' },
    Professional: { price: '$49/mo', blurb: 'Up to 5,000 leads · 5 products' },
    Enterprise: { price: 'Custom', blurb: 'Unlimited leads · dedicated support' }
  };

  ngOnInit(): void {
    const path = this.route.snapshot.routeConfig?.path ?? '';
    this.mode = path === 'signup' ? 'signup' : 'login';

    this.auth.getPlans().subscribe({
      next: (res) => {
        this.plans = res.plans?.length ? res.plans : ['Starter', 'Professional', 'Enterprise'];
        if (!this.plan) {
          this.plan = this.plans[0];
        }
      },
      error: () => {
        this.plans = ['Starter', 'Professional', 'Enterprise'];
        this.plan = this.plans[0];
      }
    });
  }

  setMode(mode: AuthMode): void {
    this.mode = mode;
    this.error = '';
    this.signupStep = 1;
    const path = mode === 'signup' ? '/signup' : '/login';
    void this.router.navigate([path], { replaceUrl: true });
  }

  get passwordStrength(): number {
    const p = this.password;
    if (!p) return 0;
    let score = 0;
    if (p.length >= 8) score++;
    if (p.length >= 12) score++;
    if (/[A-Z]/.test(p) && /[a-z]/.test(p)) score++;
    if (/\d/.test(p)) score++;
    if (/[^A-Za-z0-9]/.test(p)) score++;
    return Math.min(4, score);
  }

  get passwordStrengthLabel(): string {
    const s = this.passwordStrength;
    if (!this.password) return '';
    if (s <= 1) return 'Weak';
    if (s <= 2) return 'Fair';
    if (s <= 3) return 'Good';
    return 'Strong';
  }

  nextSignupStep(): void {
    this.error = '';
    if (this.signupStep === 1) {
      if (!this.firstName.trim() || !this.lastName.trim()) {
        this.error = 'First name and last name are required.';
        return;
      }
      if (!this.email.trim() || !this.email.includes('@')) {
        this.error = 'A valid work email is required.';
        return;
      }
      if (this.password.length < 8) {
        this.error = 'Password must be at least 8 characters.';
        return;
      }
      if (this.password !== this.confirmPassword) {
        this.error = 'Passwords do not match.';
        return;
      }
      this.signupStep = 2;
      return;
    }

    if (this.signupStep === 2) {
      if (!this.company.trim()) {
        this.error = 'Company name is required.';
        return;
      }
      this.signupStep = 3;
    }
  }

  prevSignupStep(): void {
    this.error = '';
    if (this.signupStep > 1) {
      this.signupStep = (this.signupStep - 1) as SignupStep;
    }
  }

  selectPlan(p: string): void {
    this.plan = p;
  }

  planPrice(name: string): string {
    return this.planDetails[name]?.price ?? '';
  }

  planBlurb(name: string): string {
    return this.planDetails[name]?.blurb ?? '';
  }

  submitLogin(): void {
    this.error = '';
    const email = this.email.trim();
    if (!email || !this.password) {
      this.error = 'Email and password are required.';
      return;
    }

    this.submitting = true;
    this.auth.login(email, this.password).subscribe({
      next: () => {
        this.submitting = false;
        void this.router.navigate(['/dashboard']);
      },
      error: (err: unknown) => {
        this.submitting = false;
        this.error = this.extractError(err, 'Invalid email or password.');
      }
    });
  }

  submitSignup(): void {
    this.error = '';
    if (!this.plan) {
      this.error = 'Please choose a plan.';
      return;
    }

    this.submitting = true;
    this.auth
      .signup({
        firstName: this.firstName.trim(),
        lastName: this.lastName.trim(),
        email: this.email.trim(),
        password: this.password,
        confirmPassword: this.confirmPassword,
        company: this.company.trim(),
        plan: this.plan
      })
      .subscribe({
        next: () => {
          this.submitting = false;
          void this.router.navigate(['/dashboard']);
        },
        error: (err: unknown) => {
          this.submitting = false;
          this.error = this.extractError(err, 'Signup failed. Please try again.');
        }
      });
  }

  private extractError(err: unknown, fallback: string): string {
    if (err instanceof HttpErrorResponse) {
      return (err.error as { message?: string })?.message ?? fallback;
    }
    return fallback;
  }
}
