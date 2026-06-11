import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Runtime config loaded from /config.json before the app starts. Lets the same build
 * target both the same-origin stacks (empty apiBaseUrl → relative /api, used by the
 * Kubernetes Ingress and the dev proxy) and the cross-origin Static Web Apps stack
 * (apiBaseUrl set to the ingestion FQDN), without rebuilding.
 */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  private readonly http = inject(HttpClient);

  apiBaseUrl = '';

  async load(): Promise<void> {
    try {
      const cfg = await firstValueFrom(
        this.http.get<{ apiBaseUrl?: string }>('config.json'),
      );
      this.apiBaseUrl = (cfg.apiBaseUrl ?? '').replace(/\/$/, '');
    } catch {
      this.apiBaseUrl = '';
    }
  }
}
