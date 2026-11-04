// Drives Causa through its real page only: fill, click, read what is rendered. No engine internals.
import { expect } from "@playwright/test";
import { destination, token } from "./github.mjs";

export const open = async (page, hash = "#/") => {
  await page.goto(`/index.html${hash}`);
  await page.waitForFunction(() => document.documentElement.dataset.causaReady === "running", null, { timeout: 45_000 });
};

export const connect = async (page, credential = token) => {
  await page.fill("#connect-owner", destination.owner);
  await page.fill("#connect-repository", destination.repository);
  await page.fill("#connect-branch", destination.branch);
  await page.fill("#connect-token", credential);
  await page.getByRole("button", { name: "Connect", exact: true }).click();
};

export const connected = async (page) => {
  await connect(page);
  await expect(page.locator(".causa-connection")).toContainText("Connected", { timeout: 20_000 });
};

export const screen = (page) => page.evaluate(() => location.hash);

export const gotoStart = async (page) => {
  await page.locator(".causa-nav").getByRole("button", { name: "New investigation" }).click();
  await expect(page.locator("#start-problem")).toBeVisible();
};

/** Fill the start form (only the fields given) and submit. Returns whether the workspace opened. */
export const submitStart = async (page, fields) => {
  if (fields.title !== undefined) await page.fill("#start-title", fields.title);
  if (fields.statement !== undefined) await page.fill("#start-problem", fields.statement);
  if (fields.question !== undefined) await page.fill("#start-question", fields.question);
  if (fields.identifier !== undefined) await page.fill("#start-identifier", fields.identifier);
  await page.getByRole("button", { name: "Start investigation" }).click();
  await page.waitForTimeout(50);
  return (await screen(page)).startsWith("#/investigation/");
};

export const investigationId = async (page) => (await screen(page)).replace("#/investigation/", "");

export const count = (page, testid) => page.locator(`[data-testid="${testid}"]`).count();
export const texts = (page, testid) => page.locator(`[data-testid="${testid}"]`).allTextContents();

const editorOpen = async (page, button) => {
  await page.getByRole("button", { name: button, exact: true }).click();
};

/** Submit the open editor and report whether the item was recorded (the editor closed without an error). */
export const submitEditor = async (page, submitName) => {
  await page.getByRole("button", { name: submitName, exact: true }).click();
  await page.waitForTimeout(30);
  return (await page.locator("form.causa-editor").count()) === 0;
};

export const recordObservation = async (page, { text, source = "" }) => {
  await editorOpen(page, "Record an observation");
  await page.fill("#obs-text", text);
  await page.fill("#obs-source", source);
  return submitEditor(page, "Record observation");
};

export const recordHypothesis = async (page, { statement, explanation = "" }) => {
  await editorOpen(page, "Record a hypothesis");
  await page.fill("#hyp-statement", statement);
  await page.fill("#hyp-explanation", explanation);
  return submitEditor(page, "Record hypothesis");
};

export const recordEvidence = async (page, { label, content = "", source = "", url = "" }) => {
  await editorOpen(page, "Record evidence");
  await page.fill("#ev-label", label);
  await page.fill("#ev-content", content);
  await page.fill("#ev-source", source);
  await page.fill("#ev-url", url);
  return submitEditor(page, "Record evidence");
};

export const linkEvidence = async (page, { evidence, hypothesis, kind, rationale }) => {
  await editorOpen(page, "Link evidence to a hypothesis");
  await page.selectOption("#link-evidence", { label: evidence });
  await page.selectOption("#link-hypothesis", { label: hypothesis });
  if (kind) await page.check(`input[name=relationshipKind][value="${kind}"]`);
  await page.fill("#link-rationale", rationale);
  return submitEditor(page, "Record link");
};

export const recordConclusion = async (page, statement) => {
  await editorOpen(page, "Record a conclusion");
  await page.fill("#con-statement", statement);
  return submitEditor(page, "Record conclusion");
};

export const save = async (page) => {
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.locator('[data-requirement-id="INV-016"]')).not.toHaveText("Saving…", { timeout: 30_000 });
  return page.locator('[data-requirement-id="INV-016"]').textContent();
};

/** Open an investigation in a fresh page of the given context: connect, then wait for it to load. */
export const reopen = async (page, id) => {
  await open(page, `#/investigation/${id}`);
  await connect(page);
  await expect(page.locator("#ws-heading")).toBeVisible({ timeout: 30_000 });
};

/** Everything the workspace shows, read from the page. */
export const shown = async (page) => ({
  title: await page.locator("#ws-heading").textContent(),
  statement: await page.locator('[data-testid="current-statement"]').textContent(),
  original: await page.locator('[data-requirement-id="INV-008"]').textContent(),
  question: await page.locator('[data-testid="question"]').textContent(),
  observations: await texts(page, "observation-text"),
  observationSources: await texts(page, "observation-source"),
  hypotheses: await texts(page, "hypothesis-statement"),
  explanations: await texts(page, "hypothesis-explanation"),
  evidence: await texts(page, "evidence-label"),
  evidenceContent: await texts(page, "evidence-content"),
  evidenceSources: await texts(page, "evidence-source"),
  evidenceUrls: await texts(page, "evidence-url"),
  links: await page.locator('[data-testid="relationship"]').evaluateAll((items) =>
    items.map((li) => ({
      evidence: li.querySelector('[data-testid="relationship-evidence"]').textContent,
      kind: li.getAttribute("data-kind"),
      hypothesis: li.querySelector('[data-testid="relationship-hypothesis"]').textContent,
      rationale: li.querySelector('[data-testid="relationship-rationale"]').textContent,
    }))),
  evidenceLinks: await texts(page, "evidence-links"),
  hypothesisLinks: await texts(page, "hypothesis-links"),
  conclusions: await texts(page, "conclusion-statement"),
  history: await texts(page, "history-text"),
});

/** Replay a stored causa.investigation/1 document independently of the application (test-side fold). */
export const replay = (doc) => {
  if (!doc) return null;
  const state = { details: {}, original: "", observations: [], hypotheses: [], evidence: [], links: [], conclusions: [], history: doc.events.length };
  const upsert = (list, item) => {
    const i = list.findIndex((x) => x.id === item.id);
    if (i >= 0) list[i] = item;
    else list.push(item);
  };
  for (const e of doc.events) {
    switch (e.type) {
      case "started": state.id = e.id; state.original = e.problemStatement; state.details = { title: e.title, statement: e.problemStatement, question: e.question }; break;
      case "details-edited": state.details = { title: e.title, statement: e.problemStatement, question: e.question }; break;
      case "observation-recorded": case "observation-edited": upsert(state.observations, { id: e.id, text: e.text, source: e.source }); break;
      case "hypothesis-recorded": case "hypothesis-edited": upsert(state.hypotheses, { id: e.id, statement: e.statement, explanation: e.explanation }); break;
      case "evidence-admitted": case "evidence-edited": upsert(state.evidence, { id: e.id, label: e.label, content: e.content, source: e.source, url: e.url }); break;
      case "relationship-recorded": state.links.push({ id: e.id, evidenceId: e.evidenceId, hypothesisId: e.hypothesisId, kind: e.kind, rationale: e.rationale }); break;
      case "relationship-reassessed": state.links = state.links.map((l) => (l.id === e.id ? { ...l, kind: e.kind, rationale: e.rationale } : l)); break;
      case "relationship-withdrawn": state.links = state.links.filter((l) => l.id !== e.id); break;
      case "conclusion-recorded": case "conclusion-revised": upsert(state.conclusions, { id: e.id, statement: e.statement }); break;
      default: break;
    }
  }
  return state;
};
