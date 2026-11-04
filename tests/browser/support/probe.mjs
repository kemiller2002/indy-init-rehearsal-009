// Requirement probes: record facts for dokimos.requirement-observations/1, never verdicts.
//
// Written for Rehearsal 009 from the released Dokimos 0.11.0 contract (RequirementEvidence.fs and
// RequirementJudging.fs at tag dokimos-v0.11.0); nothing here is copied from an earlier rehearsal (OD-INDY-22).
// A probe records what the page shows: the elements carrying an ID, the values tried and what the app then held,
// and expected/observed pairs. Dokimos alone decides PASS or FAIL against the manifest's rules. A test that
// records must still pass for its evidence to count, so tests assert only that the page was driven, not that a
// requirement holds.
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { execSync } from "node:child_process";
import { join } from "node:path";

const manifestPath = process.env.CAUSA_MANIFEST ?? "requirements/verification/verification-manifest.json";
const outDir = process.env.CAUSA_OBSERVATIONS ?? "test-evidence/requirement-observations";

const manifest = (() => {
  try {
    return JSON.parse(readFileSync(manifestPath, "utf8"));
  } catch {
    return null;
  }
})();

const sourceCommit = process.env.CAUSA_CANDIDATE ?? (() => {
  try {
    return execSync("git rev-parse HEAD", { encoding: "utf8" }).trim();
  } catch {
    return "";
  }
})();

const runId = process.env.CAUSA_RUN_ID ?? `local-${sourceCommit.slice(0, 12)}`;

/** The manifest's rules for a requirement (probe values come from the manifest, never from the app). */
export const requirement = (id) => manifest?.requirements.find((r) => r.id === id) ?? null;

const implicitRole = (tag, type) => {
  if (tag === "textarea") return "textbox";
  if (tag === "select") return "combobox";
  if (tag === "button") return "button";
  if (tag === "fieldset") return "group";
  if (tag === "a") return "link";
  if (tag === "input") return { radio: "radio", checkbox: "checkbox", button: "button", submit: "button" }[type] ?? "textbox";
  return "";
};

/** Facts about every element carrying data-requirement-id="id", computed in the page. */
export const elements = async (page, id) => {
  const locator = page.locator(`[data-requirement-id="${id}"]`);
  const count = await locator.count();
  const facts = [];
  for (let i = 0; i < count; i += 1) {
    const element = locator.nth(i);
    const raw = await element.evaluate((node) => {
      const text = (n) => (n?.textContent ?? "").replace(/\s+/g, " ").trim();
      const byIds = (ids) => ids.split(/\s+/).map((x) => text(document.getElementById(x))).join(" ").trim();
      const tag = node.tagName.toLowerCase();
      let name = node.getAttribute("aria-label") ?? "";
      if (!name && node.getAttribute("aria-labelledby")) name = byIds(node.getAttribute("aria-labelledby"));
      if (!name && node.labels && node.labels.length > 0) name = [...node.labels].map(text).join(" ");
      if (!name && tag === "fieldset") name = text(node.querySelector("legend"));
      if (!name && (tag === "button" || tag === "a")) name = text(node);
      return {
        tag,
        type: (node.getAttribute("type") ?? "").toLowerCase(),
        role: node.getAttribute("role") ?? "",
        accessibleName: name,
        disabled: Boolean(node.disabled) || node.getAttribute("aria-disabled") === "true",
        readOnly: Boolean(node.readOnly),
        instance: node.getAttribute("data-requirement-instance"),
        options:
          tag === "select" ? [...node.options].map((o) => o.value).filter((v) => v !== "")
          : tag === "fieldset" ? [...node.querySelectorAll("input[type=radio]")].map((r) => r.value)
          : [],
      };
    });
    const visible = await element.isVisible();
    const fact = { ...raw, role: raw.role || implicitRole(raw.tag, raw.type), visible };
    if (fact.instance === null) delete fact.instance;
    facts.push(fact);
  }
  return facts;
};

/** A recorder for one test: probes accumulate and are written once, in finally, by flush(). */
export const recorder = (testInfo) => {
  const [file, ...titles] = testInfo.titlePath;
  const test = `playwright:${file} > ${titles.join(" > ")} [${testInfo.project.name}]`;
  const probes = [];
  const facts = {};
  return {
    test,
    /** Record element facts for each structural check of a requirement. */
    async structure(page, id, checks) {
      const found = await elements(page, id);
      for (const check of checks) probes.push({ obligation: obligationId(id, check), test, elements: found });
      return found;
    },
    /** Element facts plus attempts (enter, select, validation, rejects). */
    attempts(id, check, found, attempts, expected, observed) {
      const probe = { obligation: obligationId(id, check), test, attempts };
      if (found) probe.elements = found;
      if (expected !== undefined) probe.expected = expected;
      if (observed !== undefined) probe.observed = observed;
      probes.push(probe);
    },
    /** An expected/observed pair (activate also carries element facts). */
    outcome(id, check, expected, observed, found) {
      const probe = { obligation: obligationId(id, check), test, expected: String(expected), observed: String(observed) };
      if (found) probe.elements = found;
      probes.push(probe);
    },
    fact(name, value) {
      facts[name] = value;
    },
    flush() {
      if (!manifest) return;
      mkdirSync(outDir, { recursive: true });
      const slug = test.replace(/[^A-Za-z0-9]+/g, "-").slice(0, 150);
      writeFileSync(
        join(outDir, `${slug}.json`),
        JSON.stringify({ schema: "dokimos.requirement-observations/1", runId, sourceCommit, manifestHash: manifest.contentHash, facts, probes }, null, 2),
      );
    },
  };
};

const levels = { mapped: "V1", exists: "V1", visible: "V2", accessible: "V2", editable: "V3", enter: "V3", select: "V3", activate: "V3", validation: "V4", rejects: "V4", outcome: "V4", save: "V5", reload: "V5", update: "V5", isolation: "V5" };
export const obligationId = (id, check) => `${id}/${levels[check]}/${check}`;

/** The obligations the manifest declares for a requirement (so a probe never invents one). */
export const declares = (id, check) => Boolean(requirement(id)?.obligations.some((o) => o.check === check));
