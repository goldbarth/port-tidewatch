import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AppConfig } from './app-config';

describe('AppConfig', () => {
  let config: AppConfig;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AppConfig, provideHttpClient(), provideHttpClientTesting()],
    });
    config = TestBed.inject(AppConfig);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  async function loadWith(body: { apiBaseUrl?: string }): Promise<void> {
    const loaded = config.load();
    httpMock.expectOne('config.json').flush(body);
    await loaded;
  }

  it('treats the unresolved deploy placeholder as same-origin', async () => {
    // The committed config.json ships this until the SWA deploy substitutes it.
    await loadWith({ apiBaseUrl: 'https://<INGESTION_FQDN>' });
    expect(config.apiBaseUrl).toBe('');
  });

  it('treats an empty value as same-origin', async () => {
    await loadWith({ apiBaseUrl: '' });
    expect(config.apiBaseUrl).toBe('');
  });

  it('keeps a real cross-origin FQDN and trims a trailing slash', async () => {
    await loadWith({ apiBaseUrl: 'https://ingestion.example.com/' });
    expect(config.apiBaseUrl).toBe('https://ingestion.example.com');
  });

  it('falls back to same-origin when config.json cannot be fetched', async () => {
    const loaded = config.load();
    httpMock.expectOne('config.json').error(new ProgressEvent('network error'));
    await loaded;
    expect(config.apiBaseUrl).toBe('');
  });
});
