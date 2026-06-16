import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Runtime config loaded from /config.json before the app starts. Lets the same build
 * target both the same-origin stacks (empty apiBaseUrl → relative /api, used by the
 * Kubernetes Ingress and the dev proxy) and the cross-origin Static Web Apps stack
 * (apiBaseUrl set to the ingestion FQDN), without rebuilding.
 *
 * `jaegerBaseUrl` is the browser-reachable Jaeger UI base (M8 deep-link); empty where
 * Jaeger is not exposed (e.g. the Container Apps stack), in which case no link is shown.
 */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  private readonly http = inject(HttpClient);

  apiBaseUrl = '';
  jaegerBaseUrl = '';

  async load(): Promise<void> {
    try {
      const cfg = await firstValueFrom(
        this.http.get<{ apiBaseUrl?: string; jaegerBaseUrl?: string }>('config.json'),
      );
      this.apiBaseUrl = (cfg.apiBaseUrl ?? '').replace(/\/$/, '');
      this.jaegerBaseUrl = (cfg.jaegerBaseUrl ?? '').replace(/\/$/, '');
    } catch {
      this.apiBaseUrl = '';
      this.jaegerBaseUrl = '';
    }
  }
}
