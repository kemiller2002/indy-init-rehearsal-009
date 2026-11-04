// INVESTIGATION-CONTENT-NEGATIVE, field rules: every value rule the manifest states (minLength, maxLength,
// pattern, options) is violated at least once through the page, and at least one valid value is tried. The
// values come from the manifest, not from the application. Probes record whether the page accepted each value
// and whether a refused value changed anything; Dokimos judges.
import { randomUUID } from "node:crypto";
import { test } from "@playwright/test";
import * as gh from "../support/github.mjs";
import * as causa from "../support/causa.mjs";
import { elements, recorder, requirement } from "../support/probe.mjs";

/** Valid and invalid values for a requirement, derived from its manifest rules. */
// `alsoRequired`: the field is also governed by another requirement's minLength (INV-021's field is the problem
// statement, which INV-019 makes non-empty), so an empty value is not a valid value for the field and is not tried
// as one; INV-019's own probe tries it as a violation.
const valuesFor = (id, valid, { alsoRequired = false } = {}) => {
  const v = requirement(id).validation;
  const invalid = [];
  if ((v.minLength ?? 0) >= 1) invalid.push("", "   ");
  if (v.maxLength) invalid.push("x".repeat(v.maxLength + 1));
  if (v.pattern) invalid.push(v.pattern.includes("https?") ? "ftp://example.com/file" : "not-a-valid-identifier", ...(v.pattern.includes("https?") ? ["javascript:alert(1)"] : []));
  if (v.options && v.options.length > 0) invalid.push("maybe");
  const validValues = [...valid];
  if (v.maxLength) validValues.push("y".repeat(v.maxLength));
  if ((v.minLength ?? 0) < 1 && !v.options?.length && !alsoRequired) validValues.push("");
  return { valid: validValues, invalid };
};

/** Try each value; `attempt` returns { accepted, observed }. `state` reads what a refusal must leave unchanged. */
const probeRules = async (probe, id, found, values, attempt, state) => {
  const all = [];
  const invalidOnly = [];
  const before = await state();
  for (const value of values.invalid) {
    const r = await attempt(value);
    all.push({ value, observed: r.observed, accepted: r.accepted });
    invalidOnly.push({ value, observed: r.observed, accepted: r.accepted });
  }
  const afterInvalid = await state();
  for (const value of values.valid) {
    const r = await attempt(value);
    all.push({ value, observed: r.observed, accepted: r.accepted });
  }
  if (requirement(id).obligations.some((o) => o.check === "validation")) probe.attempts(id, "validation", found, all);
  if (requirement(id).obligations.some((o) => o.check === "rejects")) probe.attempts(id, "rejects", found, invalidOnly, before, afterInvalid);
};

test.describe("INVESTIGATION-CONTENT-NEGATIVE", () => {
  test("start screen rules: identifier, problem statement, title, question; cancel writes nothing", async ({ page }, testInfo) => {
    const probe = recorder(testInfo);
    probe.fact("domain-profile-selected", false);
    const store = gh.createStore();
    try {
      await gh.install(page.context(), store);
      await causa.open(page);
      await causa.connected(page);
      let started = 0;
      const startState = async () => `start screen; ${started} started; ${store.writes.length} written`;
      const tryStart = (field) => async (value) => {
        await causa.gotoStart(page);
        const fields = { statement: "Line 2 conveyor stops intermittently.", identifier: randomUUID(), title: "", question: "", [field]: value };
        const accepted = await causa.submitStart(page, fields);
        if (accepted) started += 1;
        return { accepted, observed: value };
      };
      await causa.gotoStart(page);
      await probeRules(probe, "INV-006", await elements(page, "INV-006"), valuesFor("INV-006", [randomUUID(), randomUUID().toUpperCase()]), tryStart("identifier"), startState);
      await probeRules(probe, "INV-019", null, valuesFor("INV-019", ["Pump P-7 leaks."]), tryStart("statement"), startState);
      await probeRules(probe, "INV-021", null, valuesFor("INV-021", ["Valve V-3 sticks when cold."], { alsoRequired: true }), tryStart("statement"), startState);
      await causa.gotoStart(page);
      await probeRules(probe, "INV-020", null, valuesFor("INV-020", ["Conveyor stops"]), tryStart("title"), startState);
      await causa.gotoStart(page);
      await probeRules(probe, "INV-023", await elements(page, "INV-023"), valuesFor("INV-023", ["Why does it stop?"]), tryStart("question"), startState);

      // INV-022: Cancel leaves the start screen; nothing is created or written.
      await causa.gotoStart(page);
      await page.fill("#start-problem", "This investigation must not be created.");
      const facts = await elements(page, "INV-022");
      const writes = store.writes.length;
      await page.locator('[data-requirement-id="INV-022"]').click();
      await page.waitForTimeout(50);
      const hash = await causa.screen(page);
      probe.outcome("INV-022", "activate", "#/", hash, facts);
      probe.outcome("INV-022", "outcome", `home; ${writes} written; not shown`, `${hash === "#/" ? "home" : hash}; ${store.writes.length} written; ${(await page.locator("#start-problem").count()) === 0 ? "not shown" : "still shown"}`);
    } finally {
      probe.flush();
    }
  });

  test("workspace rules: observation, hypothesis, evidence, relationship, rationale, conclusion", async ({ page }, testInfo) => {
    const probe = recorder(testInfo);
    probe.fact("domain-profile-selected", false);
    const store = gh.createStore();
    try {
      await gh.install(page.context(), store);
      await causa.open(page);
      await causa.connected(page);
      await causa.gotoStart(page);
      await causa.submitStart(page, { statement: "Boiler B-2 loses pressure overnight.", identifier: randomUUID() });

      const editorRules = async ({ id, open, field, fill, submit, testid, valid }) => {
        await page.getByRole("button", { name: open, exact: true }).click();
        const found = await elements(page, id);
        const state = async () => `${await causa.count(page, testid)} recorded`;
        const attempt = async (value) => {
          if ((await page.locator(field).count()) === 0) await page.getByRole("button", { name: open, exact: true }).click();
          await fill(value);
          const before = await causa.count(page, testid);
          await page.getByRole("button", { name: submit, exact: true }).click();
          await page.waitForTimeout(30);
          const accepted = (await causa.count(page, testid)) > before;
          return { accepted, observed: value };
        };
        await probeRules(probe, id, found, valuesFor(id, valid), attempt, state);
        if ((await page.locator(field).count()) > 0) await page.locator('[data-requirement-id="INV-036"]').click();
      };

      await editorRules({ id: "INV-024", open: "Record an observation", field: "#obs-text", submit: "Record observation", testid: "observation",
        fill: async (v) => { await page.fill("#obs-text", v); await page.fill("#obs-source", ""); }, valid: ["Pressure fell from 9 bar to 6 bar."] });
      await editorRules({ id: "INV-025", open: "Record an observation", field: "#obs-text", submit: "Record observation", testid: "observation",
        fill: async (v) => { await page.fill("#obs-text", "Gauge PG-4 read 6 bar at 06:00."); await page.fill("#obs-source", v); }, valid: ["Shift log, 4 November"] });
      await editorRules({ id: "INV-026", open: "Record a hypothesis", field: "#hyp-statement", submit: "Record hypothesis", testid: "hypothesis",
        fill: async (v) => { await page.fill("#hyp-statement", v); }, valid: ["The feedwater check valve leaks back."] });
      await editorRules({ id: "INV-027", open: "Record evidence", field: "#ev-label", submit: "Record evidence", testid: "evidence",
        fill: async (v) => { await page.fill("#ev-label", v); await page.fill("#ev-source", ""); await page.fill("#ev-url", ""); }, valid: ["Valve inspection photo"] });
      await editorRules({ id: "INV-028", open: "Record evidence", field: "#ev-label", submit: "Record evidence", testid: "evidence",
        fill: async (v) => { await page.fill("#ev-label", "Pressure chart"); await page.fill("#ev-source", v); await page.fill("#ev-url", ""); }, valid: ["Chart recorder CR-1"] });
      await editorRules({ id: "INV-029", open: "Record evidence", field: "#ev-label", submit: "Record evidence", testid: "evidence",
        fill: async (v) => { await page.fill("#ev-label", "Vendor bulletin"); await page.fill("#ev-source", ""); await page.fill("#ev-url", v); }, valid: ["https://example.com/bulletins/42", "http://example.org/a?b=c"] });
      await editorRules({ id: "INV-033", open: "Record a conclusion", field: "#con-statement", submit: "Record conclusion", testid: "conclusion",
        fill: async (v) => { await page.fill("#con-statement", v); }, valid: ["The check valve is the most likely cause."] });

      // Relationship kind (INV-031): the only way to send a value outside the vocabulary through the page is to
      // alter a radio's value; the engine must refuse it. Then a valid kind is accepted.
      for (const statement of ["Second hypothesis for links.", "Third hypothesis for links."]) await causa.recordHypothesis(page, { statement });
      const evidenceId = await page.locator('[data-testid="evidence"]').first().getAttribute("data-id");
      const hypotheses = await page.locator('[data-testid="hypothesis"]').evaluateAll((items) => items.map((li) => li.getAttribute("data-id")));
      let nextPair = 1;
      const linkState = async () => `${await causa.count(page, "relationship")} links`;
      await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
      const kindFacts = await elements(page, "INV-031");
      const kindAttempt = async (value) => {
        if ((await page.locator("#link-evidence").count()) === 0) await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
        await page.selectOption("#link-evidence", evidenceId);
        await page.selectOption("#link-hypothesis", hypotheses[0]);
        await page.fill("#link-rationale", "The inspection photo shows seat erosion.");
        await page.evaluate((v) => { const r = document.querySelector('input[name=relationshipKind][value="supports"], input[name=relationshipKind][data-probe]'); r.setAttribute("data-probe", "1"); r.value = v; r.checked = false; }, value);
        await page.click("input[name=relationshipKind][data-probe]");
        const before = await causa.count(page, "relationship");
        await page.getByRole("button", { name: "Record link", exact: true }).click();
        await page.waitForTimeout(30);
        return { accepted: (await causa.count(page, "relationship")) > before, observed: value };
      };
      await probeRules(probe, "INV-031", kindFacts, valuesFor("INV-031", ["supports"]), kindAttempt, linkState);

      await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
      const rationaleFacts = await elements(page, "INV-038");
      const rationaleAttempt = async (value) => {
        if ((await page.locator("#link-evidence").count()) === 0) await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
        await page.selectOption("#link-evidence", evidenceId);
        await page.selectOption("#link-hypothesis", hypotheses[nextPair]);
        await page.check('input[name=relationshipKind][value="contradicts"]');
        await page.fill("#link-rationale", value);
        const before = await causa.count(page, "relationship");
        await page.getByRole("button", { name: "Record link", exact: true }).click();
        await page.waitForTimeout(30);
        const accepted = (await causa.count(page, "relationship")) > before;
        if (accepted) nextPair += 1;
        return { accepted, observed: value };
      };
      await probeRules(probe, "INV-038", rationaleFacts, valuesFor("INV-038", ["No erosion would be visible if this hypothesis held."]), rationaleAttempt, linkState);
    } finally {
      probe.flush();
    }
  });
});
