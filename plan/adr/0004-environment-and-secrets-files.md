# ADR-0004: Environment settings and secrets in separate files

- **Status:** Accepted
- **Date:** 2026-10-23
- **Decided by:** Hamid Maleki, project owner and Member A (Instructions rule 24, "Owner decision 2026-10-23"; request recorded in plan row #315)
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 session
- **Plan rows:** #315 (implementation, Done), #316, #317; written under #2 (SCRUM-14)
- **Sources:** Instructions rules 10 and 24; `plan/rows/0315.md`; [env/README.md](../../env/README.md)

## Context

Before this decision, settings were spread over `appsettings.json`, `.Development`/`.UAT` variants and literals in code (Fido2, Ollama URL,
password policy, cookie, log retention, culture, the built-in admin account and password, a design-time connection string, docker-compose
defaults). Secrets and non-secret values were mixed, and an environment could not be reproduced from a list of files.

## Decision

- Every setting lives in `env/<name>.env` (committed) for every environment: `local`, `docker`, `demo`, `uat`, `production-<customer>` (and `staging`, added later).
- Every secret lives only in `env/<name>.secrets.env` (git-ignored, never packaged) or in an environment variable; each environment has a committed `*.secrets.env.example` template.
- `PEMS_ENV` selects the environment. Precedence, lowest to highest: env file, secrets file, real environment variables, command line.
- No environment-dependent value (URL, host, credential, port, lifetime, culture, policy flag) is hard-coded. A new setting is added to every env file and read with `Required*`; required settings fail fast. `appsettings.json` stays empty.
- The first administrator comes from `Seed:Admin:*`. Customer differences are the `production-<customer>.env` file and the licence (rule 17).
- Tests keep the key sets of all environments identical and scan committed files for secrets.
- No real secret, key or connection string in the repo or package (rule 10).

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Keep `appsettings.json` plus per-environment variants | Was the state before; mixed secrets and settings, left literals in code. |
| Secrets only as environment variables | Not excluded (variables still override files) but unwieldy on Windows/IIS hosts; the secrets file keeps one reviewable list of keys. |

The plan records the "before" state and the chosen design; it does not record a longer evaluation of other configuration systems.

## Consequences

- Good: an environment is reproducible from its files; the app starts from the files alone (verified 2026-10-23 on LocalDB); secrets are kept out of git by construction and by test.
- Cost: every new setting touches all env files; `production-<customer>.env` values must be filled by a reviewed commit.
- Follow-up: #316 (Linux VM install with `PEMS_ENV`), #317 (demo pipeline with the new secrets). Supportability Matrix row SM-30.
