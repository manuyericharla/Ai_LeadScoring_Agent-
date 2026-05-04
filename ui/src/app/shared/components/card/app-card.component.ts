import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-card',
  standalone: true,
  templateUrl: './app-card.component.html',
  styleUrl: './app-card.component.scss',
  host: { class: 'app-card-host' }
})
export class AppCardComponent {
  @Input({ alias: 'heading' }) heading = '';
  @Input() subheading = '';
}
