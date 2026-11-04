# Indy Init Rehearsal 009: final report

**Outcome in one line:**
- The Causa content application is built, deployed and independently verified against `CAT-CAUSA-INVESTIGATION`:
  - Dokimos 0.11.0 accepted it, with 199 of 199 applicable mandatory obligations passing;
  - all 6 content fault probes were detected.
- The Rehearsal 009 release gate (`Verification gate`, `mode=release`, whole frozen v4.1.2 plan) **REFUSED** the candidate.
- No acceptance verdict is claimed. The authoritative verdict is computed by `tools/compute-acceptance-verdict.py` from owner-pinned inputs. Those inputs do not exist for Rehearsal 009, and agents never write approval records (§12, §16).

## 1. Timed start and finish

| | |
|---|---|
| Authoritative timed start (T0) | 2026-11-04T00:30:01Z, recorded before `conditor init` (`evidence/t0/T0.md`) |
| Implementation stopped / report written | 2026-11-04T02:25Z (about 115 of 420 minutes) |
| Time limit | 2026-11-04T07:30:01Z, not reached and not extended |

I stopped early on purpose:
- every mandatory content scenario was verified on the release candidate;
- the remaining release-gate refusals cannot be cleared by more application work inside the rules (§5, §16).

## 2. Commits

| | SHA |
|---|---|
| Indy-init source (governing, catalog pin) | `bc3c381df2840187a46799ea59f425ffe1b1ceed` |
| Competition repository before T0 (owner's README only) | `aa12742c01269c7c0aa8c44d0c5a24937165dd95` |
| T0 bootstrap | `8001e7c` |
| Verified application candidate (Dokimos evidence) | `33d072da798a4ec762f15cebfc3c205c9014bbbd`; `39dc037` merged it to `main` (PR #1) |
| Final release candidate (deployed) | `13e1cf84fb1da2cea7a2e90a3b430bbed6536a58` (PR #2: deploy SDK fix only) |
| Final commit | The merge of the pull request that adds this report |

## 3. Requirements implemented

37 of 38 catalog requirements are implemented:
- INV-001..002, INV-004..038.
- INV-003 (domain/profile indicator) applies only when `domain-profile-selected` is true. The application has no profile selection, so the fact is recorded as `false` and Dokimos reports NOT_APPLICABLE.

**What the application does:**
- Start an investigation: problem statement, title, primary question, editable UUID identifier, persistence-destination indicator, Cancel.
- Workspace:
  - original statement, creation time and state;
  - observations (text and source), hypotheses (statement and explanation), evidence (label, original content, source reference, URL);
  - evidence–hypothesis links with exactly `supports`, `contradicts`, `consistent-with` and `inconclusive`. There is no default, a rationale is required, there is one current assessment per pair, and reassessing or withdrawing keeps history;
  - conclusions and their revisions (snapshots that do not resolve the investigation);
  - details editing that keeps the identifier;
  - an append-only history with provenance;
  - Cancel on every editor.
- Save to GitHub (`causa/investigations/<id>.json` on branch `causa-data`) and reload from it in a new browser context.
- Validation by the catalog's own rules.

**How it handles failures:**
- Save failure: Retry is offered.
- Authentication failure: the app returns to the Connect form.
- Conflict: a save over a changed copy is refused (sha-guarded). Lost save answers are recovered.
- Malformed or inconsistent saved documents are refused on reload.
- Unsaved work is kept across in-app navigation.

**Stack:**
- F# domain, persistence and engine (functional core: pure decide, evolve, update and projection).
- .NET 10 WebAssembly host shim.
- Limen 0.9.1 kernel; Forma 0.13.0 CSS.
- No Bolero, Kubernetes or Terraform.

## 4. Requirements independently verified

The manifest is compiled by Praxis 3.25.0 from the pinned catalog. CI recompiles it and requires it to be byte-identical.

Dokimos 0.11.0 ran on CI run 41754817534 (live GitHub) for candidate `33d072d`. See `evidence/requirement-verification/requirement-verification.json`.

| | Count |
|---|---|
| Applicable requirements | 37 |
| Verified | **36** (97.2 %) |
| Failed | 1: INV-029, non-mandatory (§13) |
| Blocked / not run | 0 / 0 |
| Obligations | 250: 242 PASS, 5 FAIL (all INV-029), 3 NOT_APPLICABLE (INV-003) |
| Mandatory obligations | 202: 199 PASS, 3 NOT_APPLICABLE, 0 FAIL |
| Dokimos acceptance | `accepted: true`, no evidence problems, no blockers (exit 0) |

The same workflow passed again on `main` at `39dc037` (run 41756873902) and at the final candidate `13e1cf8` (run 41758591951).

## 5. Mandatory obligations passed and failed

| Obligation set | Result |
|---|---|
| CAT-CAUSA-INVESTIGATION mandatory obligations (Dokimos) | 199/199 applicable PASS |
| INVESTIGATION-CONTENT-E2E incl. INV-037 live round trip | PASS (§6) |
| INVESTIGATION-CONTENT-NEGATIVE | PASS (§7) |
| INVESTIGATION-CONTENT-MOBILE | PASS (§8) |
| INVESTIGATION-CONTENT-CONCLUSION | PASS |
| Content fault sensitivity | 6/6 detected (§9) |
| Frozen v4.1.2 plan, executable | **0/541 verified** (gate run on CI results at `39dc037`; `evidence/release-gate/`) |
| Frozen v4.1.2 plan, record (`verify-process`) | **0/119 verified** |

Why the v4.1.2 plan fails:
- That plan binds about 540 executable obligations to specific test identities (for example `Causa.Acceptance.Deterministic.PERSIST001Tests.PERSIST-001-O1`). This build did not write them; it implemented and verified the content catalog.
- Independent of the application, the plan's own suite commands cannot run on the pinned SDK (§11).

## 6. Investigation content round trip

Run against the **real GitHub repository** (`kemiller2002/indy-init-rehearsal-009`, branch `causa-data`) in CI with the run's token. The test is `tests/browser/requirements/content-e2e.spec.mjs`. It enters, through the page:
- title, problem statement and question;
- 2 observations with sources;
- 2 hypotheses with explanations;
- 2 evidence items with content, source and URL;
- 4 links, one of each relationship, each with a rationale;
- 1 conclusion.

Then it:
1. saves, and reads back what GitHub holds by decoding the document test-side, independently of the app;
2. reloads in a new browser context and compares every value (INV-037 `isolation`: PASS);
3. edits details, an observation, a hypothesis and an evidence label, reassesses a link to a new kind, and revises the conclusion, then saves;
4. reloads in a third context: identity kept, earlier text in history, every edited value correct.

INV-037 mapped, outcome, save, reload and isolation are all PASS.

## 7. Negative tests

All ran against the GitHub Contents double with fault injection (`tests/browser/scenarios/content-negative.spec.mjs`, 13 tests). All passed in CI (run 41754817534 `double/`) and locally:

| Scenario | Result |
|---|---|
| Missing name: empty problem statement refused, nothing created | PASS |
| Empty observation, empty hypothesis | PASS |
| Invalid evidence URL (`javascript:`, `ftp:`, no scheme, space); never rendered as a link | PASS |
| Invalid relationship target (evidence or hypothesis that does not exist) | PASS |
| Second link for the same pair refused, pointing to the existing one | PASS |
| Duplicate submission: double Save writes once; a double-submitted record is recorded once | PASS |
| Save failure (503, network) reported; Retry saves; nothing lost | PASS |
| Authentication failure at connect and at save; reconnect saves; token never in the page | PASS |
| Conflict: stale save refused, nothing overwritten, resolve by loading the saved copy | PASS |
| Malformed reload: not JSON, wrong schema, unknown kind, link to a missing item, rule-breaking value, broken sequence | PASS |
| Lost save answer recovered without loss; uppercase link opens; unsaved work restored after navigation | PASS |

The field rules of INV-006, 019–021, 023–029, 031, 033 and 038 are also probed per requirement (`validation` and `rejects`), and Dokimos judges them all PASS.

## 8. Mobile acceptance

`tests/browser/requirements/content-mobile.spec.mjs` runs at **320 and 390 px** (touch, `isMobile`). It covers:
- connect, home and start (including a refusal);
- the workspace and every editor;
- the saved workspace and navigation back to the list.

It checks, at every step:
- no horizontal overflow;
- every control at least 44 px;
- every control labelled;
- axe-core WCAG 2.1 A/AA: no violations.

The run found and fixed two real defects: contrast on the destination note, and `aria-controls` pointing at an unmounted editor. The `accessible` facts of every requirement on each screen are recorded and verified by Dokimos. PASS at both widths, in CI and locally.

## 9. Fault probes

The spec is `.echelon/fault-probes.json`. Each probe builds a faulted copy of the site and requires the content round trip to fail.

| Probe | Fault | CI (33d072d) |
|---|---|---|
| P-CONTENT-WIRE | saved document drops observation sources | detected |
| P-CONTENT-WIRE-INPUT | evidence-label input wired to the wrong draft | detected |
| P-CONTENT-PROJECTION | observation list projects the source as text | detected |
| P-CONTENT-VIEW | hypothesis list renders the explanation as the statement | detected |
| P-CONTENT-RELATION | reload swaps a link's evidence and hypothesis | detected |
| P-CONTENT-RELATION-REASSESS | reassessment keeps the old kind | detected |

The last probe **survived** a first local run. That showed the E2E reassessed to the kind the link already had. The test was fixed, and the probe is now detected.

## 10. Build and deployment

- `dotnet build Causa.slnx` and `npm run build`: green.
- Deployed by `Deploy to GitHub Pages` at `13e1cf8` (run 41758592085: build success, deploy success) to https://kemiller2002.github.io/indy-init-rehearsal-009/.
- The first deploy at `39dc037` failed for lack of the pinned SDK; PR #2 fixed it.
- A smoke test of the deployed site:
  - HTTP 200;
  - the WebAssembly engine is `running`;
  - starting an investigation through the page shows the entered statement;
  - no errors.

## 11. CI results

| Workflow | Final state |
|---|---|
| Build and test (required) | success (`13e1cf8`) |
| Echelon foundations (required) | success |
| Praxis validation (required) | success |
| Requirement verification | success at `33d072d`, `39dc037` and `13e1cf8` (run 41758591951: 36/37 verified, fault probes detected) |
| Deploy to GitHub Pages | success at `13e1cf8` |
| **Verification gate** (development, on PRs and pushes) | **failure** on every run |
| **Verification gate** (release; runs 41756889999 at `39dc037`, 41758805409 at `13e1cf8`) | **REFUSED** |

The gate fails in its `run-suites` step. The frozen v4.1.2 execution plan's own commands cannot run:
- `SU-OWN-DOTNET` and `SU-DOTNET-LIVE` pass several projects to one `dotnet test`. SDK 10.0.402 refuses this with `MSB1008: Only one project can be specified`, on any tree.
- `SU-BROWSER-WEBKIT` names a `webkit` Playwright project that this application does not define, and none of the plan's acceptance tests exist.

Fixing the first two requires changing the frozen package or the governance-checked workflow. Both are owner decisions; neither was touched. This was explained on PR #1. The gate was never counted as passed.

## 12. Process evidence

- `r9.fsx govern` and `check-governance`: PASS at T0, and on every CI run through the gate's governance step.
- Praxis work item `R9-CONTENT-001` is **active, not completed**:
  - The governance policy (`verification-matrix-satisfied`) requires a PASS gate matrix, and the gate cannot produce one (§11).
  - Not completing it is the correct state.
  - The work item was begun after the first application commits, not before them. This is a process deviation.
- Record obligations (`verify-process`): 0/119 verified. The release gate never reaches the record verifier, because `run-suites` refuses first.
- **Independent review:**
  - A separate reviewer agent (fresh context, implemented nothing) reviewed the full change. It found nothing blocking and 7 non-blocking defects; all 7 were fixed in `33d072d`.
  - The review was not posted as a GitHub review, so no CI-observed review attestation exists. That is another reason the record obligations are unverified.
- Acceptance verdict: `tools/compute-acceptance-verdict.py` needs an owner-pinned Rehearsal 009 approvals file and evidence manifest. Neither exists, and agents may not write them. **Not computed.**

## 13. Remaining defects

1. **INV-029 (non-mandatory) fails V1–V3 in Dokimos 0.11.0.** `RequirementJudging.componentMatches` has no case for the catalog's `url-input` component, so any element fails. This is a tool or catalog mismatch to report to the Dokimos owners; the tool is unchanged. INV-029's validation, rejects, save and reload obligations pass.
2. **The frozen v4.1.2 plan's suite commands cannot execute** on the pinned SDK (MSB1008). This is an owner decision.
3. **The v4.1.2 plan's executable and record obligations are not implemented** (0/541, 0/119).
4. **`webkit` is not configured.** The plan's SU-BROWSER-WEBKIT expects a `webkit` Playwright project.
5. **Live content probes cover only the deterministic data in the test.** The live run also writes test investigations to branch `causa-data` of this repository.

## 14. Human interventions

None. No owner decision was requested or recorded.

## 15. Compute and token usage

- One primary agent, plus one independent-review subagent: 21 tool uses, about 139k tokens, about 2.2 minutes.
- About 25 CI workflow runs.
- Local fault-probe runs:
  - one run at `f1e0be7`, about 9 minutes;
  - one interrupted when the container restarted;
  - one stopped deliberately once CI had the same result.
- The primary session's token totals are not exposed to the agent, so they are left unmeasured rather than estimated. `praxis telemetry ingest` was not run.

## 16. Reasons for incomplete work

- The release gate judges the whole frozen v4.1.2 plan. Its suite commands fail on the pinned SDK before any obligation is judged, and fixing that is not an agent's decision.
- The approved scope for this run was the content catalog and the INVESTIGATION-CONTENT scenarios. Those are verified.
- Writing about 540 plan-bound tests would not change the release-gate verdict while the plan's commands cannot run. Under the compute constraint, I stopped instead of spending the remaining five hours.
