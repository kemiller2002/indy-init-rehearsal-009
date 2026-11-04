# Causa: investigation workspace (Indy Init Rehearsal 009)

Causa records a root-cause investigation as real content: a problem statement, a title and a primary question;
observations, hypotheses and evidence; links between evidence and hypotheses, each with a relationship (`supports`,
`contradicts`, `consistent-with`, `inconclusive`) and a rationale; and conclusions with their revisions. Every
change is kept in an append-only history with its provenance. Investigations are saved as
`causa/investigations/<id>.json` (`causa.investigation/1`) in a GitHub repository you connect to.

Requirements: `CAT-CAUSA-INVESTIGATION` (INV-001..038) in kemiller2002/Indy-init, pinned in
`requirements/verification/catalog-pin.json`.

## Stack

| Layer | Where |
|---|---|
| Domain (F#, pure: rules, decide, evolve, replay) | `src/Causa.Domain` |
| Persistence (F#: canonical document, GitHub Contents API requests and classification) | `src/Causa.Persistence` |
| Engine (F#: model, update, projection, Limen protocol) | `src/Causa.App` |
| .NET WebAssembly host shim (marshalling only) | `src/Causa.Host` |
| Page (Limen `data-*` bindings, Forma CSS) | `web/` |

## Build, run, test

```bash
npm ci
npm run build                      # dotnet publish of the WebAssembly engine + page -> dist/
python3 -m http.server 4321 --directory dist   # open http://127.0.0.1:4321/index.html
dotnet test Causa.slnx             # kernel and document tests
npx playwright test                # browser suites (GitHub Contents double)
CAUSA_GITHUB_TOKEN=... npx playwright test   # live: real GitHub, branch causa-data
```

## Verification

- `requirements/verification/verification-manifest.json`: compiled by Praxis 3.25.0 from the pinned catalog and
  `requirements/requirement-implementation-map.json`. CI recompiles it and requires it to be identical.
- `tests/browser/requirements/*`: probes that record `dokimos.requirement-observations/1` facts. Dokimos 0.11.0
  (`dokimos verification requirements`) decides every obligation.
- `tests/browser/scenarios/*`: the negative and conclusion scenarios.
- `.echelon/fault-probes.json`: the content round trip must detect wiring, projection, view and relationship faults.
- `.github/workflows/requirement-verification.yml`: runs all of the above against the real repository at every
  candidate.
