import { defineConfig } from '@playwright/test';
import { BASE_URL } from './helpers/env';

// One serial scenario in the installed Chrome (no browser download). global-setup prepares the database, Mailpit,
// the .NET server and the Angular dev server on dedicated ports, and tears down only what it started.
export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 150_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    baseURL: BASE_URL,
    channel: 'chrome',
    headless: true,
    locale: 'he-IL',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
