// Real-browser suites for Causa. They drive the built site (dist/, written by `npm run build`) in Chromium.
// GitHub is the real repository when CAUSA_GITHUB_TOKEN is set (live), otherwise the stateful Contents API double
// in tests/browser/support/github.mjs. The JSON report feeds `dokimos verification requirements`.
import { existsSync } from "node:fs";
import { defineConfig, devices } from "@playwright/test";

const port = 4321;
const origin = `http://127.0.0.1:${port}`;

const preinstalledChromium = "/opt/pw-browsers/chromium";
const launchOptions = existsSync(preinstalledChromium) ? { executablePath: preinstalledChromium } : {};

export default defineConfig({
  testDir: "./tests/browser",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 240_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["json", { outputFile: process.env.CAUSA_PLAYWRIGHT_JSON ?? "test-evidence/playwright.json" }]],
  use: { baseURL: origin, trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], launchOptions } }],
  webServer: {
    command: `python3 -m http.server ${port} --bind 127.0.0.1 --directory dist`,
    url: `${origin}/index.html`,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000
  }
});
