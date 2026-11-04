// INVESTIGATION-CONTENT-NEGATIVE: the failure scenarios of the Rehearsal 009 acceptance contract
// (docs/rehearsal-009-content-readiness/FINAL-READINESS.md). Each scenario drives the real page against the GitHub
// Contents double, injects the provider answer the scenario names, and asserts what the user sees and what GitHub
// then holds. Field-rule refusals are also probed per requirement in requirements/content-validation.spec.mjs.
import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import * as gh from "../support/github.mjs";
import * as causa from "../support/causa.mjs";

test.skip(gh.live, "fault injection needs the GitHub double");

const saveState = (page) => page.locator('[data-requirement-id="INV-016"]');

const startWith = async (page, store) => {
  await gh.install(page.context(), store);
  await causa.open(page);
  await causa.connected(page);
  await causa.gotoStart(page);
  await causa.submitStart(page, { statement: "Compressor C-4 overheats after restarts.", title: "C-4 overheating", identifier: randomUUID() });
  await causa.recordObservation(page, { text: "Discharge temperature reached 118 °C after the 07:00 restart." });
  return causa.investigationId(page);
};

test.describe("INVESTIGATION-CONTENT-NEGATIVE scenarios", () => {
  test("missing investigation name (problem statement): refused with a field message; nothing is created", async ({ page }) => {
    const store = gh.createStore();
    await gh.install(page.context(), store);
    await causa.open(page);
    await causa.connected(page);
    await causa.gotoStart(page);
    const opened = await causa.submitStart(page, { title: "Untitled run", statement: "   " });
    expect(opened).toBe(false);
    await expect(page.locator("#start-problem-error")).toContainText("Problem statement is required");
    await expect(page.locator("#start-problem")).toHaveAttribute("aria-invalid", "true");
    expect(store.writes).toHaveLength(0);
  });

  test("empty observation and empty hypothesis: refused, nothing recorded, editor stays open", async ({ page }) => {
    const store = gh.createStore();
    await startWith(page, store);
    const observations = await causa.count(page, "observation");
    expect(await causa.recordObservation(page, { text: "  " })).toBe(false);
    await expect(page.locator("#obs-text-error")).toContainText("Observation text is required");
    expect(await causa.count(page, "observation")).toBe(observations);
    await page.locator('[data-requirement-id="INV-036"]').click();
    expect(await causa.recordHypothesis(page, { statement: "" })).toBe(false);
    await expect(page.locator("#hyp-statement-error")).toContainText("Hypothesis statement is required");
    expect(await causa.count(page, "hypothesis")).toBe(0);
  });

  test("invalid evidence URL: refused, never rendered as a link", async ({ page }) => {
    const store = gh.createStore();
    await startWith(page, store);
    for (const url of ["javascript:alert(1)", "ftp://example.com/x", "example.com/no-scheme", "https://exa mple.com"]) {
      if ((await page.locator("#ev-label").count()) === 0) await page.getByRole("button", { name: "Record evidence", exact: true }).click();
      await page.fill("#ev-label", "Suspicious link");
      await page.fill("#ev-url", url);
      expect(await causa.submitEditor(page, "Record evidence")).toBe(false);
      await expect(page.locator("#ev-url-error")).toContainText("absolute http:// or https://");
    }
    expect(await causa.count(page, "evidence")).toBe(0);
    expect(await page.locator('a[href^="javascript:"]').count()).toBe(0);
  });

  test("invalid relationship target: a link to evidence or a hypothesis that does not exist is refused", async ({ page }) => {
    const store = gh.createStore();
    await startWith(page, store);
    await causa.recordHypothesis(page, { statement: "Fouled intercooler reduces cooling." });
    await causa.recordEvidence(page, { label: "Intercooler photo" });
    // A target that does not exist can only reach the engine if the page is tampered with: send it in one change event.
    const tamper = (selector, value) => page.evaluate(([sel, v]) => {
      const select = document.querySelector(sel);
      select.options[1].value = v;
      select.value = v;
      select.dispatchEvent(new Event("change", { bubbles: true }));
    }, [selector, value]);
    await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
    await tamper("#link-evidence", "00000000-0000-0000-0000-000000000000");
    await page.selectOption("#link-hypothesis", { index: 1 });
    await page.check('input[name=relationshipKind][value="supports"]');
    await page.fill("#link-rationale", "Fouling visible on the fins.");
    expect(await causa.submitEditor(page, "Record link")).toBe(false);
    await expect(page.locator("#link-evidence-error")).toContainText("evidence item that exists");
    // and a hypothesis that does not exist
    await page.selectOption("#link-evidence", { index: 1 });
    await tamper("#link-hypothesis", "11111111-1111-1111-1111-111111111111");
    expect(await causa.submitEditor(page, "Record link")).toBe(false);
    await expect(page.locator("#link-hypothesis-error")).toContainText("hypothesis that exists");
    expect(await causa.count(page, "relationship")).toBe(0);
  });

  test("a second link for the same evidence-hypothesis pair is refused and points to the existing one", async ({ page }) => {
    const store = gh.createStore();
    await startWith(page, store);
    await causa.recordHypothesis(page, { statement: "Fouled intercooler reduces cooling." });
    await causa.recordEvidence(page, { label: "Intercooler photo" });
    expect(await causa.linkEvidence(page, { evidence: "Intercooler photo", hypothesis: "Fouled intercooler reduces cooling.", kind: "supports", rationale: "Fouling visible." })).toBe(true);
    expect(await causa.linkEvidence(page, { evidence: "Intercooler photo", hypothesis: "Fouled intercooler reduces cooling.", kind: "contradicts", rationale: "Second opinion." })).toBe(false);
    await expect(page.locator("#link-hypothesis-error")).toContainText("already assessed against this hypothesis (Supports)");
    expect(await causa.count(page, "relationship")).toBe(1);
  });

  test("duplicate submission: a double-activated Save writes once; a double-submitted record records once", async ({ page }) => {
    const store = gh.createStore();
    await startWith(page, store);
    await page.getByRole("button", { name: "Record an observation", exact: true }).click();
    await page.fill("#obs-text", "Fan belt is loose.");
    await page.getByRole("button", { name: "Record observation", exact: true }).dblclick({ force: true }).catch(() => {});
    await page.waitForTimeout(100);
    expect((await causa.texts(page, "observation-text")).filter((t) => t === "Fan belt is loose.")).toHaveLength(1);
    // Save twice before the first answer arrives (the double delays its answer).
    await page.context().route("https://api.github.com/**", async (route) => {
      if (route.request().method() === "PUT") await new Promise((r) => setTimeout(r, 400));
      await route.fallback();
    });
    const save = page.getByRole("button", { name: "Save", exact: true });
    await save.click();
    await save.click({ force: true }).catch(() => {});
    await expect(saveState(page)).toHaveText("Saved to GitHub", { timeout: 20_000 });
    expect(store.writes).toHaveLength(1);
  });

  test("save failure: a server error is reported, nothing is lost, and Retry saves", async ({ page, request }) => {
    const store = gh.createStore();
    const id = await startWith(page, store);
    gh.injectFault(store, { method: "PUT", status: 503, body: { message: "Service Unavailable" } });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(saveState(page)).toContainText("Save failed");
    await expect(saveState(page)).toHaveAttribute("data-state", "failed");
    expect(await gh.storedDocument(store, request, id)).toBeNull();
    expect(await causa.texts(page, "observation-text")).toEqual(["Discharge temperature reached 118 °C after the 07:00 restart."]);
    gh.injectFault(store, { method: "PUT", abort: "internetdisconnected" });
    await page.getByRole("button", { name: "Retry save" }).click();
    await expect(saveState(page)).toContainText(/Save failed|could not be reached/);
    await page.getByRole("button", { name: "Retry save" }).click();
    await expect(saveState(page)).toHaveText("Saved to GitHub");
    expect(causa.replay(await gh.storedDocument(store, request, id)).observations[0].text).toBe("Discharge temperature reached 118 °C after the 07:00 restart.");
  });

  test("authentication failure: a refused credential at connect and at save is reported; reconnecting saves", async ({ page, request }) => {
    const store = gh.createStore();
    await gh.install(page.context(), store);
    await causa.open(page);
    await causa.connect(page, "wrong-token");
    await expect(page.getByRole("alert").filter({ hasText: "GitHub refused the credential (401)" })).toBeVisible();
    await expect(page.locator(".causa-connection")).toContainText("Not connected");
    expect(await page.content()).not.toContain("wrong-token");
    await causa.connected(page);
    await causa.gotoStart(page);
    await causa.submitStart(page, { statement: "Chiller CH-1 short-cycles.", identifier: randomUUID() });
    const id = await causa.investigationId(page);
    gh.injectFault(store, { method: "PUT", status: 401, body: { message: "Bad credentials" } });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(saveState(page)).toContainText("Authentication failed");
    await expect(page.locator("#connect-heading")).toBeVisible();
    expect(await gh.storedDocument(store, request, id)).toBeNull();
    await causa.connected(page);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(saveState(page)).toHaveText("Saved to GitHub");
  });

  test("conflict: a save over a copy changed elsewhere is refused as a conflict; nothing is overwritten", async ({ browser, page, request }) => {
    const store = gh.createStore();
    const id = await startWith(page, store);
    expect(await causa.save(page)).toBe("Saved to GitHub");
    // Another browser edits and saves the same investigation.
    const other = await browser.newContext();
    await gh.install(other, store);
    const page2 = await other.newPage();
    await causa.reopen(page2, id);
    await causa.recordObservation(page2, { text: "Recorded in the other browser." });
    expect(await causa.save(page2)).toBe("Saved to GitHub");
    // This browser's copy is now stale.
    await causa.recordObservation(page, { text: "Recorded in this browser." });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(saveState(page)).toContainText("Conflict");
    const held = causa.replay(await gh.storedDocument(store, request, id));
    expect(held.observations.map((o) => o.text)).toContain("Recorded in the other browser.");
    expect(held.observations.map((o) => o.text)).not.toContain("Recorded in this browser.");
    // Resolve by loading the saved copy.
    await page.getByRole("button", { name: "Discard my changes and load the saved copy" }).click();
    await expect(page.locator('[data-testid="observation-text"]').filter({ hasText: "Recorded in the other browser." })).toBeVisible();
    await expect(saveState(page)).toHaveText("Saved to GitHub");
    await other.close();
  });

  test("malformed reload: a corrupt or invalid saved document is refused with a message; nothing is loaded", async ({ browser, page }) => {
    const store = gh.createStore();
    const id = await startWith(page, store);
    expect(await causa.save(page)).toBe("Saved to GitHub");
    const corrupt = [
      ["not JSON", "{ this is not json"],
      ["wrong schema", JSON.stringify({ schema: "something-else/9", id, events: [] })],
      ["unknown relationship kind", JSON.stringify({ schema: "causa.investigation/1", id, events: [
        { sequence: 1, actor: "x", at: "2026-11-04T00:00:00Z", type: "started", id, createdAt: "2026-11-04T00:00:00Z", title: "", problemStatement: "s", question: "" },
        { sequence: 2, actor: "x", at: "2026-11-04T00:00:00Z", type: "relationship-recorded", id: "r", evidenceId: "e", hypothesisId: "h", kind: "proves", rationale: "r" }] })],
      ["broken sequence", JSON.stringify({ schema: "causa.investigation/1", id, events: [
        { sequence: 2, actor: "x", at: "2026-11-04T00:00:00Z", type: "started", id, createdAt: "2026-11-04T00:00:00Z", title: "", problemStatement: "s", question: "" }] })],
    ];
    for (const [name, text] of corrupt) {
      gh.tamper(store, id, text);
      const context = await browser.newContext();
      await gh.install(context, store);
      const fresh = await context.newPage();
      await causa.open(fresh, `#/investigation/${id}`);
      await causa.connect(fresh);
      await expect(fresh.getByRole("alert").filter({ hasText: "could not be read" }), name).toBeVisible();
      await expect(fresh.locator("#ws-heading"), name).toHaveCount(0);
      await context.close();
    }
  });
});
