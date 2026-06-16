import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Runtime config loaded from /config.json before the app starts. Lets the same build
 * target both the same-origin stacks (empty apiBaseUrl → relative /api, used by the
 * Kubernetes Ingress and the dev proxy) and the cross-origin Static Web Apps stack
 * (apiBaseUrl set to the ingestion FQDN), without rebuilding.
 *
 * The committed config.json ships the deploy placeholder "https://<INGESTION_FQDN>",
 * which the Static Web Apps deploy substitutes. Until it is substituted (local dev,
 * any same-origin stack), the placeholder is treated as unset so the app falls back to
 * relative /api through the dev proxy — the dashboard works on a fresh clone with no
 * config edit.
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
      this.apiBaseUrl = AppConfig.normalize(cfg.apiBaseUrl);
    } catch {
      this.apiBaseUrl = '';
    }
  }

  /** Empty for an unset or unresolved-placeholder value; trimmed, no trailing slash otherwise. */
  private static normalize(value: string | undefined): string {
    const raw = (value ?? '').trim();
    if (raw === '' || raw.includes('<')) return ''; // unresolved "<…>" deploy token
    return raw.replace(/\/$/, '');
  }
}
