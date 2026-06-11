import { Component, inject } from '@angular/core';
import { GaugesService } from './gauges.service';
import { GaugeCard } from './gauge-card/gauge-card';

@Component({
  selector: 'app-root',
  imports: [GaugeCard],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly service = inject(GaugesService);
  readonly gauges = this.service.gauges;
}
