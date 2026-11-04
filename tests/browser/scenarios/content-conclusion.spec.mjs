// INVESTIGATION-CONTENT-CONCLUSION (INV-032, INV-033): a conclusion is entered as text, saved and reloaded; it is a
// snapshot that does not resolve the investigation; a revision keeps the conclusion's identity and the earlier
// statement stays in the history; several conclusions can be recorded; invalid statements are refused.
import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import * as gh from "../support/github.mjs";
import * as causa from "../support/causa.mjs";

test.skip(gh.live, "runs against the GitHub double; the live round trip is content-e2e");

test("INVESTIGATION-CONTENT-CONCLUSION: record, refuse invalid, revise, record another, save and reload", async ({ browser, page, request }) => {
  const store = gh.createStore();
  await gh.install(page.context(), store);
  await causa.open(page);
  await causa.connected(page);
  await causa.gotoStart(page);
  await causa.submitStart(page, { statement: "Packing line PL-3 rejects 4% of cartons.", identifier: randomUUID() });
  const id = await causa.investigationId(page);

  // Invalid statements are refused and nothing is recorded.
  expect(await causa.recordConclusion(page, "   ")).toBe(false);
  await expect(page.locator("#con-statement-error")).toContainText("Conclusion statement is required");
  await page.fill("#con-statement", "z".repeat(10001));
  expect(await causa.submitEditor(page, "Record conclusion")).toBe(false);
  await expect(page.locator("#con-statement-error")).toContainText("at most 10000");
  await page.locator('[data-requirement-id="INV-036"]').click();
  expect(await causa.count(page, "conclusion")).toBe(0);

  // Record: the text itself is shown; the investigation is not resolved by it.
  const first = "Glue temperature drift is the most likely cause of the rejects.";
  expect(await causa.recordConclusion(page, first)).toBe(true);
  expect(await causa.texts(page, "conclusion-statement")).toEqual([first]);
  await expect(page.locator('[data-requirement-id="INV-018"]')).toHaveText("Open");
  const conclusionId = await page.locator('[data-testid="conclusion"]').first().getAttribute("data-id");

  // Revise: same identity, new text, earlier text kept in the history.
  const revised = "Revised: glue temperature drift after 2 hours of running causes the rejects.";
  await page.locator('[data-testid="conclusion"]').first().getByRole("button", { name: "Revise conclusion" }).click();
  await expect(page.locator("#con-statement")).toHaveValue(first);
  await page.fill("#con-statement", revised);
  expect(await causa.submitEditor(page, "Record conclusion")).toBe(true);
  expect(await causa.texts(page, "conclusion-statement")).toEqual([revised]);
  expect(await page.locator('[data-testid="conclusion"]').first().getAttribute("data-id")).toBe(conclusionId);
  const history = (await causa.texts(page, "history-text")).join("\n");
  expect(history).toContain(`Conclusion ${conclusionId} recorded: "${first}"`);
  expect(history).toContain(`revised to: "${revised.length > 80 ? `${revised.slice(0, 77)}…` : revised}"`);

  // A second conclusion is a separate snapshot.
  const second = "Second snapshot: carton board humidity is not a factor.";
  expect(await causa.recordConclusion(page, second)).toBe(true);
  expect(await causa.save(page)).toBe("Saved to GitHub");

  const held = causa.replay(await gh.storedDocument(store, request, id));
  expect(held.conclusions.map((c) => c.statement)).toEqual([revised, second]);

  const other = await browser.newContext();
  await gh.install(other, store);
  const page2 = await other.newPage();
  await causa.reopen(page2, id);
  expect(await causa.texts(page2, "conclusion-statement")).toEqual([revised, second]);
  expect(await page2.locator('[data-testid="conclusion"]').first().getAttribute("data-id")).toBe(conclusionId);
  await expect(page2.locator('[data-requirement-id="INV-018"]')).toHaveText("Open");
  expect((await causa.texts(page2, "history-text")).join("\n")).toContain(first);
  await other.close();
});
