import { CommonModule } from '@angular/common';
import { Component, input } from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';

export type MetricTrendDirection = 'up' | 'down' | 'neutral';

@Component({
  selector: 'app-metric-card',
  standalone: true,
  imports: [CommonModule, IonIcon],
  templateUrl: './metric-card.component.html',
  styleUrl: './metric-card.component.scss',
})
export class MetricCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly supportingText = input<string>('');
  readonly icon = input<string>('analytics-outline');
  readonly tone = input<'orange' | 'blue' | 'yellow' | 'green'>('blue');
  readonly trendValue = input<string>('');
  readonly trendDirection = input<MetricTrendDirection>('neutral');
  readonly trendLabel = input<string>('vs ontem');
  readonly trendEmphasis = input<boolean>(false);
  readonly emphasisLabel = input<string>('');
}
