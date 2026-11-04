// INVESTIGATION-CONTENT-E2E (INV-037; docs/rehearsal-009-content-readiness/E2E-VERIFICATION.md in Indy-init).
// Enter real investigation content through the page, save it, reload it in new browser contexts, edit it, and
// record what GitHub holds and what each page shows. Probes record facts; Dokimos judges them against the
// manifest. INV-037 (isolation) is recorded only against the live repository: a double is not "GitHub".
import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import * as gh from "../support/github.mjs";
import * as causa from "../support/causa.mjs";
import { elements, recorder } from "../support/probe.mjs";

const stamp = new Date().toISOString();
const content = {
  title: `Pump P-101 trips (${stamp})`,
  statement: "Pump P-101 tripped three times on the night shift of 3 November; each trip stopped line 2 for about 40 minutes.",
  question: "Why does P-101 trip only on the night shift?",
  observations: [
    { text: "Trip log shows overcurrent alarms at 01:12, 02:47 and 04:05.", source: "SCADA trip log, line 2" },
    { text: "Night-shift ambient temperature in the pump room fell to 4 °C.", source: "Building management export" },
  ],
  hypotheses: [
    { statement: "Cold, viscous lubricant raises motor load past the overcurrent setting.", explanation: "Explains why trips cluster when the room is cold." },
    { statement: "The overcurrent relay setting was lowered during the last maintenance.", explanation: "Explains trips that began after maintenance." },
  ],
  evidence: [
    { label: "Lubricant datasheet", content: "Viscosity at 5 °C is 2.4x the value at 25 °C.", source: "Supplier datasheet rev C", url: "https://example.com/datasheets/lube-46.pdf" },
    { label: "Relay settings record", content: "Overcurrent pickup unchanged at 42 A since commissioning.", source: "Maintenance record MR-2291", url: "https://example.com/records/MR-2291" },
  ],
  links: [
    { evidence: 0, hypothesis: 0, kind: "supports", rationale: "Higher viscosity at night temperatures would raise motor current." },
    { evidence: 1, hypothesis: 1, kind: "contradicts", rationale: "The relay setting was not changed, so it cannot explain new trips." },
    { evidence: 0, hypothesis: 1, kind: "consistent-with", rationale: "Viscosity data fits a lower setting too, but does not favour it." },
    { evidence: 1, hypothesis: 0, kind: "inconclusive", rationale: "The relay record neither confirms nor rules out a load increase." },
  ],
  conclusion: "Working conclusion: cold lubricant is the most credible cause; the relay setting is ruled out.",
};
const edits = {
  title: `Pump P-101 night-shift trips (${stamp})`,
  question: "What raises P-101's motor current on cold nights?",
  observation: "Trip log shows overcurrent alarms at 01:12, 02:47 and 04:05 (three trips).",
  hypothesis: "Cold, viscous lubricant raises motor load above the 42 A overcurrent pickup.",
  evidence: "Lubricant datasheet (rev C)",
  reassess: { kind: "supports", rationale: "Reassessed: the relay record alone cannot settle the load question." },
  conclusion: "Revised conclusion: cold lubricant is the cause; schedule a lubricant change and re-test.",
};

const short = (s) => (s.length > 80 ? `${s.slice(0, 77)}…` : s);

test("INVESTIGATION-CONTENT-E2E: enter, save, reload in a new browser context, edit and reload again", async ({ browser, page, request }, testInfo) => {
  const probe = recorder(testInfo);
  probe.fact("domain-profile-selected", false);
  const store = gh.createStore();
  try {
    await gh.install(page.context(), store);
    await causa.open(page);
    await causa.connected(page);

    // ---- start screen
    await causa.gotoStart(page);
    for (const id of ["INV-001", "INV-002", "INV-023", "INV-006"]) await probe.structure(page, id, ["exists", "visible", "accessible", "editable"]);
    for (const id of ["INV-004", "INV-022"]) await probe.structure(page, id, ["exists", "visible", "accessible"]);
    await probe.structure(page, "INV-005", ["exists", "visible", "accessible"]);
    const identifier = randomUUID();
    const enter = async (id, selector, value) => {
      const found = await elements(page, id);
      await page.fill(selector, value);
      probe.attempts(id, "enter", found, [{ value, observed: await page.inputValue(selector), accepted: true }]);
    };
    await enter("INV-002", "#start-title", content.title);
    await enter("INV-001", "#start-problem", content.statement);
    await enter("INV-023", "#start-question", content.question);
    await enter("INV-006", "#start-identifier", identifier);
    const startFacts = await elements(page, "INV-004");
    const opened = await causa.submitStart(page, {});
    probe.outcome("INV-004", "activate", `#/investigation/${identifier}`, await causa.screen(page), startFacts);
    probe.outcome("INV-004", "outcome", content.statement, opened ? await page.locator('[data-testid="current-statement"]').textContent() : "(workspace not shown)");
    const id = await causa.investigationId(page);

    // ---- workspace header
    await probe.structure(page, "INV-007", ["exists", "visible"]);
    await probe.structure(page, "INV-008", ["exists", "visible"]);
    probe.outcome("INV-008", "outcome", content.statement, await page.locator('[data-requirement-id="INV-008"]').textContent());
    await probe.structure(page, "INV-018", ["exists", "visible"]);
    probe.outcome("INV-018", "outcome", "Open", await page.locator('[data-requirement-id="INV-018"]').textContent());
    for (const id2 of ["INV-009", "INV-014", "INV-015", "INV-030", "INV-032", "INV-034"]) await probe.structure(page, id2, ["exists", "visible", "accessible"]);
    await probe.structure(page, "INV-016", ["exists", "visible", "accessible"]);
    await probe.structure(page, "INV-010", ["exists", "visible"]);
    await probe.structure(page, "INV-013", ["exists", "visible"]);

    // ---- observations
    for (const [i, o] of content.observations.entries()) {
      const buttonFacts = await elements(page, "INV-014");
      await page.getByRole("button", { name: "Record an observation", exact: true }).click();
      if (i === 0) {
        probe.outcome("INV-014", "activate", "observation editor open", (await page.locator("#obs-text").isVisible()) ? "observation editor open" : "no editor", buttonFacts);
        await probe.structure(page, "INV-024", ["exists", "visible", "accessible", "editable"]);
        await probe.structure(page, "INV-025", ["exists", "visible", "accessible", "editable"]);
        await probe.structure(page, "INV-036", ["exists", "visible", "accessible"]);
      }
      await enter("INV-024", "#obs-text", o.text);
      await enter("INV-025", "#obs-source", o.source);
      await causa.submitEditor(page, "Record observation");
    }
    probe.outcome("INV-014", "outcome", JSON.stringify(content.observations.map((o) => o.text)), JSON.stringify(await causa.texts(page, "observation-text")));

    // ---- cancel a content editor (INV-036): nothing entered is recorded
    {
      const before = await causa.count(page, "observation");
      await page.getByRole("button", { name: "Record an observation", exact: true }).click();
      await page.fill("#obs-text", "This text must not be recorded.");
      const facts = await elements(page, "INV-036");
      await page.locator('[data-requirement-id="INV-036"]').click();
      const after = await causa.count(page, "observation");
      const editorGone = (await page.locator("#obs-text").count()) === 0;
      probe.outcome("INV-036", "activate", "editor closed; 0 recorded", `${editorGone ? "editor closed" : "editor open"}; ${after - before} recorded`, facts);
      probe.outcome("INV-036", "outcome", JSON.stringify(content.observations.map((o) => o.text)), JSON.stringify(await causa.texts(page, "observation-text")));
    }

    // ---- hypotheses
    for (const [i, h] of content.hypotheses.entries()) {
      const buttonFacts = await elements(page, "INV-009");
      await page.getByRole("button", { name: "Record a hypothesis", exact: true }).click();
      if (i === 0) {
        probe.outcome("INV-009", "activate", "hypothesis editor open", (await page.locator("#hyp-statement").isVisible()) ? "hypothesis editor open" : "no editor", buttonFacts);
        await probe.structure(page, "INV-026", ["exists", "visible", "accessible", "editable"]);
        await probe.structure(page, "INV-011", ["exists", "visible", "editable"]);
      }
      await enter("INV-026", "#hyp-statement", h.statement);
      await enter("INV-011", "#hyp-explanation", h.explanation);
      await causa.submitEditor(page, "Record hypothesis");
    }
    probe.outcome("INV-009", "outcome", JSON.stringify(content.hypotheses.map((h) => h.statement)), JSON.stringify(await causa.texts(page, "hypothesis-statement")));
    probe.outcome("INV-010", "outcome", JSON.stringify(content.hypotheses.map((h) => h.statement)), JSON.stringify(await causa.texts(page, "hypothesis-statement")));

    // ---- evidence
    for (const [i, e] of content.evidence.entries()) {
      await page.getByRole("button", { name: "Record evidence", exact: true }).click();
      if (i === 0) {
        for (const fid of ["INV-027", "INV-028", "INV-029"]) await probe.structure(page, fid, ["exists", "visible", "accessible", "editable"]);
        await probe.structure(page, "INV-012", ["exists", "visible", "editable"]);
      }
      await enter("INV-027", "#ev-label", e.label);
      await enter("INV-012", "#ev-content", e.content);
      await enter("INV-028", "#ev-source", e.source);
      await enter("INV-029", "#ev-url", e.url);
      await causa.submitEditor(page, "Record evidence");
    }

    // ---- links (INV-030, INV-031, INV-038)
    for (const [i, l] of content.links.entries()) {
      const buttonFacts = await elements(page, "INV-030");
      await page.getByRole("button", { name: "Link evidence to a hypothesis", exact: true }).click();
      if (i === 0) {
        probe.outcome("INV-030", "activate", "link editor open", (await page.locator("#link-evidence").isVisible()) ? "link editor open" : "no editor", buttonFacts);
        await probe.structure(page, "INV-031", ["exists", "visible", "accessible", "editable"]);
        await probe.structure(page, "INV-038", ["exists", "visible", "accessible", "editable"]);
        // select: every option of the manifest is chosen once and read back.
        const found = await elements(page, "INV-031");
        const attempts = [];
        for (const option of ["supports", "contradicts", "consistent-with", "inconclusive"]) {
          await page.check(`input[name=relationshipKind][value="${option}"]`);
          attempts.push({ value: option, observed: await page.locator("input[name=relationshipKind]:checked").getAttribute("value"), accepted: true });
        }
        probe.attempts("INV-031", "select", found, attempts);
      }
      await page.selectOption("#link-evidence", { label: content.evidence[l.evidence].label });
      await page.selectOption("#link-hypothesis", { label: short(content.hypotheses[l.hypothesis].statement) });
      await page.check(`input[name=relationshipKind][value="${l.kind}"]`);
      await enter("INV-038", "#link-rationale", l.rationale);
      await causa.submitEditor(page, "Record link");
    }
    const linkView = (await causa.shown(page)).links.map((l) => `${l.evidence}|${l.kind}|${l.hypothesis}|${l.rationale}`);
    const linkExpected = content.links.map((l) => `${content.evidence[l.evidence].label}|${l.kind}|${content.hypotheses[l.hypothesis].statement}|${l.rationale}`);
    probe.outcome("INV-030", "outcome", JSON.stringify(linkExpected), JSON.stringify(linkView));
    // shown on both the evidence and the hypothesis
    const sides = await causa.shown(page);
    expect(sides.evidenceLinks.length).toBeGreaterThan(0);
    expect(sides.hypothesisLinks.length).toBeGreaterThan(0);

    // ---- conclusion
    {
      const buttonFacts = await elements(page, "INV-032");
      await page.getByRole("button", { name: "Record a conclusion", exact: true }).click();
      probe.outcome("INV-032", "activate", "conclusion editor open", (await page.locator("#con-statement").isVisible()) ? "conclusion editor open" : "no editor", buttonFacts);
      await probe.structure(page, "INV-033", ["exists", "visible", "accessible", "editable"]);
      await enter("INV-033", "#con-statement", content.conclusion);
      await causa.submitEditor(page, "Record conclusion");
      probe.outcome("INV-032", "outcome", JSON.stringify([content.conclusion, "Open"]), JSON.stringify([...(await causa.texts(page, "conclusion-statement")), await page.locator('[data-requirement-id="INV-018"]').textContent()]));
    }

    // ---- save (INV-015) and what GitHub holds (save obligations)
    const saveFacts = await elements(page, "INV-015");
    const saveState = await causa.save(page);
    probe.outcome("INV-015", "activate", "Saved to GitHub", saveState, saveFacts);
    const held = causa.replay(await gh.storedDocument(store, request, id));
    probe.outcome("INV-015", "outcome", "Saved to GitHub", held ? "Saved to GitHub" : "nothing saved");
    const h = held ?? { details: {}, observations: [], hypotheses: [], evidence: [], links: [], conclusions: [] };
    probe.outcome("INV-001", "save", content.statement, h.details.statement ?? "");
    probe.outcome("INV-002", "save", content.title, h.details.title ?? "");
    probe.outcome("INV-023", "save", content.question, h.details.question ?? "");
    probe.outcome("INV-024", "save", JSON.stringify(content.observations.map((o) => o.text)), JSON.stringify(h.observations.map((o) => o.text)));
    probe.outcome("INV-025", "save", JSON.stringify(content.observations.map((o) => o.source)), JSON.stringify(h.observations.map((o) => o.source)));
    probe.outcome("INV-026", "save", JSON.stringify(content.hypotheses.map((x) => x.statement)), JSON.stringify(h.hypotheses.map((x) => x.statement)));
    probe.outcome("INV-011", "save", JSON.stringify(content.hypotheses.map((x) => x.explanation)), JSON.stringify(h.hypotheses.map((x) => x.explanation)));
    probe.outcome("INV-027", "save", JSON.stringify(content.evidence.map((x) => x.label)), JSON.stringify(h.evidence.map((x) => x.label)));
    probe.outcome("INV-012", "save", JSON.stringify(content.evidence.map((x) => x.content)), JSON.stringify(h.evidence.map((x) => x.content)));
    probe.outcome("INV-028", "save", JSON.stringify(content.evidence.map((x) => x.source)), JSON.stringify(h.evidence.map((x) => x.source)));
    probe.outcome("INV-029", "save", JSON.stringify(content.evidence.map((x) => x.url)), JSON.stringify(h.evidence.map((x) => x.url)));
    const heldLinks = h.links.map((l) => {
      const ev = h.evidence.find((e) => e.id === l.evidenceId)?.label;
      const hy = h.hypotheses.find((x) => x.id === l.hypothesisId)?.statement;
      return `${ev}|${l.kind}|${hy}|${l.rationale}`;
    });
    probe.outcome("INV-030", "save", JSON.stringify(linkExpected), JSON.stringify(heldLinks));
    probe.outcome("INV-031", "save", JSON.stringify(content.links.map((l) => l.kind)), JSON.stringify(h.links.map((l) => l.kind)));
    probe.outcome("INV-038", "save", JSON.stringify(content.links.map((l) => l.rationale)), JSON.stringify(h.links.map((l) => l.rationale)));
    probe.outcome("INV-032", "save", JSON.stringify([content.conclusion]), JSON.stringify(h.conclusions.map((c) => c.statement)));
    probe.outcome("INV-033", "save", JSON.stringify([content.conclusion]), JSON.stringify(h.conclusions.map((c) => c.statement)));

    // ---- reload in a new browser context (nothing shared but GitHub)
    const second = await browser.newContext();
    await gh.install(second, store);
    const page2 = await second.newPage();
    await causa.reopen(page2, id);
    const after = await causa.shown(page2);
    probe.outcome("INV-008", "reload", content.statement, after.original);
    probe.outcome("INV-017", "reload", JSON.stringify(content.observations.map((o) => o.text)), JSON.stringify(after.observations));
    probe.outcome("INV-023", "reload", content.question, after.question);
    probe.outcome("INV-024", "reload", JSON.stringify(content.observations.map((o) => o.text)), JSON.stringify(after.observations));
    probe.outcome("INV-025", "reload", JSON.stringify(content.observations.map((o) => o.source)), JSON.stringify(after.observationSources));
    probe.outcome("INV-026", "reload", JSON.stringify(content.hypotheses.map((x) => x.statement)), JSON.stringify(after.hypotheses));
    probe.outcome("INV-027", "reload", JSON.stringify(content.evidence.map((x) => x.label)), JSON.stringify(after.evidence));
    probe.outcome("INV-028", "reload", JSON.stringify(content.evidence.map((x) => x.source)), JSON.stringify(after.evidenceSources));
    probe.outcome("INV-029", "reload", JSON.stringify(content.evidence.map((x) => x.url)), JSON.stringify(after.evidenceUrls));
    const reloadedLinks = after.links.map((l) => `${l.evidence}|${l.kind}|${l.hypothesis}|${l.rationale}`);
    probe.outcome("INV-030", "reload", JSON.stringify(linkExpected), JSON.stringify(reloadedLinks));
    probe.outcome("INV-031", "reload", JSON.stringify(content.links.map((l) => l.kind)), JSON.stringify(after.links.map((l) => l.kind)));
    probe.outcome("INV-038", "reload", JSON.stringify(content.links.map((l) => l.rationale)), JSON.stringify(after.links.map((l) => l.rationale)));
    probe.outcome("INV-032", "reload", JSON.stringify([content.conclusion]), JSON.stringify(after.conclusions));
    probe.outcome("INV-033", "reload", JSON.stringify([content.conclusion]), JSON.stringify(after.conclusions));
    const everything = (s) => JSON.stringify({
      title: s.title, statement: s.statement, question: s.question, observations: s.observations, observationSources: s.observationSources,
      hypotheses: s.hypotheses, explanations: s.explanations, evidence: s.evidence, evidenceContent: s.evidenceContent,
      evidenceSources: s.evidenceSources, evidenceUrls: s.evidenceUrls, links: s.links, conclusions: s.conclusions,
    });
    const entered = {
      title: content.title, statement: content.statement, question: content.question,
      observations: content.observations.map((o) => o.text), observationSources: content.observations.map((o) => o.source),
      hypotheses: content.hypotheses.map((x) => x.statement), explanations: content.hypotheses.map((x) => x.explanation),
      evidence: content.evidence.map((x) => x.label), evidenceContent: content.evidence.map((x) => x.content),
      evidenceSources: content.evidence.map((x) => x.source), evidenceUrls: content.evidence.map((x) => x.url),
      links: content.links.map((l) => ({ evidence: content.evidence[l.evidence].label, kind: l.kind, hypothesis: content.hypotheses[l.hypothesis].statement, rationale: l.rationale })),
      conclusions: [content.conclusion],
    };
    expect(everything(after)).toBe(everything(entered));
    if (gh.live) probe.outcome("INV-037", "isolation", everything(entered), everything(after));
    probe.outcome("INV-037", "save", everything(entered), held ? everything({
      title: h.details.title, statement: h.details.statement, question: h.details.question,
      observations: h.observations.map((o) => o.text), observationSources: h.observations.map((o) => o.source),
      hypotheses: h.hypotheses.map((x) => x.statement), explanations: h.hypotheses.map((x) => x.explanation),
      evidence: h.evidence.map((x) => x.label), evidenceContent: h.evidence.map((x) => x.content),
      evidenceSources: h.evidence.map((x) => x.source), evidenceUrls: h.evidence.map((x) => x.url),
      links: h.links.map((l) => ({ evidence: h.evidence.find((e) => e.id === l.evidenceId)?.label, kind: l.kind, hypothesis: h.hypotheses.find((x) => x.id === l.hypothesisId)?.statement, rationale: l.rationale })),
      conclusions: h.conclusions.map((c) => c.statement),
    }) : "nothing saved");
    if (gh.live) {
      probe.outcome("INV-037", "reload", everything(entered), everything(after));
      probe.outcome("INV-037", "outcome", everything(entered), everything(after));
    }

    // ---- edits in the second context: identity and history are kept (INV-034, INV-035)
    const obsIdBefore = await page2.locator('[data-testid="observation"]').first().getAttribute("data-id");
    {
      const facts = await elements(page2, "INV-034");
      await page2.getByRole("button", { name: "Edit investigation details", exact: true }).click();
      probe.outcome("INV-034", "activate", "details editor open", (await page2.locator("#details-title").isVisible()) ? "details editor open" : "no editor", facts);
      await page2.fill("#details-title", edits.title);
      await page2.fill("#details-question", edits.question);
      await causa.submitEditor(page2, "Record changes");
      probe.outcome("INV-034", "outcome", `${edits.title}|${edits.question}|${id}`, `${await page2.locator("#ws-heading").textContent()}|${await page2.locator('[data-testid="question"]').textContent()}|${await causa.investigationId(page2)}`);
    }
    await page2.locator('[data-testid="observation"]').first().getByRole("button", { name: "Edit observation" }).click();
    await page2.fill("#obs-text", edits.observation);
    await causa.submitEditor(page2, "Record observation");
    await page2.locator('[data-testid="hypothesis"]').first().getByRole("button", { name: "Edit hypothesis" }).click();
    await page2.fill("#hyp-statement", edits.hypothesis);
    await causa.submitEditor(page2, "Record hypothesis");
    await page2.locator('[data-testid="evidence"]').first().getByRole("button", { name: "Edit evidence" }).click();
    await page2.fill("#ev-label", edits.evidence);
    await causa.submitEditor(page2, "Record evidence");
    await page2.locator('[data-testid="relationship"]').nth(3).getByRole("button", { name: "Reassess" }).click();
    await page2.check(`input[name=relationshipKind][value="${edits.reassess.kind}"]`);
    await page2.fill("#link-rationale", edits.reassess.rationale);
    await causa.submitEditor(page2, "Record link");
    await page2.locator('[data-testid="conclusion"]').first().getByRole("button", { name: "Revise conclusion" }).click();
    await page2.fill("#con-statement", edits.conclusion);
    await causa.submitEditor(page2, "Record conclusion");
    const obsIdAfter = await page2.locator('[data-testid="observation"]').first().getAttribute("data-id");
    const historyAfterEdit = (await causa.texts(page2, "history-text")).join("\n");
    probe.outcome("INV-035", "outcome", `same id ${obsIdBefore}; earlier text kept`, `${obsIdAfter === obsIdBefore ? "same id" : "new id"} ${obsIdAfter}; ${historyAfterEdit.includes(short(content.observations[0].text)) ? "earlier text kept" : "earlier text lost"}`);
    await causa.save(page2);
    const held2 = causa.replay(await gh.storedDocument(store, request, id));
    probe.outcome("INV-034", "save", `${edits.title}|${edits.question}|${id}`, held2 ? `${held2.details.title}|${held2.details.question}|${held2.id}` : "nothing saved");
    probe.outcome("INV-035", "save", `${obsIdBefore}|${edits.observation}`, held2 ? `${held2.observations[0].id}|${held2.observations[0].text}` : "nothing saved");

    // ---- third context: the edits survive and the history keeps the earlier text
    const third = await browser.newContext();
    await gh.install(third, store);
    const page3 = await third.newPage();
    await causa.reopen(page3, id);
    const final = await causa.shown(page3);
    probe.outcome("INV-023", "update", edits.question, final.question);
    probe.outcome("INV-024", "update", edits.observation, final.observations[0]);
    probe.outcome("INV-026", "update", edits.hypothesis, final.hypotheses[0]);
    probe.outcome("INV-027", "update", edits.evidence, final.evidence[0]);
    probe.outcome("INV-030", "update", `${edits.reassess.kind}|${edits.reassess.rationale}`, `${final.links[3]?.kind}|${final.links[3]?.rationale}`);
    probe.outcome("INV-034", "reload", `${edits.title}|${edits.question}`, `${final.title}|${final.question}`);
    probe.outcome("INV-034", "update", `${edits.title}|${content.statement}`, `${final.title}|${final.original}`);
    const finalObsId = await page3.locator('[data-testid="observation"]').first().getAttribute("data-id");
    const history = final.history.join("\n");
    const kept = [content.observations[0].text, content.hypotheses[0].statement, content.evidence[0].label].every((t) => history.includes(short(t)));
    probe.outcome("INV-035", "reload", `${obsIdBefore}|earlier text in history`, `${finalObsId}|${kept ? "earlier text in history" : "earlier text missing"}`);
    probe.outcome("INV-035", "update", `${edits.observation}|${edits.hypothesis}|${edits.evidence}|${edits.title}`, `${final.observations[0]}|${final.hypotheses[0]}|${final.evidence[0]}|${final.title}`);
    // The edited round trip must hold in full; a fault anywhere in wiring, projection, view or relationships fails here.
    expect(final.conclusions).toEqual([edits.conclusion]);
    expect(final.observations).toEqual([edits.observation, content.observations[1].text]);
    expect(final.observationSources).toEqual(content.observations.map((o) => o.source));
    expect(final.hypotheses).toEqual([edits.hypothesis, content.hypotheses[1].statement]);
    expect(final.explanations).toEqual(content.hypotheses.map((x) => x.explanation));
    expect(final.evidence).toEqual([edits.evidence, content.evidence[1].label]);
    expect(final.evidenceUrls).toEqual(content.evidence.map((x) => x.url));
    expect(final.links.map((l) => `${l.evidence}|${l.kind}|${l.hypothesis}|${l.rationale}`)).toEqual([
      `${edits.evidence}|supports|${edits.hypothesis}|${content.links[0].rationale}`,
      `${content.evidence[1].label}|contradicts|${content.hypotheses[1].statement}|${content.links[1].rationale}`,
      `${edits.evidence}|consistent-with|${content.hypotheses[1].statement}|${content.links[2].rationale}`,
      `${content.evidence[1].label}|${edits.reassess.kind}|${edits.hypothesis}|${edits.reassess.rationale}`,
    ]);
    expect(`${final.title}|${final.statement}|${final.original}|${final.question}`).toBe(`${edits.title}|${content.statement}|${content.statement}|${edits.question}`);
    await second.close();
    await third.close();
  } finally {
    probe.flush();
  }
});
