# Plan: OVS Manager - web manager for OpenVoiceSpeak servers

> **What this file is:** the plan of the web manager ("Verwaltungstool"), a web app that creates, runs, updates and watches up to 100 OVS servers on one Docker host. It is kept apart from `PLAN.md` at the user's request: `PLAN.md` stays on OpenVoiceSpeak itself, and every change the manager needs in the server or the client is a package there (Packages 190 to 194). The packages here are numbered M1 to M34 so that they never collide with `PLAN.md`. Written in English like `PLAN.md` since Package 80; the user's words stay German where they are UI text.
>
> **Order:** M1 to M5 build the app with accounts and the audit log; M6 to M10 manage containers; M11 to M19 manage a server through the protocol and update it; M20 to M25 are telemetry; M26 to M32 are mail, notifications, security extras and delivery; M33 backs up the manager itself; M34, the full documentation in `README-MANAGER.md`, comes last. The request named no order of features, so the order follows the dependencies.
>
> **From PLAN.md:** M7 needs Package 190 (server admin by variable), M11 needs 191 and 194 (service session, shared connection), M23 needs 192 (daily users), M17 needs 193 (protocol of a release).
>
> **Parallel possible:** after M1, M2 and M6 can start at once; after M5, M6 to M10 and M26 run beside each other; M20 can start after M7; M12 to M16 can run in parallel after M11; M22 to M25 in parallel after M21. The test sections are headed "Planned tests (TDD)" until a package is built (as in `PLAN.md` 172 ff.).

## Overview

| # | Package | Goal in one sentence | Depends on |
|---|---------|----------------------|------------|
| M1 | Skeleton | The manager runs as its own container on port 6999 with a SQLite database in `/data` and a health endpoint. | - |
| M2 | Languages and theme | Every page of the manager is German or English and light or dark, chosen per user. | M1 |
| M3 | First admin and login | The first start leads to exactly one admin account, and everyone logs in with user name and password. | M2 |
| M4 | Accounts | The admin creates, changes, locks and deletes server managers, and every user changes their own password, e-mail and sessions. | M3 |
| M5 | Audit log | Every change a user makes in the manager is recorded with who, when, what and which server, and the admin can read and filter it. | M4 |
| M6 | Docker access | The manager reaches Docker only through a socket proxy with an allow list and touches only containers marked as its own. | M1 |
| M7 | Create a server | The admin creates a server with name, port from 7000 to 7099 and version, and the manager is its server admin from the first start. | M5, M6 |
| M8 | Overview and start, stop, restart | The overview lists every visible server with status, version and port, and each can be started, stopped and restarted. | M7 |
| M9 | Delete a server | The admin deletes a server after typing its name, and it can be restored for 7 days before its data is gone. | M8 |
| M10 | Assign servers | The admin assigns servers to managers, and a manager sees and operates only the assigned ones. | M8 |
| M11 | Connection to the servers | The manager holds one invisible service session to each running server of its protocol and marks the others as outdated. | M8, PLAN 191, 194 |
| M12 | Server settings | Name, max users, password, auto-restart, log retention and icon of a server are changed in the manager. | M11 |
| M13 | Users, groups and moderation | Online users, known users, bans and groups of a server are managed in the manager, including giving the owner the Admin group. | M11 |
| M14 | Invite link | Each server shows its `ovs://` link with public address, port and certificate fingerprint to copy. | M11 |
| M15 | Backups | Backups are made by hand, by schedule and before every update, and can be downloaded, uploaded, restored and deleted. | M11 |
| M16 | Server log | The server log is read, searched and downloaded in the manager, and the console output is shown when the protocol is not available. | M11 |
| M17 | Versions | The overview shows each server's version, the newest release of its protocol and whether the manager itself is outdated. | M8, PLAN 193 |
| M18 | Update | A server is updated with a backup before and a health check after, rolled back on failure, singly, all at once or in a maintenance window. | M15, M17 |
| M19 | Announcements | Users on a server are told in its chat before a restart, a stop or an update, with an adjustable lead time. | M11 |
| M20 | Telemetry receiver | Every managed server sends its traces, metrics and logs to the manager over OTLP, and the manager stores them per server. | M7 |
| M21 | Telemetry storage and retention | Telemetry is condensed and deleted by age so that 100 servers fit in a bounded database. | M20 |
| M22 | Live view | A server's page shows sessions, bandwidth, voice drops, CPU and RAM live. | M21 |
| M23 | History and dashboard | Connections and users per day, bandwidth per day and month, and totals over all servers are shown as charts. | M21, PLAN 192 |
| M24 | Errors and stack traces | Errors of a server are listed with stack trace and error ID, and an error ID from a user finds its error and trace. | M21 |
| M25 | Resources | CPU, RAM and disk use of each server are shown, and the admin sets CPU and RAM limits. | M8 |
| M26 | E-mail and password reset | With SMTP set up, users with an e-mail reset their password by mail, and without it only the admin resets passwords. | M4 |
| M27 | Notifications | Users are told by e-mail or webhook when a server crashes, loops, runs full, runs out of disk or can be updated. | M8, M17, M22, M26 |
| M28 | Two-factor login | Every user can turn on TOTP with recovery codes, and the admin can require it for everyone. | M4 |
| M29 | Adopt an existing server | An OVS container the manager did not create is taken over with its data and is then managed like the others. | M7, M11 |
| M30 | Clone a server | A new server starts with the channels, groups and settings of an existing one. | M7, M15 |
| M31 | API tokens | Users create tokens for a small REST API to list, start, stop, restart and update their servers. | M8, M18 |
| M32 | Reverse proxy, image and release | The manager is released as its own image with the OVS release and runs behind any reverse proxy or the bundled Caddy. | M1 |
| M33 | Backup of the manager | The admin downloads the manager's database and keys as one encrypted archive and restores it on a new host. | M4, M7 |
| M34 | Documentation | `README-MANAGER.md` documents everything about the manager, as `README.md` does for server and client and `docs/AUDIO.md` for the audio. | M1 to M33 |

## Decisions (Rezondes, 19 October 2026)

The analysis with the comparison against the code is in the project note `notes/verwaltungstool-analyse.md`.

- **D1 Web app, Docker Compose, port 6999.** Not a desktop app. The manager is set up by Docker Compose and listens on 6999.
- **D2 Ports 7000 to 7099.** At most 100 servers; each uses one port for TCP and UDP.
- **D3 The manager is server admin.** Every server created by the manager has the manager as its only server admin (`OVS_SERVER_ADMIN_FINGERPRINT`, PLAN 190). People get at most the Admin group on a server. The real owner of a server gets a manager account with that server assigned and so acts as server admin through the manager.
- **D4 Roles.** One admin account is the highest authority; only the admin creates and deletes servers and manager accounts. Server managers operate only the servers assigned to them.
- **D5 Login.** User name and password; an e-mail address is optional and used for password reset and notifications.
- **D6 Telemetry always on, all in the manager.** The manager sets the OTLP variables on every server and receives, stores and shows the data itself; no Prometheus, Loki, Tempo or Grafana.
- **D7 Daily users.** The server may keep the fingerprints of one day in memory to count different users per day; only the counts leave it (PLAN 192).
- **D8 Docker access.** The more modern and elegant way (left to Claude): a socket proxy with an allow list per method and path, the socket mounted read-only, the proxy only in an internal network.
- **D9 Frontend Blazor Server.** Avalonia runs in the browser only as WebAssembly on a canvas without SSR; Blazor Server renders HTML on the server and pushes live data over SignalR, enough for about 100 users at the same time.
- **D10 HTTPS is the operator's choice.** The manager serves HTTP on 6999 for a reverse proxy of the operator's choice (the user runs Nginx Proxy Manager); a Caddy compose example is bundled for operators without one.
- **D11 Same repository.** `src/OVS.Manager`, own image `ghcr.io/rezondes/openvoicespeak-manager`, built by the same release.
- **D12 All features.** Every feature proposed in the analysis is planned (M1 to M32).
- **D13 Own documentation (19 October 2026).** At the end the manager gets its own `README-MANAGER.md` in the repository root that documents everything about it, as `README.md` does for server and client and `docs/AUDIO.md` for the audio (Package M34). `README.md` only links to it.
- **D14 Answers to the plan review (19 October 2026).**
  1. `OVS_SERVER_ADMIN_FINGERPRINT` always wins: at every start exactly the manager's identity is server admin, also after a restore, a clone or an adoption (PLAN 190, A202).
  2. The nickname "Serververwaltung" is reserved for the service session (PLAN 191, A203).
  3. A new server takes the manager's `TZ`; it can be changed when the server is created (M7).
  4. `OVS_MANAGER_SERVER_IMAGE` names the server image, default `ghcr.io/rezondes/openvoicespeak-server` (M7, M17, M18); a self-built image works the same way.
  5. Adoption keeps the old volume as it is, without a copy (M29).
  6. The manager backs up its own database and keys (M33).
  7. MA1, MA4 and MA7 stay as written.
  8. Automatic updates are off for every server until the admin turns them on (M18).
  9. Working mode: see below.

## Working mode

The user's decision (review question 9, 19 October 2026), for this file and Packages 190 to 194 of `PLAN.md`:

- Order: Packages 190 to 194 of `PLAN.md` first, then M1 to M34 in the order of the overview.
- Claude commits each package on its own once it is green (message via the create-commit-message skill, without co-author line) and never pushes; the user pushes.
- **A push must never turn the GitHub workflow red.** Before each commit the steps of `.github/workflows/release.yml` that the change touches run on the user's Windows PC (the CI test job runs on `windows-latest`), in a clean worktree holding exactly the content of the commit (`git worktree add` plus the staged diff), so that untracked or ignored local files cannot hide a failure:
  - always: `dotnet test` from the repository root, as CI calls it (covers `PlanReferenceTests`, `SourceLayoutTests` and rule-lint), with zero warnings;
  - a change under `native/`: `cargo test --locked` and `cargo clippy --all-targets --locked -- -D warnings` in `native/ovs_audio`;
  - a change under `website/`: `npm ci --ignore-scripts`, `npm test`, `npm run build`;
  - a change to `Dockerfile`, `src/OVS.Server` or `src/OVS.Shared`: `docker build .` (the image job); from M1 on also `docker build -f Dockerfile.manager .` for a change to the manager or `OVS.Shared`;
  - a change to `src/OVS.Client`: `dotnet publish src/OVS.Client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` (the client job);
  - a change to `.github/workflows/`: `actionlint`, and every command of a new or changed step runs locally first (the workflow itself only runs after the user's push).
- Test runs make no audible sound (muted, null output device), and nothing uses `Thread.Sleep`.
- After each commit the graphify graph is updated (`graphify update .`).
- A failing check is fixed before the commit, never skipped; a package that cannot get green stops the work with a question to the user.

## Assumptions

Set by Claude where the decisions leave a detail open; each can be changed before its package starts.

- **MA1 Stack.** ASP.NET Core on .NET 10, Blazor Web App with interactive server rendering, ASP.NET Core Identity with EF Core on SQLite (`/data/manager.db`), Docker.DotNet for the Docker Engine API, MailKit for SMTP. No UI component library: own components and own SVG charts, so the look follows the client and no large dependency comes in. Same `Directory.Build.props`, analyzers and rules as the rest of the repository.
- **MA2 Tests.** In `tests/OVS.Tests/Manager/` (the folders mirror `src`, CODING-RULES-OVS J2), with bUnit for components and `Microsoft.AspNetCore.Mvc.Testing` for the HTTP pipeline. Docker sits behind `IDockerEngine`; tests use `FakeDockerEngine`. The real adapter and anything that needs a Docker host is a manual check. Test command for every package: `dotnet test OpenVoiceSpeak.slnx --blame-hang-timeout 60s` (covers rule-lint).
- **MA3 Layout.** `src/OVS.Manager` with the folders `Accounts/`, `Audit/`, `Docker/`, `Servers/`, `Control/`, `Telemetry/`, `Mail/`, `Notifications/`, `Api/`, `Components/` (pages and shared components), `Localization/`, `Data/`. `SourceLayoutTests` and `rule-lint.json` get the new root in M1.
- **MA4 One key pair per server.** The manager makes a new identity per server (not one for all), stored in the database and encrypted with ASP.NET Core Data Protection (keys in `/data/keys`). A leaked key then opens one server, not all.
- **MA5 Versions and protocol.** A new server gets the manager's own version by default (same release, same protocol). An update offers only releases with the manager's protocol; a newer protocol means "update the manager first". A server with an older protocol is "veraltet": start, stop, update, console log and telemetry work, protocol features do not.
- **MA6 Networks.** The compose file creates the network `ovs-managed` (fixed name). The manager and all servers are in it; the manager reaches a server as `ovs-<id>:<port>`, the servers send OTLP to `http://ovs-manager:4318`. The OTLP port and the socket proxy are never published.
- **MA7 Retention defaults.** Metrics as 1-minute values for 7 days and as hourly values for 400 days, logs 30 days, traces 7 days, daily statistics forever; each changeable by environment variable.
- **MA8 Public address.** `OVS_MANAGER_PUBLIC_HOST` names the host clients use (for invite links and the overview); without it the manager uses the host of the request.
- **MA9 Deleting.** A deleted server is stopped at once and kept 7 days (container and volume), then both are removed. Its telemetry and audit lines stay until their retention ends.

---

## Package M1: Skeleton

**Goal:** The manager runs as its own container on port 6999 with a SQLite database in `/data` and a health endpoint.

**Dependencies:** none

**Affected files:**
- `src/OVS.Manager/OVS.Manager.csproj`, `Program.cs`, `ManagerOptions.cs`, `Data/ManagerDbContext.cs`, `Data/Migrations/` (new)
- `src/OVS.Manager/Components/App.razor`, `Routes.razor`, `Layout/MainLayout.razor` (new)
- `OpenVoiceSpeak.slnx`, `rule-lint.json`, `tests/OVS.Tests/SourceLayoutTests.cs` (change): the new project and root; in `rule-lint.json` also `src/OVS.Manager` in `clockFolders` and `src/OVS.Manager/Program.cs` in `compositionRoots`
- `tools/rule-lint/LintSettings.cs` (change): `.razor` among the text extensions (em dash rule)
- `tools/rule-lint/Rules/PartialTypeRule.cs`, `TypeFileRule.cs` (change): a `X.razor.cs` code-behind counts like `X.axaml.cs`, not as a part file
- `tools/rule-lint-tests/Rules/PartialTypeRuleTests.cs`, `TypeFileRuleTests.cs`, `EmDashRuleTests.cs` (change)
- `Dockerfile.manager`, `deploy/manager/docker-compose.yml` (new)
- `tests/OVS.Tests/OVS.Tests.csproj` (change): reference, bUnit, `Microsoft.AspNetCore.Mvc.Testing`
- `tests/OVS.Tests/Manager/ManagerHostTests.cs`, `tests/OVS.Tests/Manager/ManagerComposeTests.cs`, `tests/OVS.Tests/TestSupport/ManagerFactory.cs` (new)

### Context

The repository has the server, the client, the shared library and the tools; nothing serves web pages. `ObservabilityComposeTests` already checks compose files as text, the pattern for `ManagerComposeTests`. rule-lint reads only `.cs` and `.axaml` as code; a `.razor` file is markup with a `@code` block it cannot parse. So every component keeps its logic in a `X.razor.cs` code-behind (a partial class, as Avalonia's `X.axaml.cs`), and a `.razor` file holds markup and bindings only; then every rule of `CODING-RULES.md` reaches the manager's code (the user's question, 19 October 2026).

### Acceptance Criteria

- [x] AC1: `GET /healthz` answers 200 with the manager's version; the database is created and migrated at start in `OVS_MANAGER_DATA_DIR` (default `/data`).
- [x] AC2: The image runs as a non-root user, listens on 6999 and keeps its data in the volume `/data`.
- [x] AC3: `deploy/manager/docker-compose.yml` publishes only 6999, uses the network `ovs-managed` by its fixed name and pins every image.
- [x] AC4: `SourceLayoutTests` and rule-lint check `src/OVS.Manager` like the other projects, including the one-clock rule; a `X.razor.cs` code-behind is no part-file finding, and an em dash in a `.razor` file is one.
- [x] AC6: No `.razor` file of the manager contains a `@code` block (checked by a test over the files).
- [x] AC5 (manual): `docker compose -f deploy/manager/docker-compose.yml up -d` starts the manager and the page answers on port 6999. (checked 19 October 2026 with `up -d --build` under its own project name: `/healthz` 200 with the version, the start page 200, the process runs as `app` (uid 1654); removed with `down -v` afterwards)

### Tests (TDD)

1. `ManagerHostTests > "Healthz_Started_OkWithVersion"` and `"Start_EmptyDataDir_DatabaseCreated"` - AC1
2. `ManagerComposeTests > "Image_RuntimeStage_NonRootPort6999DataVolume"` (reads `Dockerfile.manager`) - AC2
3. `ManagerComposeTests > "Compose_Services_OnlyPort6999NetworkNamedImagesPinned"` - AC3
4. `SourceLayoutTests > "Projects_Ours_AreChecked"` with `OVS.Manager` in the list - AC4
5. `PartialTypeRuleTests > "Check_RazorCodeBehind_FindsNothing"`, `TypeFileRuleTests > "Check_RazorCodeBehind_FindsNothing"`, `EmDashRuleTests > "Check_EmDashInRazor_Reports"` - AC4
6. `ManagerHostTests > "Razor_ManagerFiles_NoCodeBlocks"` - AC6
7. Manual check AC5.

(as built: `OVS.Manager` uses `Microsoft.NET.Sdk.Web` with `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 and `Microsoft.EntityFrameworkCore.Design` 10.0.12 (design time only); the tests reference `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, bUnit comes with the first component test (M2). Migrations are made with `dotnet-ef` 10.0.12 from the tool manifest `dotnet-tools.json` (`dotnet tool restore`); their files are named after the migration type (`Initial.cs`, the id stays in the attribute) and marked `// <auto-generated />`, so rule-lint reads them as generated. `ManagerFactory` is a `WebApplicationFactory` over `ManagerOptions` (the top-level `Program` of three projects would be ambiguous) with a temporary data folder. `ComposeFile` in `TestSupport` now reads the compose files for `ObservabilityComposeTests` and `ManagerComposeTests`. `SourceLayout.ListProjects` names the scanned projects and a `X.razor.cs` is named like `X.axaml.cs`. The port is set by `ASPNETCORE_HTTP_PORTS=6999` in `Dockerfile.manager`; the compose file builds the image from the checkout until M32 publishes it, so only pulled images are checked for a pinned tag. Test names got their three parts (RL601). Found on the way: with the ASP.NET Core framework in the test process the OTLP exporter took `IHttpClientFactory` from a service provider that a logger factory had already disposed, so `ServerTelemetryTests` lost the last log lines; `ServerTelemetry.ForSignal` now gives every exporter its own `HttpClient`, as the SDK's default does.)

### Steps

1. Tests 5 (red), then the rule-lint changes (green).
2. Tests 1 to 4 and 6 (red), then project, host, database, Dockerfile, compose (green).
3. Manual check; full suite, rule-lint.

### Out of Scope

- Pages beyond the layout (M2 ff.), the release job (M32)

---

## Package M2: Languages and theme

**Goal:** Every page of the manager is German or English and light or dark, chosen per user.

**Dependencies:** M1

**Affected files:**
- `src/OVS.Manager/Localization/Strings.resx`, `Strings.en.resx`, `UiCulture.cs` (new)
- `src/OVS.Manager/Components/Layout/ThemeSwitch.razor`, `LanguageSwitch.razor`, `wwwroot/app.css` (new): colour tokens for light and dark
- `tests/OVS.Tests/Manager/Localization/ManagerLocalizationTests.cs`, `tests/OVS.Tests/Manager/Components/LayoutTests.cs` (new)

### Context

The client is German with English (`src/OVS.Client/Localization/`), so the manager follows: German by default, English as the second language, all user-facing text in `.resx`.

### Acceptance Criteria

- [x] AC1: Without a choice the browser's language decides (German for `de-*`, otherwise English); the choice of a logged-in user is stored with the account and wins.
- [x] AC2: Every key of `Strings.resx` exists in `Strings.en.resx` and the other way round.
- [x] AC3: The theme follows the system until the user picks light or dark; the pick is stored.
- [x] AC4: No page contains a German or English text outside the resources (checked by a test over the `.razor` files).

### Tests (TDD)

1. `ManagerLocalizationTests > "Culture_BrowserGerman_German"` and `"Culture_StoredChoice_Wins"`, `AccountPagesTests > "Profile_Language_StoredWithAccount"` (M4) - AC1
2. `ManagerLocalizationTests > "Resources_GermanAndEnglish_SameKeys"` - AC2
3. `LayoutTests > "Theme_Picked_StoredAndApplied"` - AC3
4. `ManagerLocalizationTests > "Razor_ManagerPages_NoLiteralText"` - AC4

(as built: the pages are rendered on the server per request (static SSR), so the login of M3 can set its cookie; a page with live data opts into interactive server rendering itself. `UiCulture.CreateOptions` is the request localization: the culture cookie, then `Accept-Language`, then English; `de-*` falls back to German. The choice is stored in the culture cookie; since M4 the profile also stores it with the account, and the login writes it into the cookie again. `/culture` and `/theme` store the pick in a cookie and go back to a local address only (anything else is 400, no open redirect); `ThemeCookie` keeps `Theme` (system, light, dark) and `App` sets `data-theme` only for a pick, so `app.css` follows `prefers-color-scheme` otherwise. No bUnit yet: the layout tests read the rendered HTML through `ManagerFactory`. Razor generates public components, so CA1515 is off for `src/OVS.Manager/Components/**.razor.cs` in `.editorconfig`.)

### Steps

1. Tests (red), then resources, culture, theme (green). Full suite, rule-lint.

### Out of Scope

- Further languages

---

## Package M3: First admin and login

**Goal:** The first start leads to exactly one admin account, and everyone logs in with user name and password.

**Dependencies:** M2

**Affected files:**
- `src/OVS.Manager/Accounts/ManagerUser.cs`, `ManagerRoles.cs`, `FirstAdmin.cs`, `LoginLimits.cs` (new)
- `src/OVS.Manager/Components/Pages/Setup.razor`, `Login.razor`, `Logout.razor` (new)
- `src/OVS.Manager/Program.cs` (change): Identity, cookie, antiforgery (forwarded headers come with M32)
- `tests/OVS.Tests/Manager/Accounts/FirstAdminTests.cs`, `LoginTests.cs` (new)

### Context

ASP.NET Core Identity brings hashing, lockout, security stamps and reset tokens (MA1). The roles are `Admin` and `Manager` (D4). Sign-in by user name; e-mail optional (D5).

### Acceptance Criteria

- [x] AC1: While no account exists, every page leads to `/setup`, where the first account is created with role Admin; afterwards `/setup` answers 404.
- [x] AC2: With `OVS_MANAGER_ADMIN_USER` and `OVS_MANAGER_ADMIN_PASSWORD` set and no account, the admin is created at start and `/setup` is never shown.
- [x] AC3: Login with user name and password; a wrong pair shows one text for both cases; 5 failures lock the account for 15 minutes, and more than 20 failures per IP in 15 minutes are refused.
- [x] AC4: Passwords need at least 12 characters; the cookie is HttpOnly, SameSite=Strict, and Secure whenever the request is HTTPS; every form carries an antiforgery token.
- [x] AC5: Logout ends the session; a page without login leads to `/login` and back after it.

### Tests (TDD)

1. `FirstAdminTests > "Setup_NoAccount_RedirectedAndAdminCreated"` and `"Setup_AccountExists_NotFound"` - AC1
2. `FirstAdminTests > "Environment_NoAccount_AdminCreatedNoSetup"` - AC2
3. `LoginTests > "Login_WrongPassword_SameTextAsUnknownUser"`, `"Login_FiveFailures_Locked"`, `"Login_ManyFailuresFromIp_Refused"` - AC3
4. `LoginTests > "Password_Short_Refused"` and `"Cookie_Flags_HttpOnlyStrict"` - AC4
5. `LoginTests > "Page_NotLoggedIn_LoginAndBack"` and `"Logout_LoggedIn_SessionEnded"` - AC5

(as built: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12; `ManagerDbContext` is an `IdentityDbContext<ManagerUser>` with the migration `Identity`. `FirstAdmin` (a singleton) creates both roles at start, the admin of the environment while no account exists, and the admin of `/setup`; it remembers once an account exists, so `SetupGate` asks the database only until then. `SetupGate` leads every request but the health check, the switches, the framework and files to `/setup` while no account exists, and answers 404 on `/setup` afterwards. Every page needs a login (fallback policy); `/setup`, `/login`, `/healthz`, `/culture`, `/theme` and the static files are anonymous. The login shows one text for an unknown user, a wrong password and a locked account, so it never tells which accounts exist; `LoginLimits` counts failures per address on the run's `TimeProvider`. Logout is `POST /logout` (`AccountEndpoints`, antiforgery required) with a button in the layout instead of a `Logout.razor` page. `LocalUrls` checks every return address (login, switches). `ManagerForm` in `TestSupport` submits a page's form like a browser (hidden antiforgery and form-name fields); `ManagerFactory` takes settings and asks for German pages.)

### Steps

1. Tests 1 and 2 (red), then Identity and `FirstAdmin` (green).
2. Tests 3 to 5 (red), then login, limits, cookie (green). Full suite, rule-lint.

### Out of Scope

- Mail reset (M26), TOTP (M28)

---

## Package M4: Accounts

**Goal:** The admin creates, changes, locks and deletes server managers, and every user changes their own password, e-mail and sessions.

**Dependencies:** M3

**Affected files:**
- `src/OVS.Manager/Accounts/AccountService.cs` (new)
- `src/OVS.Manager/Components/Pages/Accounts/AccountList.razor`, `AccountEdit.razor`, `Profile.razor` (new)
- `tests/OVS.Tests/Manager/Accounts/AccountServiceTests.cs`, `tests/OVS.Tests/Manager/Components/AccountPagesTests.cs` (new)

### Context

D4: the admin is the highest authority; managers are created only by the admin. A forgotten password without mail is reset by the admin (D5, M26).

### Acceptance Criteria

- [x] AC1: The admin creates a manager with user name, start password and optional e-mail; the manager must change the password at the first login.
- [ ] AC2: The admin locks, unlocks, renames, resets the password of and deletes managers; a deleted manager loses every assignment.
- [x] AC3: The admin account cannot be deleted, locked or demoted, and exactly one admin exists.
- [x] AC4: Every user changes their own password (with the old one), e-mail and language, sees their active sessions and ends all others ("überall abmelden").
- [x] AC5: A manager reaches no account page except their profile (403).

### Tests (TDD)

1. `AccountServiceTests > "Create_Manager_MustChangePassword"` - AC1
2. `AccountServiceTests > "Manage_Manager_LockRenameResetDelete"` - AC2 (`"Delete_Manager_AssignmentsGone"` comes with the assignments, M10)
3. `AccountServiceTests > "Admin_DeleteLockDemote_Refused"` - AC3
4. `AccountPagesTests > "Profile_ChangePassword_NeedsOld"`, `"Profile_EndOtherSessions_OthersLoggedOut"` and `"Profile_Language_StoredWithAccount"` - AC4
5. `AccountPagesTests > "AccountList_Manager_Forbidden"` - AC5

(as built: AC2 stays open until M10 adds the assignments, whose rows go with the account. `ManagerUser` got `MustChangePassword` and `Language` (migration `AccountsAndSessions`). A login cookie carries its `ManagerSession` (`SessionStore`, table `Sessions`, deleted with the account) and is valid only while it exists, so ending the other sessions, a lock and a reset log other browsers out at their next request; `SessionCookieEvents` adds the session and the must-change claim at sign-in, writes the account's language into the culture cookie, keeps Identity's security stamp check and answers a forbidden page with 403. `PasswordChangeGate` leads every page to `/profile` while the start password is in use. `AccountService` refuses every change of the admin and any promotion with `AdminProtected`; there is no role change but this refusal. The profile page is `ProfilePage` (`Profile` clashes with `System.Web.Profile`, CA1724). `TextFormat` fills the placeholders of a resource text.)

### Steps

1. Service tests (red, green), then pages. Full suite, rule-lint.

### Out of Scope

- A second admin

---

## Package M5: Audit log

**Goal:** Every change a user makes in the manager is recorded with who, when, what and which server, and the admin can read and filter it.

**Dependencies:** M4

**Affected files:**
- `src/OVS.Manager/Audit/AuditEntry.cs`, `AuditLog.cs` (new)
- `src/OVS.Manager/Components/Pages/AuditPage.razor` (new)
- `src/OVS.Manager/Accounts/AccountService.cs` (change): writes its entries
- `tests/OVS.Tests/Manager/Audit/AuditLogTests.cs` (new)

### Context

Comes before every server action so that M7 ff. write their lines from the start. Retention: forever unless `OVS_MANAGER_AUDIT_DAYS` is set.

### Acceptance Criteria

- [x] AC1: Each entry holds time (UTC, shown in local time), user, action, target (account or server) and a short detail; it is never changed afterwards.
- [x] AC2: Logins, failed logins and every action of M4 write an entry.
- [x] AC3: The admin filters by user, server, action and time span and exports the result as CSV; a manager sees only entries of their assigned servers.
- [x] AC4: With `OVS_MANAGER_AUDIT_DAYS` older entries are deleted once a day.

### Tests (TDD)

1. `AuditLogTests > "Write_Entry_AllFieldsReadOnly"` - AC1
2. `AuditLogTests > "Accounts_Actions_Recorded"` and `"Login_Failed_Recorded"` - AC2
3. `AuditLogTests > "Filter_ServerAndUser_OnlyTheirLines"`, `"Export_FilteredLines_Csv"`, `"Query_ManagerScope_OnlyAssignedServers"` - AC3
4. `AuditLogTests > "Retention_Set_OldEntriesDeleted"` - AC4

(as built: `AuditEntry` has init-only fields and `AtUtc` as a UTC `DateTime` (SQLite cannot compare `DateTimeOffset`; a converter gives the kind back); migration `AuditLog` with indexes on time, server and user. `AuditLog` writes and queries (newest first, at most 1000 lines); `AccountService` writes every change of M4 with the acting user of the request (`IHttpContextAccessor`, "system" outside one), the login, setup and logout write theirs. `AuditScope` gives the admin every line and a manager the lines of their servers, none until M10 assigns any. `/audit` filters by user, server, action and local days; `/audit/export` gives the same filter as CSV (RFC 4180, a leading formula character defused). `AuditRetention` (a hosted service on the run's `TimeProvider`) deletes once a day with `OVS_MANAGER_AUDIT_DAYS`; `ManagerOptions` refuses a value that is no number of days above 0. `ManagerHostTests > Model_Current_NoPendingChanges` makes sure every model change has its migration.)

### Steps

1. Tests (red), then log and page (green). Full suite, rule-lint.

### Out of Scope

- The audit lines of the OVS servers themselves (they stay in the server log, M16)

---

## Package M6: Docker access

**Goal:** The manager reaches Docker only through a socket proxy with an allow list and touches only containers marked as its own.

**Dependencies:** M1

**Affected files:**
- `src/OVS.Manager/Docker/IDockerEngine.cs`, `DockerEngine.cs` (Docker.DotNet), `ManagedLabels.cs`, `ManagedContainer.cs` (new)
- `deploy/manager/docker-compose.yml` (change): service `socket-proxy` (`wollomatic/socket-proxy`, pinned) with regex allow list, socket read-only, network `docker-proxy` internal
- `tests/OVS.Tests/TestSupport/FakeDockerEngine.cs` (new)
- `tests/OVS.Tests/Manager/Docker/DockerEngineTests.cs`, `tests/OVS.Tests/Manager/ManagerComposeTests.cs` (change)

### Context

D8. The proxy allows only the calls the manager makes: list, inspect, create, start, stop, restart, remove, logs and stats of containers, pull and inspect of images, create and remove of volumes, connect to the network. The proxy cannot filter by label, so the manager refuses every container without `ovs.managed=true` itself (`ManagedLabels`).

### Acceptance Criteria

- [x] AC1: The compose file gives the proxy the socket read-only, publishes no port of it, and its allow list contains exactly the calls named above.
- [x] AC2: `DockerEngine` lists only containers with `ovs.managed=true`; any call for another container fails without reaching Docker.
- [x] AC3: Every container the manager creates carries `ovs.managed`, `ovs.server-id`, `ovs.port` and `ovs.version`, and its volume is named `ovs-<id>-data`.
- [x] AC4: An unreachable proxy shows "Docker nicht erreichbar" on the overview and in `/healthz` (status 503), and the rest of the manager keeps working.
- [x] AC5 (manual): On a Docker host the manager lists, starts and stops a test container through the proxy, and a call outside the allow list is refused by the proxy (403). (checked 19 October 2026 on Docker Desktop 29.2.1 with the proxy of the compose file, a curl container standing in for the manager (allowed in `-allowfrom`) and a container labelled `ovs.managed=true`: list 200, stop and start 204 with the state following, `GET /info` and `POST .../exec` 403, the socket mounted read-only; the manager itself starts and stops servers from M8 on. Correction found in M7: Docker.DotNet sends its paths without the `/v1.x` prefix, so POST and DELETE take the version as optional like GET)

### Tests (TDD)

1. `ManagerComposeTests > "SocketProxy_Compose_ReadOnlyNoPortAllowList"` - AC1
2. `DockerEngineTests > "List_Labels_OnlyManaged"` and `"Call_ForeignContainer_RefusedBeforeDocker"` - AC2
3. `DockerEngineTests > "Create_Spec_LabelsAndVolumeName"` - AC3
4. `DockerEngineTests > "Health_ProxyDown_Degraded"` - AC4
5. Manual check AC5.

(as built: `Docker.DotNet` 3.125.15 and `wollomatic/socket-proxy:1.13.1`; the proxy runs as `65534:999` (the socket's group on the host, see the question in the hand-over) with a read-only root, no capabilities and `no-new-privileges`, only in the internal network `docker-proxy`, which the manager joins as well. `DockerEngine` lists with the label filter and checks the label again; before any call with a container id it lists the managed containers and throws `ForeignContainerException` for any other id, so nothing with it is sent. A server's container is `ovs-<id>` with its volume `ovs-<id>-data`, the same port inside and outside for TCP and UDP, the network and `unless-stopped`. `/healthz` answers 503 with `docker: false` while the proxy does not answer within 2 s, and the start page shows "Docker nicht erreichbar". The adapter's tests run against `FakeDockerApi`, a Docker Engine API on localhost that keeps every request; the later packages use `FakeDockerEngine`, which `ManagerFactory` puts in unless a test asks for the real adapter.)

### Steps

1. Tests (red), then interface, adapter, labels, compose (green). Manual check; full suite, rule-lint.

### Out of Scope

- Rootless Docker and Podman (a note in `README-MANAGER.md`, M34)

---

## Package M7: Create a server

**Goal:** The admin creates a server with name, port from 7000 to 7099 and version, and the manager is its server admin from the first start.

**Dependencies:** M5, M6, PLAN 190

**Affected files:**
- `src/OVS.Manager/Servers/ManagedServer.cs`, `ServerCatalog.cs`, `PortPool.cs`, `ServerIdentityStore.cs`, `ServerFactory.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/CreateServer.razor` (new)
- `tests/OVS.Tests/Manager/Servers/PortPoolTests.cs`, `ServerFactoryTests.cs` (new)

### Context

D2, D3, D6, MA4, MA6. The container gets `OVS_PORT`, `OVS_SERVER_NAME`, `OVS_MAX_USERS`, `OVS_PASSWORD`, `TZ`, `OVS_SERVER_ADMIN_FINGERPRINT` (the new identity of MA4) the image `OVS_MANAGER_SERVER_IMAGE:<version>` (D14) and the OTLP variables (`OTEL_EXPORTER_OTLP_ENDPOINT=http://ovs-manager:4318`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, `OTEL_SERVICE_NAME=ovs-server`, `OTEL_RESOURCE_ATTRIBUTES=service.instance.id=<id>`), the port mapped for TCP and UDP, the volume and the network, restart policy `unless-stopped`.

### Acceptance Criteria

- [x] AC1: The form takes name, max users, password (optional), version (default: the manager's own, MA5), time zone (default: the manager's `TZ`, D14) and port (default: the lowest free one); a port outside 7000 to 7099, taken by another server or in use on the host is refused with a text.
- [x] AC2: With 100 servers the button is off with "Alle 100 Ports sind belegt".
- [x] AC3: The created container has exactly the variables, mapping, labels, volume, network and restart policy above; the identity's private key is stored encrypted and never shown.
- [x] AC4: Only the admin reaches the page; the creation writes an audit line.
- [ ] AC5 (manual): A created server starts, a client connects, and the server log shows "Server admin set from OVS_SERVER_ADMIN_FINGERPRINT" and no token.

### Tests (TDD)

1. `PortPoolTests > "Next_SomeTaken_LowestFree"`, `"Check_OutOfRangeOrTaken_Refused"`, `"Next_HundredServers_NoneFree"` - AC1, AC2
2. `ServerFactoryTests > "Create_ContainerSpec_ExactVariablesAndMappings"` (on `FakeDockerEngine`) - AC3
3. `ServerFactoryTests > "Identity_PrivateKey_StoredEncrypted"` - AC3
4. `ServerFactoryTests > "Create_Manager_Forbidden"` and `"Create_Admin_AuditLine"` - AC4
5. Manual check AC5.

(as built: `ManagedServer` and `ServerIdentity` (migration `AddServers`: a migration class named `Servers` would clash with the namespace, one server per port as a unique index); `ServerIdentityStore` makes one `ClientIdentity` per server and keeps its PKCS#8 key only protected by Data Protection (keys in `/data/keys`, application name `OVS.Manager`). `ServerFactory` checks name (`TextRules`), user limit (1 to 1000), time zone and port (`PortPool`), creates the container through `IDockerEngine`, saves, starts and writes `ServerCreated`; a port Docker reports as taken on the host (`PortInUseException`) removes the container and the rows again and shows "Der Port ist auf dem Host schon belegt" (since M9 its empty volume too). `ManagerOptions` gives `OVS_MANAGER_SERVER_IMAGE` (default `ghcr.io/rezondes/openvoicespeak-server`) and `TZ` (default UTC). Docker failures are `DockerCallException` and show a text instead of a 500. Manual check AC5 (19 October 2026, Docker Desktop 29.2.1): the compose file with a locally built server image, the admin from the environment, a server created through `/servers/new` on port 7050: the container `ovs-<id>` ran with exactly the labels, the volume, the network and `unless-stopped`, and its log showed "Server admin set from OVS_SERVER_ADMIN_FINGERPRINT" and no token. It also found the proxy's version prefix (M6). The client's connection is still to be checked by hand.)

### Steps

1. Tests 1 to 4 (red), then pool, identity store, factory, page (green). Manual check; full suite, rule-lint.

### Out of Scope

- Clone and adopt (M29, M30), resource limits (M25)

---

## Package M8: Overview and start, stop, restart

**Goal:** The overview lists every visible server with status, version and port, and each can be started, stopped and restarted.

**Dependencies:** M7

**Affected files:**
- `src/OVS.Manager/Servers/ServerStatus.cs`, `StatusWatcher.cs`, `ServerLifecycle.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/ServerList.razor`, `ServerPage.razor` (new)
- `tests/OVS.Tests/Manager/Servers/StatusWatcherTests.cs`, `ServerLifecycleTests.cs` (new)
- (as built, also) `src/OVS.Manager/Docker/InspectedContainer.cs`, `Servers/ServerSnapshot.cs`, `IServerProbe.cs`, `TlsServerProbe.cs`, `Localization/ServerTexts.cs`, `Components/Pages/Servers/ServerWatchPage.cs`, `ServerActions.razor` (new); `Components/Pages/Home.razor` (removed, the overview is the start page); `tests/OVS.Tests/Manager/Servers/TlsServerProbeTests.cs`, `TestSupport/FakeServerProbe.cs`, `ServerPages.cs` (new)

### Context

Status comes from Docker's list and inspect every 5 seconds (running, restarting, exited with code; the socket proxy passes no events) and from a TLS connect to the port (the server answers). The server ends cleanly on SIGTERM and tells clients "ServerShutdown" (`Program.cs`, Package 5); a stop uses a timeout of 15 s.

### Acceptance Criteria

- [x] AC1: The overview shows per server name, status ("läuft", "startet", "gestoppt", "abgestürzt", "Neustart-Schleife", "nicht erreichbar"), version, port, users online and uptime, and updates live without reload.
- [x] AC2: Start, stop and restart work from the overview and the server page and write audit lines; a running action disables the buttons of that server.
- [x] AC3: More than 3 restarts within 10 minutes show "Neustart-Schleife" with the last exit code.
- [x] AC4: "läuft" needs the container running and a TLS handshake on the port; a running container without answer is "nicht erreichbar".
- [x] AC5: The manager's own restart loses no status: containers are found again by their labels (M6 AC3).

### Tests (TDD)

1. `StatusWatcherTests > "Refresh_DockerStates_StatusMapped"` and `"List_StatusChange_RedrawnLive"` (bUnit) - AC1
2. `ServerLifecycleTests > "StartStopRestart_Overview_AuditAndButtonsDisabled"` (bUnit) and `"Start_UnknownServer_NotFound"` - AC2
3. `StatusWatcherTests > "Refresh_FourRestartsInTenMinutes_RestartLoop"` (with `ManualTimeProvider`) - AC3
4. `StatusWatcherTests > "Refresh_RunningNoHandshake_NotReachable"`, `TlsServerProbeTests > "Answers_RunningServer_True"` and `"Answers_ClosedPort_False"` - AC4
5. `StatusWatcherTests > "Refresh_NewWatcher_ServersFoundByLabels"` - AC5
6. `DockerEngineTests > "Inspect_Labels_StateOrRefused"` - the adapter's inspect

(as built: the socket proxy allows no `events`, so `StatusWatcher` (a singleton and hosted service) lists the managed containers every 5 seconds, inspects each (`IDockerEngine.InspectAsync`, refused for a container without the label) and finds a server's container by its label `ovs.server-id`, not by the stored id; `ServerLifecycle` does the same, so a recreated container is found again. Mapping: running with a TLS handshake is "läuft", without one "startet" for 30 s after its start and then "nicht erreichbar"; restarting is "startet"; exited or dead with an exit code other than 0 is "abgestürzt" with the code; exited with 0, created and paused are "gestoppt"; a server without container is "nicht erreichbar". AC3 counts the rises of Docker's restart count since the manager started: more than 3 within 10 minutes is "Neustart-Schleife" with the last exit code. `TlsServerProbe` (behind `IServerProbe`, the tests use `FakeServerProbe`) connects to `ovs-<id>:<port>` in the network `ovs-managed` and only finishes the handshake; the certificate is not checked (CA5359 suppressed: self-signed, nothing is sent), and the server drops such a connection without a log line. A running action marks the server busy in the watcher, which turns its buttons off in every browser and refuses a second action. The overview `ServerList` is the start page `/` (it took the Docker banner of `Home`), `ServerPage` is `/servers/{id}`; both inherit `ServerWatchPage`, render interactively on the server and draw again on the watcher's `Changed`. Visible are the servers of `AuditScope.For`: the admin all, a manager none until M10. Users online show "–" until the service session of M11 reports them. The stop keeps M6's 30 s for the server's own shutdown. bUnit 2.11.3 renders the pages in the tests on the services of `ManagerFactory`.)

### Steps

1. Tests (red), then watcher, lifecycle, pages (green). Full suite, rule-lint.

### Out of Scope

- Announcements before a stop (M19)

---

## Package M9: Delete a server

**Goal:** The admin deletes a server after typing its name, and it can be restored for 7 days before its data is gone.

**Dependencies:** M8

**Affected files:**
- `src/OVS.Manager/Servers/ServerDeletion.cs`, `DeletionSweep.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/DeleteServerDialog.razor`, `Trash.razor` (new)
- `tests/OVS.Tests/Manager/Servers/ServerDeletionTests.cs` (new)

### Context

D4 and MA9. The port stays taken while the server is in the trash.

### Acceptance Criteria

- [x] AC1: Delete is offered only to the admin and needs the exact server name typed; the server is stopped and listed in the trash with its end date.
- [x] AC2: Restoring within 7 days brings back the same container, volume, port and assignments.
- [x] AC3: After 7 days the daily sweep removes container and volume and frees the port; "jetzt endgültig löschen" does it at once after a second confirmation.
- [x] AC4: Every step writes an audit line.

### Tests (TDD)

1. `ServerDeletionTests > "Delete_NameWrong_Refused"` and `"Delete_Admin_StoppedInTrash"` (bUnit, and 403 on the trash for a manager) - AC1
2. `ServerDeletionTests > "Restore_WithinPeriod_SameServer"` - AC2
3. `ServerDeletionTests > "Sweep_AfterSevenDays_RemovedPortFree"` and `"DeleteNow_Confirmed_Removed"` - AC3
4. `ServerDeletionTests > "Steps_DeleteRestorePurge_AuditLines"` - AC4

(as built: `ManagedServer.DeletedAtUtc` marks a server in the trash (migration `AddServerDeletion`). `ServerDeletion` deletes (exact name, `ServerProblem.NameMismatch` otherwise; container stopped), restores (container started again), purges (container, volume through the new `IDockerEngine.RemoveVolumeAsync`, identity and row) and sweeps; every step writes `ServerDeleted`, `ServerRestored` or `ServerPurged`, the sweep as "system". `DeletionSweep` runs at the start and then daily. The watcher and `ServerLifecycle` leave servers in the trash out, while `PortPool` still counts their ports. The dialog `DeleteServerDialog` sits on the server page for the admin only; the trash `/servers/trash` (admin only, link "Papierkorb") lists the day each server goes for good and asks a second time before "Jetzt endgültig löschen". Assignments (M10) live in their own rows and stay untouched by delete and restore.)

### Steps

1. Tests (red), then deletion, sweep, dialog, trash (green). Full suite, rule-lint.

### Out of Scope

- Deleting telemetry early (MA9)

---

## Package M10: Assign servers

**Goal:** The admin assigns servers to managers, and a manager sees and operates only the assigned ones.

**Dependencies:** M8

**Affected files:**
- `src/OVS.Manager/Servers/ServerAssignment.cs`, `ServerAccessHandler.cs` (new): authorization policy per server
- `src/OVS.Manager/Components/Pages/Servers/Assignments.razor` (new)
- `tests/OVS.Tests/Manager/Servers/ServerAccessTests.cs` (new)

### Context

D4 and D3: the owner of a server gets a manager account with that server and so acts as its server admin.

### Acceptance Criteria

- [x] AC1: The admin assigns any number of servers to a manager and any number of managers to a server, on the account and on the server page.
- [x] AC2: A manager's overview lists only assigned servers; any page, action or API call for another server answers 403 (also by typing the URL).
- [x] AC3: A manager may start, stop, restart, update and operate assigned servers but never create, delete or assign.
- [x] AC4: Every change of an assignment writes an audit line.

### Tests (TDD)

1. `ServerAccessTests > "Assign_AccountAndServerPage_SameAssignments"` - AC1
2. `ServerAccessTests > "Manager_OtherServer_Forbidden"` (pages and endpoints) - AC2
3. `ServerAccessTests > "Manager_CreateDeleteAssign_Forbidden"` - AC3
4. `ServerAccessTests > "Assign_EachChange_AuditLine"` - AC4

(as built: `ServerAssignment` rows (migration `AddServerAssignments`, key manager and server, deleted with the account or the purged server; delete and restore of M9 keep them). `ServerAccess` replaces M5's `AuditScope`: the admin sees every server, a manager the assigned ones, for the overview, the server page and the audit page and export. `ServerAccessHandler` (with `ServerAccessRequirement`) is the policy `ServerAccess` on `/servers/{id}` and answers 403 for a server that is not the manager's. `ServerAssignmentList` lists managers and servers and sets an assignment, only for a manager, writing `ServerAssigned` or `ServerUnassigned` with the manager as target. The component `Assignments` shows the checkboxes on a manager's account page and on the server page, for the admin only; creating, deleting and assigning stay admin pages. `TestSupport/ManagerSignIn` makes and logs in a manager for the tests. The REST API of M31 checks the same `ServerAccess`.)

### Steps

1. Tests (red), then policy and page (green). Full suite, rule-lint.

### Out of Scope

- Finer rights per manager and server (every assigned manager is server admin, D3)

---

## Package M11: Connection to the servers

**Goal:** The manager holds one invisible service session to each running server of its protocol and marks the others as outdated.

**Dependencies:** M8, PLAN 191, PLAN 194

**Affected files:**
- `src/OVS.Manager/Control/ServerConnections.cs`, `ServerLink.cs`, `ServerMirror.cs` (new): one `ControlConnection` per server with `Service = true`, the state of the server as a mirror
- `src/OVS.Manager/Servers/ManagedServer.cs` (change): certificate fingerprint (pinned on first connect)
- `tests/OVS.Tests/Manager/Control/ServerConnectionsTests.cs` (new)

### Context

MA5, A205 of PLAN.md. The manager connects inside `ovs-managed` as `ovs-<id>:<port>`. The mirror is what M12 to M16 read; it follows the server's broadcasts like the client's `StateMirror`.

### Acceptance Criteria

- [x] AC1: For every running server a service session is open within 10 s of its start and reconnects with backoff after a loss.
- [x] AC2: `VersionMismatch` marks the server "veraltet" with its protocol, and no protocol page of it is offered; Docker actions stay.
- [x] AC3: The certificate fingerprint is stored at the first connect; a later different certificate stops the connection and shows "Zertifikat geändert" until the admin accepts the new one (audit line).
- [x] AC4: The mirror holds users online, channels, groups and settings, live from the server's messages.
- [x] AC5: No user of the server sees the manager in a channel or the user list.

### Tests (TDD)

1. `ServerConnectionsTests > "Running_ServerRestarted_ConnectedAgain"` (in-process server) - AC1
2. `ServerConnectionsTests > "VersionMismatch_Server_Outdated"` (`HandshakeStub`) - AC2
3. `ServerConnectionsTests > "Certificate_Changed_StoppedUntilAccepted"` - AC3
4. `ServerConnectionsTests > "Mirror_UserJoinsAndLeaves_Followed"` - AC4
5. `ServerConnectionsTests > "Service_UserOnServer_NeverSeesIt"` - AC5

(as built: the client's `StateMirror` and `ChannelPair` moved to `OVS.Shared/Net` (public; `StateMirror.Groups` is an `IReadOnlyList`), so the manager mirrors a server with the same code; `ServerLink` keeps it under a lock. `ServerConnections` (singleton, hosted) opens a `ServerLink` for every server whose container runs, from the watcher's checks, and ends it when the container stops or the server goes to the trash. A link connects with `ControlConnection` as "Serververwaltung" with `Service = true` and the manager's identity for that server, retries after 1 s, doubling up to 8 s (so a started server is reached within 10 s), and after `VersionMismatch` waits 5 minutes ("veraltet (Protokoll N)" from the server's detail text). The first certificate is pinned in `ManagedServer.CertificateFingerprint` (migration `AddCertificateFingerprint`); another one stops the link with `CertificateChangedException` and "Zertifikat geändert", until the admin clicks "Neues Zertifikat annehmen" on the server page, which pins it, writes `CertificateAccepted` with the fingerprint and connects again. `OVS_MANAGER_SERVER_HOST` names one host for all servers (a manager outside Docker; the tests use 127.0.0.1); without it a server is `ovs-<id>`, also for the probe of M8. The overview shows the link's state next to the name and the users online (without the service session) from the mirror; the server page has a row "Verwaltung".)

### Steps

1. Tests (red), then connections, link, mirror (green). Full suite, rule-lint.

### Out of Scope

- Pages that use the mirror (M12 to M16)

---

## Package M12: Server settings

**Goal:** Name, max users, password, auto-restart, log retention and icon of a server are changed in the manager.

**Dependencies:** M11

**Affected files:**
- `src/OVS.Manager/Components/Pages/Servers/ServerSettings.razor` (new)
- `src/OVS.Manager/Control/SettingsCommands.cs` (new): `UpdateServerSettings`, `SetServerIcon`
- `tests/OVS.Tests/Manager/Control/SettingsCommandsTests.cs` (new)

### Context

The protocol has `ServerSettingsInfo`, `UpdateServerSettings`, `SetServerIcon` (Packages 69, 151 ff.); the ranges come from `ServerLimitsRule`.

### Acceptance Criteria

- [x] AC1: The page shows the current values from the mirror and saves changes with `UpdateServerSettings`; a refused value shows the server's error text.
- [x] AC2: Changes from a client admin appear on an open page without reload.
- [x] AC3: An icon upload is checked for size and type like in the client and set with `SetServerIcon`.
- [x] AC4: Each save writes an audit line with the changed fields (the password only as "geändert").

### Tests (TDD)

1. `SettingsCommandsTests > "Save_Values_SentAndRefusalShown"` - AC1
2. `SettingsCommandsTests > "ExternalChange_OpenPage_Updated"` (bUnit) - AC2
3. `SettingsCommandsTests > "Icon_TooLargeOrNoPng_Refused"` - AC3
4. `SettingsCommandsTests > "Save_Audit_FieldsWithoutPassword"` - AC4

(as built: `ServerLink.RequestAsync` sends a request with a new id and waits up to 10 s for the server's `Error` with that id (returned) or the message that shows it was done (`ServerSettingsChanged` here). `SettingsCommands` builds `UpdateServerSettings` from `SettingsInput` (name, welcome text, password new or removed, max users, log days, daily log file, daily restart and its time; the AFK settings of the mirror stay) and takes over a new name in the manager's row too; a refusal shows "Der Server hat abgelehnt:" with the server's text. The icon is checked for 512 KB and a square PNG of 64 to 512 px before it is sent (the client also scales a larger picture, the manager has no image library); `SetServerIcon` sets it. Each save writes `ServerSettingsChanged` with the changed fields, the password only as "password changed" or "password removed", and `ServerIconChanged`. The page `/servers/{id}/settings` (policy `ServerAccess`, so assigned managers too) follows the mirror and takes a change from a client admin unless the form holds unsaved input; the server page links to it. Without a connection or for an outdated server it says so. `TestSupport/ServerLinks` starts in-process servers with the manager's open service session for M11 ff.)

### Steps

1. Tests (red), then commands and page (green). Full suite, rule-lint.

### Out of Scope

- Channels (they stay in the client)

---

## Package M13: Users, groups and moderation

**Goal:** Online users, known users, bans and groups of a server are managed in the manager, including giving the owner the Admin group.

**Dependencies:** M11

**Affected files:**
- `src/OVS.Manager/Components/Pages/Servers/Users.razor`, `Bans.razor`, `Groups.razor` (new)
- `src/OVS.Manager/Control/ModerationCommands.cs`, `GroupCommands.cs` (new)
- `tests/OVS.Tests/Manager/Control/ModerationCommandsTests.cs`, `GroupCommandsTests.cs` (new)

### Context

The protocol has kick, ban, unban, server mute (timed), move, delete known user, list bans and users, and the group messages (Packages 8, 71, 72, 80, 166). The manager is server admin, so it may act on everyone (Package 181 AC4).

### Acceptance Criteria

- [x] AC1: The online list shows nickname, channel, groups, muted state and connect time live; kick, ban (with reason and duration), server mute (with duration) and move work from it.
- [x] AC2: The known users list shows first and last login, online and speech time and lets the manager assign and remove groups, among them Admin.
- [x] AC3: The ban list shows reason, end and who banned; unban works.
- [x] AC4: Groups are created, changed, ordered and deleted; Guest, Admin and Server-Admin show as fixed like in the client.
- [x] AC5: Every action writes an audit line in the manager besides the server's own.

### Tests (TDD)

1. `ModerationCommandsTests > "Online_MuteMoveKickBan_Followed"` - AC1
2. `GroupCommandsTests > "KnownUser_AssignAdmin_Sent"` - AC2
3. `ModerationCommandsTests > "Bans_Unban_ListedAndLifted"` - AC3
4. `GroupCommandsTests > "FixedGroups_Page_NotEditable"` (bUnit) and `"Groups_CreateMoveDelete_ChangedOnServer"` - AC4
5. `ModerationCommandsTests > "Actions_EachOne_AuditLine"` - AC5

(as built: `ModerationCommands` reads the users online and the channels from the mirror and kicks, bans (reason, minutes or for good, with the IP when asked), server-mutes (minutes or until lifted) and moves through the service session, each waiting for the broadcast that shows it (`UserLeft`, `UserUpdated`); the known users (first page of 200) and the bans come with `ListUsers` and `ListBans`, an unban answers with the fresh ban list. Actions the server confirms to nobody (assign and take a group, delete a known user) use `ServerLink.RequestConfirmedAsync`: the action, then a `GetAvatar` of an avatar that does not exist, which the server always answers (lists are throttled to 2 a second); its answer shows the action was taken, and the action's refusal comes before it. Taking Admin from the last admin is refused by the server ("LastAdmin"), which the page shows. `GroupCommands` creates, changes, moves (one place up or down with `ReorderGroups`) and deletes groups, waiting for `GroupsChanged`; Admin and Server-Admin have fixed rights and Guest, Admin and Server-Admin cannot be deleted, as `GroupRules` says for the client. The rights are the client's `Perm_*` texts. Every action writes its own audit line (`UserKicked`, `UserBanned`, `UserUnbanned`, `UserMuted`, `UserUnmuted`, `UserMoved`, `KnownUserDeleted`, `GroupAssigned`, `GroupUnassigned`, `GroupCreated`, `GroupChanged`, `GroupDeleted`, `GroupsReordered`). Pages: `/servers/{id}/users` (online with an action panel, known users with their groups), `/servers/{id}/bans` and `/servers/{id}/groups`, all with the policy `ServerAccess`, linked from the server page. The connect time of a user online is the known user's last login (`UserInfo` has none).)

### Steps

1. Tests (red), then commands and pages (green). Full suite, rule-lint.

### Out of Scope

- Channel and link editing, chat reading (no chat content in the manager)

---

## Package M14: Invite link

**Goal:** Each server shows its `ovs://` link with public address, port and certificate fingerprint to copy.

**Dependencies:** M11

**Affected files:**
- `src/OVS.Manager/Servers/InviteLink.cs` (new): builds the link in the format of `src/OVS.Client/Net/JoinLink.cs` (Package 170)
- `src/OVS.Manager/Components/Pages/Servers/ServerPage.razor` (change)
- `tests/OVS.Tests/Manager/Servers/InviteLinkTests.cs` (new)

### Context

MA8. The client parses the link with `JoinLink`; the test parses the manager's link with it so that both stay in step.

### Acceptance Criteria

- [x] AC1: The link holds `OVS_MANAGER_PUBLIC_HOST` (or the request's host), the port and the pinned fingerprint, and `JoinLink` of the client reads it back to the same values.
- [x] AC2: A copy button puts it on the clipboard; the server password is never part of it.

### Tests (TDD)

1. `InviteLinkTests > "Build_PublicHostOrRequest_ClientReadsSame"` - AC1
2. `InviteLinkTests > "Box_ServerWithPassword_NeverContainsPassword"` (bUnit) - AC2

(as built: the client's `JoinLink` (Package 170) moved to `OVS.Shared/Net` (public), so the manager builds the very link the client reads. That link carries only host and port and refuses anything else, so the fingerprint is not in it: the box shows the pinned fingerprint (M11) beside the link, to compare with the client's prompt at the first connect, and "–" before the first connect. `InviteLink.For` takes `OVS_MANAGER_PUBLIC_HOST` (`ManagerOptions.PublicHost`), else the host of the page's address. `InviteBox` on the server page shows `ovs://host:port` and the web link `…?join=host:port`, each with a copy button (`navigator.clipboard`); the password is never part of either.)

### Steps

1. Tests (red), then link and button (green). Full suite, rule-lint.

### Out of Scope

- Links with expiry (dropped in Packages 151 to 170)

---

## Package M15: Backups

**Goal:** Backups are made by hand, by schedule and before every update, and can be downloaded, uploaded, restored and deleted.

**Dependencies:** M11

**Affected files:**
- `src/OVS.Manager/Control/BackupCommands.cs`, `BackupSchedule.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/Backups.razor` (new)
- `tests/OVS.Tests/Manager/Control/BackupCommandsTests.cs`, `BackupScheduleTests.cs` (new)

### Context

The protocol has create, list, download, upload, restore and delete of backups with the server's quota (Packages 74, 75). The schedule runs in the manager and calls `CreateBackup`.

### Acceptance Criteria

- [x] AC1: The list shows name, time and size; create, download (streamed through the manager), upload, restore (after a confirmation that names the effect: everyone is disconnected) and delete work.
- [x] AC2: A schedule per server (off, daily at a time, weekly) keeps the newest N scheduled backups and deletes older scheduled ones; manual ones stay.
- [x] AC3: `CreateBeforeUpdateAsync` makes a backup marked "vor Update <Version>" for M18; a full quota is reported as an error, not ignored.
- [x] AC4: Each action writes an audit line.

### Tests (TDD)

1. `BackupCommandsTests > "Backups_EachAction_DoneOnServer"` - AC1
2. `BackupScheduleTests > "Daily_KeepsNewestN_ManualStay"` (with `ManualTimeProvider` for manager and server) and `"LastDue_DailyAndWeekly_InServerZone"` - AC2
3. `BackupCommandsTests > "BeforeUpdate_QuotaFull_MarkedThenRefused"` - AC3
4. `BackupCommandsTests > "Actions_EachOne_AuditLine"` - AC4

(as built: `BackupCommands` lists (first page of 200), makes (`CreateBackup`, whose answer is the new list; the server allows one in 10 s per session, so the manager asks for no list before it), deletes, restores (the server disconnects everyone, the manager too, so the answer is the `Disconnected` with `Restoring`; pending requests of a link end with it), downloads chunk by chunk (`DownloadBackup`, 512 KB) into a stream and uploads chunk by chunk (`UploadBackupChunk`, answered by `UploadBackupAck` and at the end `BackupUploaded`). `GET /servers/{id}/backups/download?file=` streams a backup to the browser (policy `ServerAccess`). `BackupMark` rows (migration `AddBackupSchedules`) mark scheduled backups and those before an update ("vor Update <version>", `CreateBeforeUpdateAsync` for M18); a backup without a mark was made by hand and is never deleted by the schedule. `BackupSchedule` per server: off, daily at a time or weekly on a day, in the server's time zone, keeping 1 to 100 scheduled backups; `BackupScheduler` checks once a minute and makes the due ones, then deletes the oldest scheduled ones beyond the number. A full quota is the server's refusal ("at most 50 backups"). Each step writes `BackupCreated`, `BackupDeleted`, `BackupRestored`, `BackupDownloaded`, `BackupUploaded` or `BackupScheduleChanged`; the schedule as "system". The page `/servers/{id}/backups` (`ServerBackupPage`; `ServerBackups` clashes with the protocol's namespace) asks a second time before restore (naming that everyone is disconnected) and delete. In the tests the in-process server stays closed after a restore; the server's host restarts it in production.)

### Steps

1. Tests (red), then commands, schedule, page (green). Full suite, rule-lint.

### Out of Scope

- Backups of the volume by Docker (the protocol backup holds everything of the server)

---

## Package M16: Server log

**Goal:** The server log is read, searched and downloaded in the manager, and the console output is shown when the protocol is not available.

**Dependencies:** M11

**Affected files:**
- `src/OVS.Manager/Control/LogCommands.cs` (new): `ListLogs`, `ReadLog`, `SearchLogs`, `PrepareLogDownload`
- `src/OVS.Manager/Docker/ConsoleLog.cs` (new): container logs, followed live
- `src/OVS.Manager/Components/Pages/Servers/ServerLog.razor` (new)
- `tests/OVS.Tests/Manager/Control/LogCommandsTests.cs` (new)

### Context

Packages 81 and 82 give the server log over the protocol; the channel logs (chat) are left out of the manager like out of the telemetry (OpenTelemetry decision 3). The console carries the token line; the manager hides lines starting with "Server admin token:".

### Acceptance Criteria

- [x] AC1: The server log is listed per day, paged and searched; a hit opens its place.
- [x] AC2: A day's log is downloaded as a file.
- [x] AC3: For an outdated or unreachable server the console output is shown live instead, without token lines.
- [x] AC4: Channel logs are never listed or shown.

### Tests (TDD)

1. `LogCommandsTests > "List_ReadAndSearch_HitOpensPage"` - AC1
2. `LogCommandsTests > "Download_ServerLog_FileWithItsLines"` - AC2
3. `LogCommandsTests > "Console_NoSession_ShownWithoutToken"` (bUnit) - AC3
4. `LogCommandsTests > "ChannelLogs_Chat_NeverListed"` - AC4

(as built: `LogCommands` lists the server's log files (`ListLogs`, only `LogKind.Server` with ids under `server/`), reads a page (`ReadLog`, 1000 lines a page, the last page first), searches (`SearchLogs` with `LogKind.Server`) and opens a hit at its page, and downloads a file (`PrepareLogDownload` with that one file, then `DownloadLogChunk`; the server sends a single file as it is, several as a zip). A channel id is refused before any request ("Nur das Serverlog ist hier zu sehen."). `GET /servers/{id}/log/download?file=server/...` streams it (policy `ServerAccess`, audit line `LogDownloaded`). Without an open service session the page `/servers/{id}/log` follows the container's output instead (`IDockerEngine.FollowLogsAsync` over the proxy's `containers/{id}/logs`, the last 200 lines and then live) through `ConsoleLog`, which drops every line with "Server admin token:"; the page keeps the newest 500 lines.)

### Steps

1. Tests (red), then commands, console, page (green). Full suite, rule-lint.

### Out of Scope

- Telemetry logs (M24)

---

## Package M17: Versions

**Goal:** The overview shows each server's version, the newest release of its protocol and whether the manager itself is outdated.

**Dependencies:** M8, PLAN 193

**Affected files:**
- `src/OVS.Manager/Servers/ReleaseCatalog.cs` (new): GitHub releases (`deploy-<sha7>`, as `src/OVS.Client/Net/Updates/UpdateChecker.cs`), image label `org.openvoicespeak.protocol` from GHCR
- `src/OVS.Manager/Components/Pages/Servers/ServerList.razor` (change), `Components/Layout/ManagerUpdateBanner.razor` (new)
- `tests/OVS.Tests/Manager/Servers/ReleaseCatalogTests.cs` (new)

### Context

MA5, A206 of PLAN.md. `DDMMYY.stamp` is compared by release date, never as text. The catalog is fetched at most hourly and cached; without internet the overview says "Versionsprüfung nicht möglich".

### Acceptance Criteria

- [x] AC1: Each server shows its version and, if a newer release with the manager's protocol exists, "Update auf <Version> verfügbar".
- [x] AC2: Releases with a newer protocol than the manager's are never offered for servers; the banner says "Neue Version des Verwaltungstools verfügbar" with the compose command to update it.
- [x] AC3: Release order follows the release date (a test with versions whose text order differs).
- [x] AC4: Without network or with a GitHub error the last known catalog stays, with its age shown.

### Tests (TDD)

1. `ReleaseCatalogTests > "Newer_SameProtocol_Offered"` (`FakeHttp`) - AC1
2. `ReleaseCatalogTests > "NewerProtocol_NotOffered_ManagerBanner"` (bUnit) - AC2
3. `ReleaseCatalogTests > "Order_TextOrderDiffers_ByDate"` - AC3
4. `ReleaseCatalogTests > "Offline_LastCatalog_KeptWithAge"` (bUnit) - AC4

(as built: `ReleaseCatalog` (singleton and hosted service) fetches `releases?per_page=50` of the public repository at the start and then hourly; a release is "OpenVoiceSpeak <version>" with its `published_at` and the protocol from its notes ("Protokoll: N", PLAN 193; reading the image label from GHCR was not needed). Releases are ordered by `published_at` only. A server is offered the newest release of the manager's own protocol when it was published after the server's version (or the server's version is not in the catalog), shown as "Update auf <Version> verfügbar" in the overview. A CI-built manager older than the newest release gets the banner "Neue Version des Verwaltungstools verfügbar" with `docker compose pull ovs-manager && docker compose up -d ovs-manager` (`ManagerUpdateBanner` in the layout). A failed fetch keeps the last catalog and the banner says "Versionsprüfung nicht möglich, Stand vor N Stunden", or "Versionsprüfung nicht möglich." before any catalog. The tests never reach GitHub: `ManagerFactory.GitHub` answers with an empty list. Found on the way: `BackupScheduler.RunDueAsync` runs one at a time now, so the minutely check and a slow run never make the same backup twice.)

### Steps

1. Tests (red), then catalog, overview, banner (green). Full suite, rule-lint.

### Out of Scope

- The update itself (M18)

---

## Package M18: Update

**Goal:** A server is updated with a backup before and a health check after, rolled back on failure, singly, all at once or in a maintenance window.

**Dependencies:** M15, M17

**Affected files:**
- `src/OVS.Manager/Servers/ServerUpdate.cs`, `UpdateWindow.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/UpdateDialog.razor` (new)
- `tests/OVS.Tests/Manager/Servers/ServerUpdateTests.cs`, `UpdateWindowTests.cs` (new)

### Context

Data migrations of the server only go forward (`ServerData.CurrentVersion`), so a rollback is the old image plus the backup made before. Each server keeps a pinned tag, never `latest`.

### Acceptance Criteria

- [x] AC1: An update runs: pull the image, backup (M15), announcement (M19, when chosen), stop, recreate with the same variables, port, volume and labels and the new tag, start, health check (handshake within 60 s, version as expected).
- [x] AC2: A failed health check recreates the old version, restores the backup and reports "Update fehlgeschlagen, alte Version wiederhergestellt" with the reason.
- [x] AC3: "Alle aktualisieren" updates one server after the other and stops at the first failure.
- [x] AC4: A maintenance window per server (day and time) runs pending updates then; automatic updates are off for every server until the admin turns them on (D14).
- [x] AC5: Every step is shown live and writes audit lines.

### Tests (TDD)

1. `ServerUpdateTests > "Update_StepsInOrder_SameSpecNewTag"` (`FakeDockerEngine`) - AC1, AC5
2. `ServerUpdateTests > "Update_HealthFails_OldVersionAndBackupBack"` - AC2
3. `ServerUpdateTests > "UpdateAll_OneFails_StopsAtFirstFailure"` - AC3
4. `UpdateWindowTests > "Window_MondayFourToFive_OpenOnce"` and `"Window_PendingUpdate_DueOnlyWhenTurnedOn"` (with `ManualTimeProvider`) - AC4
5. `ServerUpdateTests > "Dialog_Update_StepsShownLive"` (bUnit) - AC5

(as built: `ServerUpdate` (scoped) runs pull, backup (`BackupCommands.CreateBeforeUpdateAsync`), stop and remove, create with the old container's variables (read by `InspectAsync`), port, network and labels and the new tag, start and the health check: the container of the new tag runs and the TLS handshake answers within `OVS_MANAGER_UPDATE_HEALTH_SECONDS` (60 s). The server sends no build version, so "version as expected" is the running container's tag. A failed check removes the new container, starts the old tag, waits for the service session and restores the backup; the result is "Update fehlgeschlagen, alte Version wiederhergestellt" with the reason (or without backup when the session never came). "Alle aktualisieren" in the overview (admin) goes by port and stops at the first failure. `UpdateWindow` (table `UpdateWindows`, migration `AddUpdateWindows`) holds day and time of a one-hour weekly window in the server's time zone with `AutoUpdate` off by default; `UpdateWindowRunner` checks each minute and runs a due update once per window as user "system". Each step goes live to the `UpdateDialog` on the server page and to the audit log (`ServerUpdateStep`, `ServerUpdated`, `ServerUpdateFailed`, `UpdateWindowChanged`). The announcement step (`UpdateStep.Announce`) comes with M19.)

### Steps

1. Tests (red), then update, window, dialog (green). Full suite, rule-lint.

### Out of Scope

- Updating the manager itself (banner of M17)

---

## Package M19: Announcements

**Goal:** Users on a server are told in its chat before a restart, a stop or an update, with an adjustable lead time.

**Dependencies:** M11

**Affected files:**
- `src/OVS.Manager/Control/Announcement.cs` (new): server chat as the service session (A203 of PLAN.md)
- `src/OVS.Manager/Servers/ServerLifecycle.cs` (change): optional lead time
- `tests/OVS.Tests/Manager/Control/AnnouncementTests.cs` (new)

### Context

The service session may send server chat; clients show its nickname ("Serververwaltung") although it is not in their list (PLAN 191 AC4). No new message type is needed.

### Acceptance Criteria

- [x] AC1: Restart, stop and update offer "sofort", "in 1 / 5 / 15 Minuten"; with a lead time the server chat gets "Der Server wird in N Minuten neu gestartet/gestoppt/aktualisiert." and a reminder one minute before.
- [x] AC2: A pending action can be cancelled; the chat then says "abgebrochen".
- [x] AC3: A free text can be sent to the server chat from the server page (audit line).

### Tests (TDD)

1. `AnnouncementTests > "Restart_FiveMinutes_ChatAndReminder"` (with `ManualTimeProvider`) - AC1
2. `AnnouncementTests > "Stop_Cancelled_ChatSaysCancelled"` - AC2
3. `AnnouncementTests > "FreeText_Sent_InChatAndAudit"` - AC3

(as built: `Announcement` (singleton) sends `SendChat` to the server chat over the service session and takes the server's echo to its sender as the proof it went out. A lead time of 1, 5 or 15 minutes ("sofort" runs at once as before) makes a pending action per server: the chat gets "Der Server wird in N Minuten neu gestartet/gestoppt/aktualisiert." at once and "in 1 Minute" a minute before; the waits go by absolute due times, so a clock that moved on does not lose them. The action then runs on its own in a fresh scope, so it does not end with the page. `ServerActions` offers the lead time for stop and restart, shows the pending action and "Abbrechen", which says "Der angekündigte Neustart wurde abgebrochen." (or Stoppen, Update) in the chat; the buttons are off while something is announced. `ServerLifecycle` stays unchanged: the lead time sits in front of it. The update dialog offers the lead time as well; an announced update runs pull and backup after the announcement, right before the stop, so the backup holds the last minutes too (M18 AC1 order otherwise). `AnnouncementBox` on the server page sends a free text with the audit line `ServerChatSent`. With no service session the countdown still runs and the action still comes; only the chat lines are missing.)

### Steps

1. Tests (red), then announcement and lifecycle (green). Full suite, rule-lint.

### Out of Scope

- A banner in the client (would need a new message; the chat is enough, A203)

---

## Package M20: Telemetry receiver

**Goal:** Every managed server sends its traces, metrics and logs to the manager over OTLP, and the manager stores them per server.

**Dependencies:** M7

**Affected files:**
- `src/OVS.Manager/Telemetry/OtlpReceiver.cs` (new): `POST /v1/traces`, `/v1/metrics`, `/v1/logs` with `application/x-protobuf` on port 4318, only in `ovs-managed`
- `src/OVS.Manager/Telemetry/Proto/` (new): the classes of `opentelemetry-proto` (pinned version), made by `Grpc.Tools` at build time
- `src/OVS.Manager/Telemetry/TelemetryStore.cs`, `MetricPoint.cs`, `LogRecordRow.cs`, `SpanRow.cs` (new)
- `tests/OVS.Tests/Manager/Telemetry/OtlpReceiverTests.cs` (new)

### Context

D6. The server exports with `http/protobuf` when the manager sets it (M7). The server is told apart by `service.instance.id`. A Kestrel endpoint on 4318 answers only the three paths; the port is not published (MA6).

### Acceptance Criteria

- [x] AC1: A real OVS server (in-process, with the manager's variables) sends metrics, logs and spans, and the store holds them under its server id.
- [x] AC2: Requests without a known `service.instance.id`, or from outside the internal network, are refused (403) and not stored.
- [x] AC3: Bodies over 4 MB and malformed protobuf are refused (413, 400) without breaking the receiver.
- [x] AC4: Port 4318 serves no page and the web port 6999 accepts no OTLP.

### Tests (TDD)

1. `OtlpReceiverTests > "RealServer_AllSignals_Stored"` - AC1
2. `OtlpReceiverTests > "Receive_UnknownInstance_403NothingStored"`, `"Receive_OutsideNetwork_403"` and `"IsInternal_Address_PrivateOnly"` - AC2
3. `OtlpReceiverTests > "Receive_TooLargeOrMalformed_413And400ThenStillWorks"` - AC3
4. `OtlpReceiverTests > "Ports_Separated_EachServesOnlyItsOwn"` - AC4

(as built: the receiver is the middleware `TelemetryReceiver` (named so beside the tests' own `OtlpReceiver`), placed before every other middleware: on port 4318 it answers only `POST /v1/traces`, `/v1/metrics` and `/v1/logs` (`application/x-protobuf`, gzip allowed, 4 MB after unpacking) and 404 or 405 for everything else, and on the web port those three paths are 404. A request must come from loopback or a private address (the Docker networks) and every resource in it must name a server of the manager by `service.instance.id`, else 403 and nothing is stored. The `.proto` files in `Telemetry/Proto` are the part of opentelemetry-proto v1.5.0 that is read (data messages, which have the fields of the export requests), compiled by `Grpc.Tools` 2.66.0 into internal classes with `Google.Protobuf` 3.28.0. `OtlpConversion` makes `SpanRow`, `LogRecordRow` and `MetricPoint` rows (times in Unix milliseconds, ids as hex, attributes as a JSON object) in `TelemetryStore`, a SQLite database of its own (`telemetry.db`, migration `AddTelemetryTables`). The image listens on 6999 and 4318 (`ASPNETCORE_HTTP_PORTS`); compose publishes 6999 only. Found on the way: the server's `AddService` made up a random `service.instance.id` that replaced the manager's; `autoGenerateServiceInstanceId: false` keeps the one of `OTEL_RESOURCE_ATTRIBUTES`. The AC1 test runs a real server run exporting to the tests' OTLP receiver and posts its bodies to the manager, which also proves the field numbers.)

### Steps

1. Tests (red), then protos, receiver, store (green). Full suite, rule-lint.

### Out of Scope

- Condensing and deleting (M21), any display (M22 ff.)

---

## Package M21: Telemetry storage and retention

**Goal:** Telemetry is condensed and deleted by age so that 100 servers fit in a bounded database.

**Dependencies:** M20

**Affected files:**
- `src/OVS.Manager/Telemetry/Rollup.cs`, `Retention.cs`, `DailyStats.cs` (new)
- `tests/OVS.Tests/Manager/Telemetry/RollupTests.cs`, `RetentionTests.cs` (new)

### Context

MA7. Counters arrive cumulative (OTLP default), so rollups store per-minute deltas; gauges store last, minimum and maximum. `DailyStats` keeps per server and day: connections, highest `ovs.users.daily` and `ovs.users.daily.new` (PLAN 192, A204), bytes in and out.

### Acceptance Criteria

- [x] AC1: Raw points become 1-minute values, and those become hourly values, both with correct deltas across a server restart (counter reset).
- [x] AC2: Data older than its retention is deleted once an hour; the times come from `OVS_MANAGER_RETENTION_*` with the defaults of MA7.
- [x] AC3: Daily statistics are written at the end of each day per server and never deleted.
- [x] AC4: The database file stays under a size set by `OVS_MANAGER_TELEMETRY_MAX_MB` (default 5000) by deleting the oldest raw data first, with a warning on the overview.

### Tests (TDD)

1. `RollupTests > "Counter_Restart_DeltaCorrect"` and `"Minute_ToHour_GaugeLastMinMax"` (with `ManualTimeProvider`) - AC1
2. `RetentionTests > "Old_Deleted_ByVariable"` (with `ManualTimeProvider`) - AC2
3. `RollupTests > "DailyStats_DayEnded_WrittenOnce"` - AC3
4. `RetentionTests > "OverSize_OldestRawFirst_Warning"` (bUnit for the warning) - AC4

(as built: `Rollup` (hosted, each minute) takes the raw points of finished minutes in time order and adds them to `MetricRollup` rows of level Minute: a gauge keeps last, lowest and highest, a sum or histogram the delta to the series' `CounterState` (another start time or a lower value is a restart, then the whole value counts); the raw points are deleted then, and every touched hour is made again from its minutes (level Hour). For each server and each finished day of the last week in its time zone without a row, `DailyStats` gets connections (`ovs.connections`), the highest `ovs.users.daily` and `ovs.users.daily.new`, and the voice and control bytes in and out (`ovs.direction`). `Retention` (hosted, each hour) deletes minute values after `OVS_MANAGER_RETENTION_MINUTE_DAYS` (7), hourly values after `OVS_MANAGER_RETENTION_HOUR_DAYS` (400), log lines after `OVS_MANAGER_RETENTION_LOG_DAYS` (30), spans after `OVS_MANAGER_RETENTION_TRACE_DAYS` (7) and raw points older than a day; daily statistics stay. While the used pages exceed `OVS_MANAGER_TELEMETRY_MAX_MB` (5000) it deletes the oldest day of raw data (spans, log lines, raw points, minute values) at a time; freed pages are reused, the file is not shrunk (no VACUUM). Then `TelemetryLimit.Reached` shows "Die Telemetriedaten haben die Grenze von N MB erreicht" on the overview. Migration `AddTelemetryRollups`.)

### Steps

1. Tests (red), then rollup, retention, daily stats (green). Full suite, rule-lint.

### Out of Scope

- Export to other backends (the server's own OTLP variables remain for that)

---

## Package M22: Live view

**Goal:** A server's page shows sessions, bandwidth, voice drops, CPU and RAM live.

**Dependencies:** M21

**Affected files:**
- `src/OVS.Manager/Components/Charts/LineChart.razor`, `Sparkline.razor`, `StatTile.razor` (new): own SVG charts (MA1)
- `src/OVS.Manager/Components/Pages/Servers/LiveView.razor` (new)
- `src/OVS.Manager/Telemetry/LiveFeed.cs` (new): newest points, Docker stats every 5 s
- `tests/OVS.Tests/Manager/Components/ChartTests.cs`, `tests/OVS.Tests/Manager/Telemetry/LiveFeedTests.cs` (new)

### Context

Metrics of Package 147: `ovs.sessions`, `ovs.voice.bytes`, `ovs.control.bytes`, `ovs.voice.drops`, `ovs.outbox.aborts`, `ovs.requests`, runtime metrics. CPU and RAM of the container come from Docker stats (M6 allow list).

### Acceptance Criteria

- [x] AC1: The page shows tiles (users online, kbit/s in and out, drops per minute, CPU %, RAM MB) and lines for the last 15 minutes, updated at least every 10 s without reload.
- [x] AC2: Charts have axes with units, a tooltip per point and read correctly in light and dark (M2 tokens).
- [x] AC3: A server without data for 2 minutes shows "keine Telemetrie" instead of a flat line.

### Tests (TDD)

1. `LiveFeedTests > "Page_NewPoint_ShownWithinTenSeconds"` (bUnit, `ManualTimeProvider`), `"Minutes_BytesAndDrops_KbitAndPerMinute"` and `ChartTests > "Tiles_Values_LabelValueUnit"` - AC1
2. `ChartTests > "Axes_UnitsAndTooltip_EachPoint"` and `"Colors_FromTokens_NoFixedColor"` - AC2
3. `LiveFeedTests > "NoData_TwoMinutes_NoTelemetry"` - AC3

(as built: `LiveView` on the server page reads `LiveFeed` at once and every 5 s (a `PeriodicTimer` on the `TimeProvider`) and redraws without a reload. The feed takes the minute values of M21 of the last 15 minutes (users from `ovs.sessions`, kbit/s in and out from `ovs.voice.bytes` and `ovs.control.bytes` by `ovs.direction`, drops per minute from `ovs.voice.drops`), the newest raw `ovs.sessions` for the users tile, and CPU and RAM from `IDockerEngine.StatsAsync` (Docker's stats without streaming, CPU as `docker stats` counts it, RAM without the page cache); CPU and RAM lines are kept by the page from its opening on. No data for 2 minutes shows "keine Telemetrie" and leaves out the telemetry lines. `LineChart` draws own SVG with a value axis in its unit (top 1, 2 or 5 times a power of ten), a clock axis every 5 minutes and a `<title>` tooltip per point, all colours as M2 tokens; `StatTile` shows a figure with label and unit. `ChartPoint` and `ChartSeries` are public (Razor makes components public; CA1515 is suppressed there with the reason). No `Sparkline`: the tiles and lines cover the page; add it with M23 if the overview needs one.)

### Steps

1. Tests (red), then charts, feed, page (green). Full suite, rule-lint.

### Out of Scope

- History (M23)

---

## Package M23: History and dashboard

**Goal:** Connections and users per day, bandwidth per day and month, and totals over all servers are shown as charts.

**Dependencies:** M21, PLAN 192

**Affected files:**
- `src/OVS.Manager/Components/Pages/Servers/History.razor`, `Components/Pages/Dashboard.razor` (new)
- `src/OVS.Manager/Components/Charts/BarChart.razor` (new)
- `tests/OVS.Tests/Manager/Components/HistoryTests.cs`, `DashboardTests.cs` (new)

### Context

From `DailyStats` and the hourly values of M21. The dashboard is the start page after login.

### Acceptance Criteria

- [x] AC1: Per server: connections per day, users per day (new and total), bandwidth per day and per month, peak users per day, for 7 days, 30 days, 12 months and a free range.
- [x] AC2: The dashboard shows users online over all visible servers, bandwidth now, ports in use (e.g. "37 / 100"), servers by status and updates available.
- [x] AC3: A manager's dashboard counts only assigned servers.
- [x] AC4: Every chart can be downloaded as CSV.

### Tests (TDD)

1. `HistoryTests > "History_Ranges_ValuesFromDailyStats"` - AC1
2. `DashboardTests > "Dashboard_TwoServers_TotalsPortsStatusUpdates"` - AC2
3. `DashboardTests > "Dashboard_Manager_OnlyAssignedCounted"` - AC3
4. `HistoryTests > "Csv_Download_LabelsAndValues"` - AC4

(as built: the page `/servers/{id}/history` (linked from the server page) shows four `BarChart`s, connections, users (total and new), most users online at once, and bandwidth in and out in MB, per day for 7 or 30 finished days or a free range, and per month for 12 months or a free range over 62 days. `HistoryQuery` reads `DailyStats` and the hourly `ovs.sessions` maxima (the peak), in the server's time zone; a day without data is 0, a month adds up its days (users: its highest day). The dashboard sits on top of the start page (the overview) rather than in a page of its own, as tiles: users online and bandwidth in / out now over the visible servers (`DashboardQuery`, the newest data of the last 2 minutes), the ports they take of 100, the servers by status, and the updates offered; a manager's visible servers are the assigned ones, so their dashboard counts only those. Every `BarChart` and `LineChart` has a "CSV" link with its data as a data URI (`ChartCsv`, invariant numbers), so no endpoint is needed.)

### Steps

1. Tests (red), then pages and bar chart (green). Full suite, rule-lint.

### Out of Scope

- Statistics per user (no fingerprints leave the server, A204)

---

## Package M24: Errors and stack traces

**Goal:** Errors of a server are listed with stack trace and error ID, and an error ID from a user finds its error and trace.

**Dependencies:** M21

**Affected files:**
- `src/OVS.Manager/Components/Pages/Servers/Errors.razor`, `Components/Pages/ErrorLookup.razor` (new)
- `src/OVS.Manager/Telemetry/ErrorQuery.cs` (new)
- `tests/OVS.Tests/Manager/Telemetry/ErrorQueryTests.cs` (new)

### Context

Package 143: the error ID is the trace id (146) and the log record of `RequestFailed` holds the stack trace (148). The ID is what a user copies from the client.

### Acceptance Criteria

- [x] AC1: The errors page lists log records of level Error and above with time, message and error ID, grouped by message with count; one opens with the whole stack trace.
- [x] AC2: The lookup takes an error ID and opens the error with its trace (spans with name, duration, status), across all servers the user may see.
- [x] AC3: The server list shows the number of errors of the last 24 hours per server.

### Tests (TDD)

1. `ErrorQueryTests > "Errors_SameTemplate_GroupedWithStackTrace"` (with bUnit) - AC1
2. `ErrorQueryTests > "Lookup_ErrorId_ErrorAndTrace"` and `"Lookup_OtherManagersServer_NotFound"` - AC2
3. `ErrorQueryTests > "Count_LastDay_InOverview"` (bUnit) - AC3

(as built: `ErrorQuery` reads the log lines of OTLP severity 17 and above. `/servers/{id}/errors` (linked from the server page and the overview's count) groups a server's newest 1000 by their template (`{OriginalFormat}`, else the text) with count, last time and error ID; a click opens `ErrorView` with the exception and the whole `exception.stacktrace`. `/errors` (in the navigation) takes an error ID in any case (hex, stored lower), finds the line by its trace id or its `ErrorId` attribute on a server the user may see (`ServerAccess.VisibleAsync`), and shows it with the spans of its trace (name, start, duration, status). The overview has a column "Fehler (24 h)" per server.)

### Steps

1. Tests (red), then query and pages (green). Full suite, rule-lint.

### Out of Scope

- Client errors (the client exports nothing, A161)

---

## Package M25: Resources

**Goal:** CPU, RAM and disk use of each server are shown, and the admin sets CPU and RAM limits.

**Dependencies:** M8

**Affected files:**
- `src/OVS.Manager/Docker/ResourceLimits.cs`, `VolumeUsage.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/Resources.razor` (new)
- `tests/OVS.Tests/Manager/Docker/ResourceLimitsTests.cs` (new)

### Context

Limits are container settings (`NanoCpus`, `Memory`); changing them recreates the container like an update without new tag. Volume size comes from `docker system df` data for the volume.

### Acceptance Criteria

- [x] AC1: Each server shows CPU and RAM now and over 24 hours, and the size of its volume.
- [x] AC2: The admin sets CPU (cores) and RAM (MB) limits at creation and later; a change recreates the container after a confirmation.
- [x] AC3: The overview shows the free disk of the host's Docker data root and warns below 10 %.

### Tests (TDD)

1. `ResourceLimitsTests > "Usage_Sampled_ShownWithVolumeSize"` and `"VolumeUsage_Answer_SizesByName"` - AC1
2. `ResourceLimitsTests > "Limits_Change_Recreated"` (bUnit) - AC2
3. `ResourceLimitsTests > "Disk_BelowTenPercent_Warning"` (bUnit) - AC3

(as built: `/servers/{id}/resources` (linked from the server page) shows CPU and RAM now (Docker stats) and the hourly peaks of the last 24 hours, and the volume's size. `UsageSampler` (hosted, each minute) writes every running container's CPU and RAM as the gauges `container.cpu.percent` and `container.memory.mb` into the telemetry store, so the rollup of M21 keeps them. The size comes from `GET /system/df?type=volume` (Docker.DotNet has no call for it; the proxy's allow list gets `system/df`), kept 10 minutes; a unix socket host gives no sizes. `ResourceLimits` (CPU cores 0.1 to 64, RAM 64 MB to 256 GB, empty for none) are stored on the server (migration `AddResourceLimits`), set at creation in the form and later by the admin: after a confirmation `ServerResources` stops, removes and makes the container again with the same variables and the new `NanoCpus` and `Memory`, starts it if it ran, and writes `ServerLimitsChanged`; updates (M18) keep the limits. The overview shows the free space of the disk of the manager's data folder (`IHostDisk`, `DataDirDisk`), which in the compose setup is a named volume in Docker's data root, and warns below 10 %. The page is `ResourceUse`, as `Resources` collides with `System.Resources`.)

### Steps

1. Tests (red), then limits, usage, page (green). Full suite, rule-lint.

### Out of Scope

- Network limits

---

## Package M26: E-mail and password reset

**Goal:** With SMTP set up, users with an e-mail reset their password by mail, and without it only the admin resets passwords.

**Dependencies:** M4

**Affected files:**
- `src/OVS.Manager/Mail/SmtpOptions.cs`, `MailSender.cs` (MailKit), `MailTemplates.cs` (new)
- `src/OVS.Manager/Components/Pages/ForgotPassword.razor`, `ResetPassword.razor`, `Components/Pages/Settings/MailSettings.razor` (new)
- `tests/OVS.Tests/Manager/Mail/PasswordResetTests.cs`, `MailSenderTests.cs` (new)

### Context

D5. `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM`, `SMTP_TLS` (none, starttls, tls) in the compose file. Mails in the user's language.

### Acceptance Criteria

- [x] AC1: "Passwort vergessen" takes a user name or e-mail and always shows the same text; a user with e-mail gets a link valid for 1 hour and usable once.
- [x] AC2: Without SMTP the link "Passwort vergessen" says "Bitte wende dich an den Administrator".
- [x] AC3: The admin sends a test mail from the settings; failures show the SMTP error.
- [x] AC4: A reset ends all sessions of the account and writes an audit line.

### Tests (TDD)

1. `PasswordResetTests > "Forgot_SameTextAlways_LinkOnceOneHour"` (fake sender) - AC1
2. `PasswordResetTests > "Forgot_NoSmtp_AskAdmin"` - AC2
3. `MailSenderTests > "TestMail_SentOrFailed_ResultShown"`, `"Sender_ClosedPort_SmtpError"` and `"Options_Variables_SecurityAndPort"` - AC3
4. `PasswordResetTests > "Reset_ByMail_SessionsEndedAudit"` - AC4

(as built: `SmtpOptions` reads `SMTP_HOST`, `SMTP_PORT` (default 25, 587 or 465 by `SMTP_TLS`), `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` and `SMTP_TLS` (none, starttls as default, tls); host and sender make it set up. `MailSender` sends plain text with MailKit 4.16.0 (4.8.0 has advisories; `System.Security.Cryptography.Pkcs` is pinned to 10.0.13 for the same reason) behind `IMailSender`, and turns every SMTP failure into a `MailSendException` with the server's text. `/forgot-password` (linked from the login) takes a user name or e-mail and always answers the same text; an account with an e-mail gets a mail in its language with a link to `/reset-password` holding an Identity reset token, valid for one hour (`TokenLifespan`) and good once (the reset changes the security stamp the token holds). Without SMTP the page says "Bitte wende dich an den Administrator". A reset ends every session of the account and writes `PasswordResetByMail`; the mail writes `PasswordResetMailSent`. `/settings/mail` (admin, in the navigation) shows the settings without the password and sends a test mail, showing the SMTP error on failure (`TestMailSent`). The one-hour expiry is checked as the option's value: Identity's token provider uses the system clock, which a test cannot move.)

### Steps

1. Tests (red), then mail, pages (green). Full suite, rule-lint.

### Out of Scope

- Notifications (M27)

---

## Package M27: Notifications

**Goal:** Users are told by e-mail or webhook when a server crashes, loops, runs full, runs out of disk or can be updated.

**Dependencies:** M8, M17, M22, M26

**Affected files:**
- `src/OVS.Manager/Notifications/NotificationRule.cs`, `Notifier.cs`, `EmailChannel.cs`, `WebhookChannel.cs`, `DiscordWebhookChannel.cs` (new): one class per channel (CODING-RULES rule 26)
- `src/OVS.Manager/Components/Pages/Settings/Notifications.razor` (new)
- `tests/OVS.Tests/Manager/Notifications/NotifierTests.cs` (new)

### Context

Events: crashed, restart loop, not reachable over 5 minutes, server full, voice drops over a threshold for 5 minutes, disk below 10 %, update available, update failed. Webhooks: Discord format and generic JSON (Fluxer takes Discord-compatible webhooks).

### Acceptance Criteria

- [x] AC1: Each user picks per event and channel (e-mail, webhook URL) for their servers; the admin also for manager events (disk, manager update).
- [x] AC2: An event is sent once and again only after it was resolved (no repeat while the server loops); a resolved event sends "wieder ok".
- [x] AC3: Thresholds (drops %, minutes) are set per server.
- [x] AC4: A failed send is retried 3 times and shown in the settings.

### Tests (TDD)

1. `NotifierTests > "Rules_PerEventAndChannel_EachUserTold"` - AC1
2. `NotifierTests > "Loop_SentOnce_ResolvedOk"` - AC2
3. `NotifierTests > "Drops_OverThreshold_FiveMinutes"` (with `ManualTimeProvider`) - AC3
4. `NotifierTests > "Send_Fails_RetriedAndShown"` (bUnit) and `"Webhook_PrivateAddress_Refused"` - AC4

(as built: `Notifier` (hosted, each minute) looks at every server of the status watcher and at the manager: crashed, restart loop, not reachable for 5 minutes, full (users without the service session at the server's limit), drops (every one of the server's last N finished minutes above its percent of the voice packets received, `ServerAlertSettings`, 5 % and 5 minutes by default), update available, and for the admin Docker's disk below 10 % and a new manager version; an update reports its failure or success (`ReportUpdateAsync`). `AlertState` keeps what was sent, so an event goes out once and "Wieder ok" once when it is over, also across a restart of the manager. `NotificationRule` holds per user, event and channel the target; a user gets a server's events only when they see the server, the manager's events only as admin. One class per channel behind `INotificationChannel`: `EmailChannel` (the account's address, M26), `WebhookChannel` (JSON with event, server, resolved, text, time) and `DiscordWebhookChannel` (`content`, also Fluxer). A send is tried 3 more times after 0.25, 0.5 and 1 s; the last error stays at the rule and shows in `/settings/notifications` (in the navigation), where each user ticks events per channel, enters the webhook URLs and sets the thresholds of their servers. Webhooks go through a client that connects only to public addresses (`PublicOnly`, checked on every connection) and follows no redirect, so no URL reaches the socket proxy or a server. Migration `AddNotifications`.)

### Steps

1. Tests (red), then rules, notifier, channels, page (green). Full suite, rule-lint.

### Out of Scope

- Push to phones

---

## Package M28: Two-factor login

**Goal:** Every user can turn on TOTP with recovery codes, and the admin can require it for everyone.

**Dependencies:** M4

**Affected files:**
- `src/OVS.Manager/Components/Pages/Account/TwoFactor.razor`, `LoginTwoFactor.razor` (new)
- `src/OVS.Manager/Accounts/TwoFactorPolicy.cs` (new)
- `tests/OVS.Tests/Manager/Accounts/TwoFactorTests.cs` (new)

### Context

Identity's authenticator support (RFC 6238); the QR code as SVG made in the manager (no external service).

### Acceptance Criteria

- [x] AC1: A user turns TOTP on by scanning a QR code and entering a code; 10 recovery codes are shown once.
- [x] AC2: Login then asks for a code or a recovery code; each recovery code works once.
- [x] AC3: With "2FA Pflicht" set by the admin, a user without TOTP must set it up right after login.
- [x] AC4: The admin can turn off a user's TOTP (audit line).

### Tests (TDD)

1. `TwoFactorTests > "Enable_CodeChecked_RecoveryCodesOnce"` - AC1
2. `TwoFactorTests > "Login_CodeOrRecovery_RecoveryOnce"` - AC2
3. `TwoFactorTests > "Required_NoTotp_SetupAfterLogin"` - AC3
4. `TwoFactorTests > "Admin_Disable_Audit"` - AC4

(as built: `/profile/two-factor` (linked from the profile) gives the user an Identity authenticator key, shows it as a QR code made in the manager (QRCoder 1.6.0, SVG, `otpauth://totp/...`) and as text, and turns TOTP on after a right code, showing 10 recovery codes once (`TwoFactorEnabled`). The login leads to `/login/two-factor` when the password was right; six digits are a TOTP code, anything else a recovery code, which works once; failures count for the address as in the login. "2FA Pflicht" is a `ManagerSetting` (migration `AddManagerSettings`) the admin switches on the account list (`TwoFactorPolicyChanged`); at sign-in a user without TOTP then gets a claim, and `TwoFactorGate` leads every page to the setup until it is done (the sign-in is refreshed then). The admin turns a manager's TOTP off on the account page, which also forgets its key (`TwoFactorDisabled`). Pages in `Components/Pages/Accounts` (`TwoFactorSetup`) and `Components/Pages` (`LoginTwoFactor`).)

### Steps

1. Tests (red), then pages and policy (green). Full suite, rule-lint.

### Out of Scope

- Passkeys

---

## Package M29: Adopt an existing server

**Goal:** An OVS container the manager did not create is taken over with its data and is then managed like the others.

**Dependencies:** M7, M11

**Affected files:**
- `src/OVS.Manager/Servers/ServerAdoption.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/AdoptServer.razor` (new)
- `tests/OVS.Tests/Manager/Servers/ServerAdoptionTests.cs` (new)

### Context

The allow list of M6 lets the manager inspect any container but act only on its own, so adoption needs a confirmation and works by recreating: a new managed container on the old volume, used as it is (D14). An old server admin loses that role at the first start with the manager's variable and keeps their other groups (PLAN 190, A202); the dialog says so before the adoption.

### Acceptance Criteria

- [x] AC1: The admin sees containers running an `openvoicespeak-server` image without the label and can adopt one after picking a port in range.
- [x] AC2: Adoption stops the old container, creates the managed one on the old volume as it is (no copy), with the manager's variables and identity, and removes the old container only after a successful health check; on a failed check the old container is started again.
- [x] AC3: The dialog names the current server admin, who becomes a normal user with their other groups; after the adoption the manager's service session connects.
- [x] AC4: An audit line names old and new container.

### Planned tests (TDD)

1. `ServerAdoptionTests > "Foreign_Listed_AdoptNeedsPort"` - AC1
2. `ServerAdoptionTests > "Adopt_VolumeKept_OldRemovedAfterHealth"` and `"Adopt_HealthFails_OldStartedAgain"` - AC2
3. `ServerAdoptionTests > "Confirm_OldServerAdmin_NamedThenReplaced"` - AC3
4. `ServerAdoptionTests > "Adopt_Audit"` - AC4

(as built: `/servers/adopt` (admin, linked in the navigation) lists every container of an `openvoicespeak-server` image without the manager's label (`IDockerEngine.ListForeignServersAsync`) with the lowest free port; `StopForeignAsync`, `StartForeignAsync` and `RemoveForeignAsync` act only on such a container. `ServerAdoption` takes name, user limit, time zone and password from the old variables and the version from the image tag, makes a new identity, and creates the managed container with the old volume or host folder as `/data` as it is (`ContainerSpec.DataSource`, `ManagedServer.DataSource`, migration `AddServerDataSource`). The health check shared with the update (`ServerHealth`, M18) decides: the old container is removed (`ServerAdopted`, "old -> new"), or the new one, its identity and row go and the old one starts again (`ServerAdoptFailed`). A container without anything at `/data` is refused. The dialog names the server admin by role, not by name: the manager has no session on a foreign server before it adopts it. Deleting an adopted server for good removes its volume (`ManagedServer.Volume`), never a host folder, and the resource page shows that volume's size.)

### Steps

1. Tests (red), then adoption and page (green). Full suite, rule-lint.

### Out of Scope

- Servers outside Docker

---

## Package M30: Clone a server

**Goal:** A new server starts with the channels, groups and settings of an existing one.

**Dependencies:** M7, M15

**Affected files:**
- `src/OVS.Manager/Servers/ServerClone.cs` (new)
- `src/OVS.Manager/Components/Pages/Servers/CreateServer.razor` (change): "aus Server kopieren"
- `tests/OVS.Tests/Manager/Servers/ServerCloneTests.cs` (new)

### Context

The backup of a server holds channels, groups, settings and users; a clone creates the new server (M7), restores a fresh backup of the source and then removes the known users and bans unless chosen.

### Acceptance Criteria

- [x] AC1: Creating a server can copy another server; channels, links, groups and settings arrive, users and bans only when ticked.
- [x] AC2: The clone has its own port, identity, certificate and name; the manager is its server admin.
- [x] AC3: Audit line with source and clone.

### Planned tests (TDD)

1. `ServerCloneTests > "Clone_ChannelsGroupsSettings_UsersOnlyTicked"` - AC1
2. `ServerCloneTests > "Clone_Restored_OwnNameCertificateAdmin"` - AC2
3. `ServerCloneTests > "Clone_Done_AuditNamesBoth"` - AC3

(as built: the form at `/servers/new` has "Aus Server kopieren" with "Nutzer mitkopieren" and "Banns mitkopieren"; the clone gets the source's version unless another is given. After `ServerFactory` made it, `ServerClone` waits up to the health timeout for its service session, makes and downloads a fresh backup of source and clone, and `CloneArchive` merges them: the source's data, logo and (with users) profile pictures, the clone's name in the settings and the clone's own certificate (a plain restore would carry the source's certificate and fingerprint). The source's users lose the Server-Admin group and the clone's own users join, so the manager's identity for the clone is its only server admin (A191). The archive is uploaded to the clone and restored there; the server checks it like every restore. Audit line `ServerCloned` "source (id) -> clone (id)". When the copy fails, the new server stays empty and the form says so.)

### Steps

1. Tests (red), then clone and form (green). Full suite, rule-lint.

### Out of Scope

- Templates stored apart from servers

---

## Package M31: API tokens

**Goal:** Users create tokens for a small REST API to list, start, stop, restart and update their servers.

**Dependencies:** M8, M18

**Affected files:**
- `src/OVS.Manager/Api/ApiToken.cs`, `ApiTokenHandler.cs`, `ServerEndpoints.cs` (new)
- `src/OVS.Manager/Components/Pages/Account/ApiTokens.razor` (new)
- `tests/OVS.Tests/Manager/Api/ServerEndpointsTests.cs` (new)

### Context

The API reuses the lifecycle and update services and the access policy of M10; it is for scripts, not for the UI.

### Acceptance Criteria

- [x] AC1: A user creates a token with name and expiry; it is shown once and stored only as a hash; it can be revoked.
- [x] AC2: `GET /api/servers`, `POST /api/servers/{id}/start|stop|restart|update` work with `Authorization: Bearer`, limited to the token owner's servers.
- [x] AC3: Every API call writes an audit line naming the token.
- [x] AC4: An OpenAPI document lies at `/api/openapi.json`.

### Planned tests (TDD)

1. `ServerEndpointsTests > "Token_Created_ShownOnceHashedRevoked"` - AC1
2. `ServerEndpointsTests > "Endpoints_OtherServer_Forbidden"` - AC2
3. `ServerEndpointsTests > "Call_WithToken_AuditNamesToken"` - AC3
4. `ServerEndpointsTests > "OpenApi_Get_ListsEveryPath"` - AC4

(as built: `/profile/api-tokens` (`ApiTokenPage`, linked from the profile) makes a token with a name and 1 to 3650 days; the secret `ovs_` plus 32 random bytes is shown once, the table `ApiTokens` (migration `AddApiTokens`) keeps only its SHA-256, and a token is revoked by removing it; a deleted account takes its tokens along. `ApiTokenHandler` is a second scheme used only by `/api`: a valid token of an account that is not locked signs in as that user with their roles. `ServerEndpoints` serve `GET /api/servers` (the servers the user sees, as the overview has them) and `POST /api/servers/{id}/start|stop|restart|update` (update without `?version=` takes the newest offer) behind the access policy of M10, so another server is 403 and no token 401. A filter writes `ApiCalled` with the token's name, method, path and status after each authorised call; the lifecycle and update write their own lines as the user. `/api/openapi.json` is a hand-written OpenAPI 3.0 document embedded in the manager, readable without a token; no OpenAPI package was added for five paths.)

### Steps

1. Tests (red), then tokens and endpoints (green). Full suite, rule-lint.

### Out of Scope

- Creating or deleting servers by API

---

## Package M32: Reverse proxy, image and release

**Goal:** The manager is released as its own image with the OVS release and runs behind any reverse proxy or the bundled Caddy.

**Dependencies:** M1

**Affected files:**
- `.github/workflows/release.yml` (change): builds and pushes `ghcr.io/rezondes/openvoicespeak-manager:latest` and `:<version>` (amd64, arm64)
- `deploy/manager/caddy/docker-compose.yml`, `deploy/manager/caddy/Caddyfile` (new)
- `src/OVS.Manager/Program.cs` (change): `OVS_MANAGER_BASE_URL`, forwarded headers only from `OVS_MANAGER_TRUSTED_PROXIES`
- `tests/OVS.Tests/Manager/ManagerComposeTests.cs` (change)

### Context

D10, D11. The user runs Nginx Proxy Manager, which needs WebSocket support on for SignalR; how to set up each proxy is written down in `README-MANAGER.md` (M34). Caddy gets certificates by itself.

### Acceptance Criteria

- [x] AC1: The Caddy compose file serves the manager over HTTPS on 443 for `OVS_MANAGER_DOMAIN`, with images pinned.
- [x] AC2: Forwarded headers are honoured only from the trusted proxies; links and cookies use `OVS_MANAGER_BASE_URL`.
- [ ] AC3 (manual): The release workflow pushes the manager image with the same version as the server.
- [ ] AC4 (manual): Behind Nginx Proxy Manager with WebSocket support on, login and live pages work.

### Planned tests (TDD)

1. `ManagerComposeTests > "Caddy_Https443_Pinned"` - AC1
2. `ManagerHostTests > "ForwardedHeaders_FromAddress_OnlyTrustedCount"`, `"ForwardedHeaders_NoTrustedProxy_Ignored"`, `"Options_InvalidProxySetting_Refused"` and `"BaseUrl_Set_UsedForLinksAndCookies"` - AC2
3. Manual checks AC3 and AC4.

(as built: `ManagerProxy` turns forwarded headers (For, Proto, Host; one proxy) on only when `OVS_MANAGER_TRUSTED_PROXIES` names addresses or CIDR networks, and only from those; an empty list never reaches ASP.NET, which would trust everyone then. `OVS_MANAGER_BASE_URL` (an http or https origin without a path) gives every request its scheme and host, so reset links, the invite link, redirects and the Secure flag of the cookies follow it; a sub path is not supported. Both are checked at the start with the variable's name in the error. `deploy/manager/caddy/docker-compose.yml` extends the manager's compose file (`-f ... -f ...`): Caddy 2.10.0 on 80 and 443 for `OVS_MANAGER_DOMAIN` with its certificate, the manager publishes no port then, and both share the network `edge` (10.255.250.0/29) where Caddy has the fixed address the manager trusts. The release workflow builds `Dockerfile.manager` for amd64 and arm64 in the image job and pushes `openvoicespeak-manager:latest` and `:<version>`; the manager's compose file still builds from the checkout until a release exists. AC3 and AC4 stay manual.)

### Steps

1. Tests (red), then compose, Caddyfile, options (green). Workflow; manual checks; full suite, rule-lint.

### Out of Scope

- TLS inside the manager (D10)
- The proxy documentation (M34)

---

## Package M33: Backup of the manager

**Goal:** The admin downloads the manager's database and keys as one encrypted archive and restores it on a new host.

**Dependencies:** M4, M7

**Affected files:**
- `src/OVS.Manager/Data/ManagerBackup.cs`, `ManagerRestore.cs` (new)
- `src/OVS.Manager/Components/Pages/Settings/ManagerBackupPage.razor` (new)
- `tests/OVS.Tests/Manager/Data/ManagerBackupTests.cs` (new)

### Context

D14. Without the Data Protection keys (`/data/keys`) the stored server identities cannot be decrypted, and the manager loses its server admin role on every server (MA4). Telemetry is not part of the archive; the servers' own data is in their volumes and their backups (M15).

### Acceptance Criteria

- [ ] AC1: The admin downloads one archive with the database (without telemetry tables), the keys and a manifest (manager version, time, server ids); it is encrypted with a password the admin types twice (AES-256-GCM, key from PBKDF2).
- [ ] AC2: On a manager without accounts, `/setup` offers "Aus Backup wiederherstellen"; with the right password the accounts, assignments, servers and identities come back, and the manager finds the containers by their labels (M8 AC5).
- [ ] AC3: A wrong password, a damaged archive or one of a newer manager version is refused with a text and changes nothing.
- [ ] AC4: A scheduled backup (off, daily, weekly) keeps the newest N archives in `/data/backups`, encrypted with a password from `OVS_MANAGER_BACKUP_PASSWORD`; without it the schedule cannot be turned on.
- [ ] AC5: Download, restore and schedule changes write audit lines.

### Planned tests (TDD)

1. `ManagerBackupTests > "Download_EncryptedWithManifestNoTelemetry"` - AC1
2. `ManagerBackupTests > "Restore_FreshManager_EverythingBack"` - AC2
3. `ManagerBackupTests > "Restore_WrongPasswordDamagedOrNewer_Refused"` - AC3
4. `ManagerBackupTests > "Schedule_KeepsNewestN_NeedsPassword"` (with `ManualTimeProvider`) - AC4
5. `ManagerBackupTests > "Actions_AuditLines"` - AC5

### Steps

1. Tests (red), then backup, restore, schedule, page (green). Full suite, rule-lint.

### Out of Scope

- Telemetry in the archive, backups to external storage

---

## Package M34: Documentation

**Goal:** `README-MANAGER.md` documents everything about the manager, as `README.md` does for server and client and `docs/AUDIO.md` for the audio.

**Dependencies:** M1 to M33

**Affected files:**
- `README-MANAGER.md` (new), German like `README.md`
- `README.md` (change): a short section "Verwaltungstool" that links to `README-MANAGER.md`
- `tests/OVS.Tests/Manager/ManagerDocsTests.cs` (new)

### Context

D13. `README.md` explains setup, ports, variables, logs, telemetry, backup and update of the server and the client; `docs/AUDIO.md` explains how a part works with the files behind it. `README-MANAGER.md` does both for the manager.

### Acceptance Criteria

- [ ] AC1: `README-MANAGER.md` covers: what the manager is; setup with Docker Compose (port 6999, the network `ovs-managed`, the socket proxy and why); every environment variable with default and meaning; the first admin; roles and assignments; creating, starting, stopping, deleting and restoring servers and the port range 7000 to 7099; the manager as server admin and how an owner works through it; settings, users, groups, bans, invite link, backups and server log; versions, updates, rollback and maintenance windows; telemetry (what is received, retention, live view, history, errors and error IDs); resources and limits; e-mail, password reset and notifications; two-factor login; adopting and cloning servers; the API with its tokens; backup and restore of the manager itself (M33); HTTPS behind Nginx Proxy Manager, other proxies or the bundled Caddy; updating the manager itself; troubleshooting.
- [ ] AC2: Every environment variable the manager reads appears in `README-MANAGER.md`, and every variable named there is read by the manager.
- [ ] AC3: Every compose file and path named in `README-MANAGER.md` exists.
- [ ] AC4: `README.md` links to `README-MANAGER.md` and no longer describes the manager itself.
- [ ] AC5 (manual): Someone sets up the manager on a fresh Docker host only by `README-MANAGER.md`, creates a server and connects to it with a client.

### Planned tests (TDD)

1. `ManagerDocsTests > "Readme_CoversEveryTopic"` (a heading per topic of AC1) - AC1
2. `ManagerDocsTests > "Variables_ReadmeAndCodeMatch"` (variables found in `src/OVS.Manager` against the README) - AC2
3. `ManagerDocsTests > "NamedFiles_Exist"` - AC3
4. `ManagerDocsTests > "MainReadme_LinksManagerReadme"` - AC4
5. Manual check AC5.

### Steps

1. Tests (red), then write `README-MANAGER.md` and the link in `README.md` (green).
2. Manual check on a fresh host; full suite, rule-lint.

### Out of Scope

- A website page for the manager
