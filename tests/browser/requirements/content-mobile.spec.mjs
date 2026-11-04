// INVESTIGATION-CONTENT-MOBILE at 320 and 390 CSS px: every screen and editor of the content workflow is usable
// on a phone. For each width: no horizontal overflow, every control at least 44 px tall with a visible label, no
// axe-core WCAG 2.1 A/AA violation, navigation reachable, and the `accessible` facts of every requirement shown on
// that screen recorded for Dokimos.
import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";
import * as gh from "../support/github.mjs";
import * as causa from "../support/causa.mjs";
import { declares, recorder } from "../support/probe.mjs";

const axeSource = readFileSync("node_modules/axe-core/axe.min.js", "utf8");

const layout = async (page) =>
  page.evaluate(() => {
    const doc = document.documentElement;
    const overflow = doc.scrollWidth - doc.clientWidth;
    const offenders = [...document.querySelectorAll("main *")]
      .filter((el) => { const r = el.getBoundingClientRect(); return r.width > 0 && r.right > doc.clientWidth + 1; })
      .slice(0, 5)
      .map((el) => `${el.tagName.toLowerCase()}#${el.id}.${el.className}`);
    const small = [...document.querySelectorAll("main button, main input:not([type=radio]), main textarea, main select, main label.causa-choice, nav a, nav button")]
      .filter((el) => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height < 44; })
      .map((el) => `${el.tagName.toLowerCase()}#${el.id} ${Math.round(el.getBoundingClientRect().height)}px "${(el.textContent ?? "").trim().slice(0, 30)}"`);
    const unlabelled = [...document.querySelectorAll("main input:not([type=radio]), main textarea, main select")]
      .filter((el) => el.getBoundingClientRect().width > 0 && !(el.labels && el.labels.length > 0) && !el.getAttribute("aria-label"))
      .map((el) => el.id);
    return { overflow, offenders, small, unlabelled };
  });

const axe = async (page) => {
  await page.evaluate(axeSource);
  return page.evaluate(async () => {
    const result = await window.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
    return result.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).slice(0, 3).join(", ")}`);
  });
};

for (const width of [320, 390]) {
  test(`INVESTIGATION-CONTENT-MOBILE at ${width} px: start, workspace and every editor`, async ({ browser }, testInfo) => {
    const probe = recorder(testInfo);
    probe.fact("domain-profile-selected", false);
    const context = await browser.newContext({ viewport: { width, height: 740 }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    const page = await context.newPage();
    const store = gh.createStore();
    const problems = [];
    const check = async (screenName, ids) => {
      const l = await layout(page);
      if (l.overflow > 0) problems.push(`${screenName}: horizontal overflow ${l.overflow}px (${l.offenders.join("; ")})`);
      for (const s of l.small) problems.push(`${screenName}: target under 44px: ${s}`);
      for (const u of l.unlabelled) problems.push(`${screenName}: unlabelled control #${u}`);
      for (const v of await axe(page)) problems.push(`${screenName}: axe ${v}`);
      for (const id of ids) if (declares(id, "accessible")) await probe.structure(page, id, ["accessible"]);
    };
    try {
      await gh.install(context, store);
      await causa.open(page);
      await check("connect", []);
      await causa.connected(page);
      await check("home", []);
      // Navigation: the primary navigation is reachable and opens the start screen.
      await causa.gotoStart(page);
      await check("start", ["INV-001", "INV-002", "INV-023", "INV-006", "INV-004", "INV-022", "INV-005"]);
      await page.fill("#start-problem", "");
      await page.getByRole("button", { name: "Start investigation" }).click();
      await check("start with a refusal", []);
      await causa.submitStart(page, { statement: "Mixer M-2 vibrates at 50% speed.", title: "M-2 vibration", question: "What excites the vibration?", identifier: randomUUID() });
      await check("workspace", ["INV-009", "INV-014", "INV-015", "INV-016", "INV-030", "INV-032", "INV-034"]);
      await page.getByRole("button", { name: "Record an observation", exact: true }).click();
      await check("observation editor", ["INV-024", "INV-025", "INV-036"]);
      await page.fill("#obs-text", "Vibration peaks at 1480 rpm.");
      await causa.submitEditor(page, "Record observation");
      await page.getByRole("button", { name: "Record a hypothesis", exact: true }).click();
      await check("hypothesis editor", ["INV-026", "INV-036"]);
      await page.fill("#hyp-statement", "The impeller is out of balance.");
      await causa.submitEditor(page, "Record hypothesis");
      await page.getByRole("button", { name: "Record evidence", exact: true }).click();
      await check("evidence editor", ["INV-027", "INV-028", "INV-029", "INV-036"]);
      await page.fill("#ev-label", "Vibration spectrum");
      await page.fill("#ev-url", "https://example.com/spectra/m2-very-long-path-segment-that-must-wrap-on-a-narrow-screen/2026-11-04.png");
      await causa.submitEditor(page, "Record evidence");
      await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
      await check("link editor", ["INV-031", "INV-038", "INV-036"]);
      await page.selectOption("#link-evidence", { index: 1 });
      await page.selectOption("#link-hypothesis", { index: 1 });
      await page.check('input[name=relationshipKind][value="supports"]');
      await page.fill("#link-rationale", "A 1x running-speed peak is the signature of imbalance.");
      await causa.submitEditor(page, "Record link");
      await page.getByRole("button", { name: "Record a conclusion", exact: true }).click();
      await check("conclusion editor", ["INV-033", "INV-036"]);
      await page.fill("#con-statement", "Imbalance is the leading explanation.");
      await causa.submitEditor(page, "Record conclusion");
      await page.getByRole("button", { name: "Edit investigation details", exact: true }).click();
      await check("details editor", ["INV-036"]);
      await page.locator('[data-requirement-id="INV-036"]').click();
      await causa.save(page);
      await check("workspace with content, saved", []);
      // Navigation back to the list works from the phone layout.
      await page.locator(".causa-nav").getByRole("link", { name: "Investigations" }).click();
      await expect(page.getByRole("heading", { name: "Investigations" })).toBeVisible();
      await check("home with an investigation", []);
      expect(problems).toEqual([]);
    } finally {
      probe.flush();
      await context.close();
    }
  });
}
