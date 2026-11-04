// Smoke: the built site loads every resource it references, starts the WebAssembly engine and raises no error.
import { expect, test } from "@playwright/test";

test("the Causa page loads every resource and starts its engine", async ({ page }) => {
  const failures = [];
  page.on("response", (response) => {
    if (response.status() >= 400) failures.push(`${response.status()} ${response.url()}`);
  });
  page.on("requestfailed", (request) => failures.push(`failed ${request.url()}`));
  page.on("pageerror", (error) => failures.push(`script error: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") failures.push(`console error: ${message.text()}`);
  });

  await page.goto("/index.html");
  await page.waitForFunction(() => document.documentElement.dataset.causaReady, null, { timeout: 45_000 });

  expect(await page.evaluate(() => document.documentElement.dataset.causaReady)).toBe("running");
  await expect(page.getByRole("heading", { name: "Connect to GitHub" })).toBeVisible();
  expect(failures).toEqual([]);
});
