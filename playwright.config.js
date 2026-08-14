import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: './tests/e2e',
    fullyParallel: false,
    workers: 1,
    retries: process.env.CI ? 1 : 0,
    timeout: 60_000,
    expect: {
        timeout: 10_000
    },
    reporter: [
        ['line'],
        ['html', { outputFolder: 'artifacts/playwright-report', open: 'never' }]
    ],
    outputDir: 'artifacts/playwright-results',
    use: {
        baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://jellyfin:8096',
        screenshot: 'only-on-failure',
        trace: 'retain-on-failure',
        video: 'retain-on-failure'
    }
});
