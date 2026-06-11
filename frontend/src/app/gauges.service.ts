import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer, switchMap, catchError, of } from 'rxjs';
import { Gauge } from './gauge.model';

@Injectable({ providedIn: 'root' })
export class GaugesService {
  private readonly http = inject(HttpClient);

  // Poll /api/gauges every 4s. switchMap drops an in-flight request when the next tick
  // fires; on error we fall back to an empty list so a brief API outage (e.g. a restart)
  // does not break the view.
  readonly gauges = toSignal(
    timer(0, 4000).pipe(
      switchMap(() =>
        this.http.get<Gauge[]>('/api/gauges').pipe(catchError(() => of<Gauge[]>([])))),
    ),
    { initialValue: [] as Gauge[] },
  );
}
