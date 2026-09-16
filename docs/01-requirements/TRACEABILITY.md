# Requirements Traceability

## 1. Authoritative ledger

The individual traceability ledger is [`TRACEABILITY.csv`](TRACEABILITY.csv). It contains one row for every normative requirement in:

- `REQUIREMENTS.md`; and
- `SECURITY_REQUIREMENTS.md`.

The CSV is the machine-checkable index; the source requirement documents retain the complete normative wording. `ACCEPTANCE_CRITERIA.md` supplies observable cross-requirement scenarios, while `TEST_STRATEGY.md` defines environments and evidence quality.

## 2. Columns

| Column | Meaning |
|---|---|
| `requirement_id` | Stable unique normative ID |
| `source` | Authoritative specification file |
| `delivery_goals` | Roadmap goals expected to implement or prove the requirement |
| `acceptance_family` | Closest observable acceptance scenario family |
| `planned_test_id` | Reserved unique verification identifier; the implemented test catalogue may split it into suffixed cases |
| `status` | Current evidence status |
| `implementation_evidence` | Code/configuration/schema artifact; empty until real |
| `verification_evidence` | Test/result/manual record; empty until executed |
| `notes` | Exception, ADR, limitation, or follow-up |

## 3. Status vocabulary

- `Specified`: approved/draft requirement exists, but implementation is not claimed.
- `Implemented`: authoritative implementation evidence exists, but sufficient verification is incomplete.
- `Verified`: implementation and proportionate verification evidence both pass for the identified release.
- `Blocked`: required proof cannot currently be obtained and the exact blocker is recorded.
- `Deferred`: the requirement was explicitly removed from the current release by approved scope change; mandatory v1 requirements cannot be silently deferred.

Every row initially has status `Specified`. Empty evidence is intentional and must not be interpreted as failure or success. A row becomes `Verified` only after its evidence path, release/commit, environment, result and integrity metadata are recorded.

## 4. Verification-family routing

| Requirement prefix | Primary acceptance family | Principal proof boundary |
|---|---|---|
| `TEN` | `AC-TEN` | API authorization, SQL Server RLS, private storage |
| `IAM` | `AC-IAM` | ASP.NET authentication/session and browser E2E |
| `DEV` | `AC-DEV` | Enrollment, real mTLS path, agent and physical Pi |
| `LIC` | `AC-LIC` | Backend/agent protocol, signed vectors and trusted time |
| `CNT` | `AC-CNT` | Upload pipeline, object storage and publication |
| `PLY` | `AC-SYN` / `AC-PLY` | Agent cache, local API, Chromium and physical display |
| `API` | `AC-TEN` / `AC-OPS` | OpenAPI, middleware, authorization, audit and observability |
| `SEC-*` | Matching security acceptance family | Real trust boundary plus ASVS control evidence |
| `NFR` | `AC-OPS` / `AC-VER` | Load, accessibility, resilience, recovery and hardware |
| `VER` | `AC-VER` | Quality-gate evidence bundle |
| `DOC` | `AC-DOC` | Cross-artifact and final-release audit |

## 5. Update discipline

1. A requirement change updates its source wording, acceptance coverage, threat references and traceability row in the same change.
2. Requirement IDs are never reused. Removed requirements remain recorded with an approved reason.
3. Evidence links point to immutable or release-scoped artifacts, not a developer's untracked local result.
4. A grouped test may satisfy multiple rows only when its assertions explicitly exercise every mapped requirement.
5. UI screenshots support usability/report explanation but do not prove authorization, tenant isolation, cryptography or recovery.
6. The final audit checks that every mandatory row is `Verified`; absence from the ledger is itself a failure.
