import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../shared/services/auth.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss'
})
export class SidebarComponent {
  readonly auth = inject(AuthService);

  @Input() drawerOpen = false;
  @Output() drawerClose = new EventEmitter<void>();

  navClick(): void {
    this.drawerClose.emit();
  }

  logout(): void {
    this.auth.logout();
    this.drawerClose.emit();
  }
}
