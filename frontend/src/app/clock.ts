import { Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer, map } from 'rxjs';

/**
 * Shared 1 Hz wall-clock tick (epoch ms). One timer for the whole app so every view's
 * relative time (measurement age, time-in-stage) advances between polls without each
 * component spawning its own interval.
 */
@Injectable({ providedIn: 'root' })
export class Clock {
  readonly now = toSignal(
    timer(0, 1000).pipe(map(() => Date.now())),
    { initialValue: Date.now() },
  );
}
