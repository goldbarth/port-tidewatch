import { Injectable, inject, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer, switchMap, map, catchError, of, scan } from 'rxjs';
import { Gauge, GaugesState } from './gauge.model';
import { AppConfig } from './app-config';

const INITIAL: GaugesState = { gauges: [], lastUpdated: null, connected: false };

@Injectable({ providedIn: 'root' })
export class GaugesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  // Poll {apiBaseUrl}/api/gauges every 4s. apiBaseUrl is empty for the same-origin
  // stacks (relative /api), set to the ingestion FQDN for Static Web Apps. switchMap
  // drops an in-flight request when the next tick fires. On error we keep the last
  // successful snapshot and only flip `connected` to false — a brief API outage
  // (e.g. a restart) surfaces as a stale indicator instead of blanking the view.
  readonly state = toSignal(
    timer(0, 4000).pipe(
      switchMap(() =>
        this.http.get<Gauge[]>(`${this.config.apiBaseUrl}/api/gauges`).pipe(
          map((gauges) => ({ ok: true as const, gauges })),
          catchError(() => of({ ok: false as const })),
        ),
      ),
      scan<{ ok: true; gauges: Gauge[] } | { ok: false }, GaugesState>(
        (prev, res) =>
          res.ok
            ? { gauges: res.gauges, lastUpdated: Date.now(), connected: true }
            : { ...prev, connected: false },
        INITIAL,
      ),
    ),
    { initialValue: INITIAL },
  );

  // Convenience projection for views that only need the gauges.
  readonly gauges = computed(() => this.state().gauges);
}
