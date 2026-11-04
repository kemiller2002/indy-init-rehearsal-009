# Requirement verification evidence

These files come from the CI artifact of the `Requirement verification` run 41754817534, for candidate
`33d072da798a4ec762f15cebfc3c205c9014bbbd`. They are copied unchanged.

| File | What it is |
|---|---|
| `requirement-verification.json` | Dokimos 0.11.0 verdict on the live run. 36 of 37 applicable requirements are verified; INV-003 is not applicable; INV-029 fails. Exit 0: every mandatory obligation passed. |
| `verification-manifest.json` | The manifest CI compiled from the pinned catalog. It is byte-identical to `requirements/verification/verification-manifest.json`. |
| `live/` | Observations and the Playwright report from the run against the real repository (branch `causa-data`). |
| `double/` | The negative, conclusion and mobile scenarios against the GitHub Contents double, with fault injection. |
| `fault-probe-results.json` | Dokimos fault probes at the candidate: 6 of 6 detected. |

INV-029 (Evidence source URL, non-mandatory) fails V1–V3 only because Dokimos 0.11.0's
`RequirementJudging.componentMatches` has no case for the catalog's `url-input` component. Any element fails that
check, whatever it is. Its validation, rejects, save and reload obligations pass.
