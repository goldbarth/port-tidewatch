import {
  ApplicationConfig,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  inject,
} from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { AppConfig } from './app-config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    // Load runtime config (API base URL) before the app starts.
    provideAppInitializer(() => inject(AppConfig).load()),
  ]
};
