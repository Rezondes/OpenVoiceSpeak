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

- [ ] AC1: Each entry holds time (UTC, shown in local time), user, action, target (account or server) and a short detail; it is never changed afterwards.
- [ ] AC2: Logins, failed logins and every action of M4 write an entry.
- [ ] AC3: The admin filters by user, server, action and time span and exports the result as CSV; a manager sees only entries of their assigned servers.
- [ ] AC4: With `OVS_MANAGER_AUDIT_DAYS` older entries are deleted once a day.

### Planned tests (TDD)

1. `AuditLogTests > "Write_Entry_AllFieldsReadOnly"` - AC1
2. `AuditLogTests > "Accounts_Actions_Recorded"` and `"Login_Failed_Recorded"` - AC2
3. `AuditLogTests > "Filter_ByServerAndUser"`, `"Export_Csv"`, `"Manager_SeesOnlyAssigned"` - AC3
4. `AuditLogTests > "Retention_Set_OldEntriesDeleted"` - AC4

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

- [ ] AC1: The compose file gives the proxy the socket read-only, publishes no port of it, and its allow list contains exactly the calls named above.
- [ ] AC2: `DockerEngine` lists only containers with `ovs.managed=true`; any call for another container fails without reaching Docker.
- [ ] AC3: Every container the manager creates carries `ovs.managed`, `ovs.server-id`, `ovs.port` and `ovs.version`, and its volume is named `ovs-<id>-data`.
- [ ] AC4: An unreachable proxy shows "Docker nicht erreichbar" on the overview and in `/healthz` (status 503), and the rest of the manager keeps working.
- [ ] AC5 (manual): On a Docker host the manager lists, starts and stops a test container through the proxy, and a call outside the allow list is refused by the proxy (403).

### Planned tests (TDD)

1. `ManagerComposeTests > "SocketProxy_ReadOnlyNoPortAllowList"` - AC1
2. `DockerEngineTests > "List_OnlyManaged"` and `"Call_ForeignContainer_RefusedBeforeDocker"` - AC2
3. `DockerEngineTests > "Create_LabelsAndVolumeName"` - AC3
4. `DockerEngineTests > "ProxyDown_HealthDegraded"` - AC4
5. Manual check AC5.

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

- [ ] AC1: The form takes name, max users, password (optional), version (default: the manager's own, MA5), time zone (default: the manager's `TZ`, D14) and port (default: the lowest free one); a port outside 7000 to 7099, taken by another server or in use on the host is refused with a text.
- [ ] AC2: With 100 servers the button is off with "Alle 100 Ports sind belegt".
- [ ] AC3: The created container has exactly the variables, mapping, labels, volume, network and restart policy above; the identity's private key is stored encrypted and never shown.
- [ ] AC4: Only the admin reaches the page; the creation writes an audit line.
- [ ] AC5 (manual): A created server starts, a client connects, and the server log shows "Server admin set from OVS_SERVER_ADMIN_FINGERPRINT" and no token.

### Planned tests (TDD)

1. `PortPoolTests > "Next_LowestFree"`, `"Take_OutOfRangeOrTaken_Refused"`, `"Full_HundredServers_NoneFree"` - AC1, AC2
2. `ServerFactoryTests > "Create_ContainerSpec_ExactVariablesAndMappings"` (on `FakeDockerEngine`) - AC3
3. `ServerFactoryTests > "Identity_StoredEncrypted"` - AC3
4. `ServerFactoryTests > "Create_Manager_Forbidden"` and `"Create_AuditLine"` - AC4
5. Manual check AC5.

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

### Context

Status comes from Docker events and inspect (running, restarting, exited with code) and from a TLS connect to the port (the server answers). The server ends cleanly on SIGTERM and tells clients "ServerShutdown" (`Program.cs`, Package 5); a stop uses a timeout of 15 s.

### Acceptance Criteria

- [ ] AC1: The overview shows per server name, status ("läuft", "startet", "gestoppt", "abgestürzt", "Neustart-Schleife", "nicht erreichbar"), version, port, users online and uptime, and updates live without reload.
- [ ] AC2: Start, stop and restart work from the overview and the server page and write audit lines; a running action disables the buttons of that server.
- [ ] AC3: More than 3 restarts within 10 minutes show "Neustart-Schleife" with the last exit code.
- [ ] AC4: "läuft" needs the container running and a TLS handshake on the port; a running container without answer is "nicht erreichbar".
- [ ] AC5: The manager's own restart loses no status: containers are found again by their labels (M6 AC3).

### Planned tests (TDD)

1. `StatusWatcherTests > "Events_StatusMapped"` and `"List_LiveUpdate"` (bUnit) - AC1
2. `ServerLifecycleTests > "StartStopRestart_AuditAndButtonsDisabled"` - AC2
3. `StatusWatcherTests > "FourRestartsInTenMinutes_RestartLoop"` (with `ManualTimeProvider`) - AC3
4. `StatusWatcherTests > "Running_NoHandshake_NotReachable"` - AC4
5. `StatusWatcherTests > "ManagerRestart_ServersFoundByLabels"` - AC5

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

- [ ] AC1: Delete is offered only to the admin and needs the exact server name typed; the server is stopped and listed in the trash with its end date.
- [ ] AC2: Restoring within 7 days brings back the same container, volume, port and assignments.
- [ ] AC3: After 7 days the daily sweep removes container and volume and frees the port; "jetzt endgültig löschen" does it at once after a second confirmation.
- [ ] AC4: Every step writes an audit line.

### Planned tests (TDD)

1. `ServerDeletionTests > "Delete_NameWrong_Refused"` and `"Delete_Admin_StoppedInTrash"` - AC1
2. `ServerDeletionTests > "Restore_WithinPeriod_SameServer"` - AC2
3. `ServerDeletionTests > "Sweep_AfterSevenDays_RemovedPortFree"` and `"DeleteNow_Confirmed_Removed"` - AC3
4. `ServerDeletionTests > "Steps_AuditLines"` - AC4

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

- [ ] AC1: The admin assigns any number of servers to a manager and any number of managers to a server, on the account and on the server page.
- [ ] AC2: A manager's overview lists only assigned servers; any page, action or API call for another server answers 403 (also by typing the URL).
- [ ] AC3: A manager may start, stop, restart, update and operate assigned servers but never create, delete or assign.
- [ ] AC4: Every change of an assignment writes an audit line.

### Planned tests (TDD)

1. `ServerAccessTests > "Assign_BothDirections"` - AC1
2. `ServerAccessTests > "Manager_OtherServer_Forbidden"` (pages and endpoints) - AC2
3. `ServerAccessTests > "Manager_CreateDeleteAssign_Forbidden"` - AC3
4. `ServerAccessTests > "Assign_AuditLine"` - AC4

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

- [ ] AC1: For every running server a service session is open within 10 s of its start and reconnects with backoff after a loss.
- [ ] AC2: `VersionMismatch` marks the server "veraltet" with its protocol, and no protocol page of it is offered; Docker actions stay.
- [ ] AC3: The certificate fingerprint is stored at the first connect; a later different certificate stops the connection and shows "Zertifikat geändert" until the admin accepts the new one (audit line).
- [ ] AC4: The mirror holds users online, channels, groups and settings, live from the server's messages.
- [ ] AC5: No user of the server sees the manager in a channel or the user list.

### Planned tests (TDD)

1. `ServerConnectionsTests > "Running_ConnectedAndReconnected"` (in-process server) - AC1
2. `ServerConnectionsTests > "VersionMismatch_Outdated"` - AC2
3. `ServerConnectionsTests > "Certificate_Changed_StoppedUntilAccepted"` - AC3
4. `ServerConnectionsTests > "Mirror_FollowsBroadcasts"` - AC4
5. `ServerConnectionsTests > "Service_InvisibleToClients"` - AC5

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

- [ ] AC1: The page shows the current values from the mirror and saves changes with `UpdateServerSettings`; a refused value shows the server's error text.
- [ ] AC2: Changes from a client admin appear on an open page without reload.
- [ ] AC3: An icon upload is checked for size and type like in the client and set with `SetServerIcon`.
- [ ] AC4: Each save writes an audit line with the changed fields (the password only as "geändert").

### Planned tests (TDD)

1. `SettingsCommandsTests > "Save_Values_SentAndRefusalShown"` - AC1
2. `SettingsCommandsTests > "ExternalChange_PageUpdated"` - AC2
3. `SettingsCommandsTests > "Icon_TooLarge_Refused"` - AC3
4. `SettingsCommandsTests > "Save_AuditFieldsPasswordHidden"` - AC4

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

- [ ] AC1: The online list shows nickname, channel, groups, muted state and connect time live; kick, ban (with reason and duration), server mute (with duration) and move work from it.
- [ ] AC2: The known users list shows first and last login, online and speech time and lets the manager assign and remove groups, among them Admin.
- [ ] AC3: The ban list shows reason, end and who banned; unban works.
- [ ] AC4: Groups are created, changed, ordered and deleted; Guest, Admin and Server-Admin show as fixed like in the client.
- [ ] AC5: Every action writes an audit line in the manager besides the server's own.

### Planned tests (TDD)

1. `ModerationCommandsTests > "Online_LiveAndActions"` - AC1
2. `GroupCommandsTests > "KnownUser_AssignAdmin_Sent"` - AC2
3. `ModerationCommandsTests > "Bans_ListAndUnban"` - AC3
4. `GroupCommandsTests > "FixedGroups_NotEditable"` and `"Groups_CreateReorderDelete"` - AC4
5. `ModerationCommandsTests > "Actions_AuditLines"` - AC5

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

- [ ] AC1: The link holds `OVS_MANAGER_PUBLIC_HOST` (or the request's host), the port and the pinned fingerprint, and `JoinLink` of the client reads it back to the same values.
- [ ] AC2: A copy button puts it on the clipboard; the server password is never part of it.

### Planned tests (TDD)

1. `InviteLinkTests > "Build_ClientJoinLinkReadsSame"` - AC1
2. `InviteLinkTests > "Build_NeverContainsPassword"` - AC2

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

- [ ] AC1: The list shows name, time and size; create, download (streamed through the manager), upload, restore (after a confirmation that names the effect: everyone is disconnected) and delete work.
- [ ] AC2: A schedule per server (off, daily at a time, weekly) keeps the newest N scheduled backups and deletes older scheduled ones; manual ones stay.
- [ ] AC3: `CreateBeforeUpdateAsync` makes a backup marked "vor Update <Version>" for M18; a full quota is reported as an error, not ignored.
- [ ] AC4: Each action writes an audit line.

### Planned tests (TDD)

1. `BackupCommandsTests > "List_Create_Download_Upload_Restore_Delete"` - AC1
2. `BackupScheduleTests > "Daily_KeepsNewestN_ManualStay"` (with `ManualTimeProvider`) - AC2
3. `BackupCommandsTests > "BeforeUpdate_Marked"` and `"QuotaFull_Error"` - AC3
4. `BackupCommandsTests > "Actions_AuditLines"` - AC4

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

- [ ] AC1: The server log is listed per day, paged and searched; a hit opens its place.
- [ ] AC2: A day's log is downloaded as a file.
- [ ] AC3: For an outdated or unreachable server the console output is shown live instead, without token lines.
- [ ] AC4: Channel logs are never listed or shown.

### Planned tests (TDD)

1. `LogCommandsTests > "List_Read_Search"` - AC1
2. `LogCommandsTests > "Download_Day_File"` - AC2
3. `LogCommandsTests > "Console_Outdated_ShownWithoutToken"` - AC3
4. `LogCommandsTests > "ChannelLogs_NeverListed"` - AC4

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

- [ ] AC1: Each server shows its version and, if a newer release with the manager's protocol exists, "Update auf <Version> verfügbar".
- [ ] AC2: Releases with a newer protocol than the manager's are never offered for servers; the banner says "Neue Version des Verwaltungstools verfügbar" with the compose command to update it.
- [ ] AC3: Release order follows the release date (a test with versions whose text order differs).
- [ ] AC4: Without network or with a GitHub error the last known catalog stays, with its age shown.

### Planned tests (TDD)

1. `ReleaseCatalogTests > "Newer_SameProtocol_Offered"` (`FakeHttp`) - AC1
2. `ReleaseCatalogTests > "NewerProtocol_NotOffered_ManagerBanner"` - AC2
3. `ReleaseCatalogTests > "Order_ByDateNotText"` - AC3
4. `ReleaseCatalogTests > "Offline_LastCatalogWithAge"` - AC4

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

- [ ] AC1: An update runs: pull the image, backup (M15), announcement (M19, when chosen), stop, recreate with the same variables, port, volume and labels and the new tag, start, health check (handshake within 60 s, version as expected).
- [ ] AC2: A failed health check recreates the old version, restores the backup and reports "Update fehlgeschlagen, alte Version wiederhergestellt" with the reason.
- [ ] AC3: "Alle aktualisieren" updates one server after the other and stops at the first failure.
- [ ] AC4: A maintenance window per server (day and time) runs pending updates then; automatic updates are off for every server until the admin turns them on (D14).
- [ ] AC5: Every step is shown live and writes audit lines.

### Planned tests (TDD)

1. `ServerUpdateTests > "Update_StepsInOrder_SameSpecNewTag"` (`FakeDockerEngine`) - AC1
2. `ServerUpdateTests > "HealthFails_OldVersionAndBackupBack"` - AC2
3. `ServerUpdateTests > "All_StopsAtFirstFailure"` - AC3
4. `UpdateWindowTests > "Window_PendingUpdateRuns"` (with `ManualTimeProvider`) and `"AutoUpdate_DefaultOff"` - AC4
5. `ServerUpdateTests > "Steps_LiveAndAudit"` - AC5

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

- [ ] AC1: Restart, stop and update offer "sofort", "in 1 / 5 / 15 Minuten"; with a lead time the server chat gets "Der Server wird in N Minuten neu gestartet/gestoppt/aktualisiert." and a reminder one minute before.
- [ ] AC2: A pending action can be cancelled; the chat then says "abgebrochen".
- [ ] AC3: A free text can be sent to the server chat from the server page (audit line).

### Planned tests (TDD)

1. `AnnouncementTests > "Restart_FiveMinutes_ChatAndReminder"` (with `ManualTimeProvider`) - AC1
2. `AnnouncementTests > "Cancel_ChatSaysCancelled"` - AC2
3. `AnnouncementTests > "FreeText_SentAndAudited"` - AC3

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

- [ ] AC1: A real OVS server (in-process, with the manager's variables) sends metrics, logs and spans, and the store holds them under its server id.
- [ ] AC2: Requests without a known `service.instance.id`, or from outside the internal network, are refused (403) and not stored.
- [ ] AC3: Bodies over 4 MB and malformed protobuf are refused (413, 400) without breaking the receiver.
- [ ] AC4: Port 4318 serves no page and the web port 6999 accepts no OTLP.

### Planned tests (TDD)

1. `OtlpReceiverTests > "RealServer_AllSignalsStored"` - AC1
2. `OtlpReceiverTests > "UnknownInstance_Refused"` and `"OutsideNetwork_Refused"` - AC2
3. `OtlpReceiverTests > "TooLarge_413"` and `"Malformed_400"` - AC3
4. `OtlpReceiverTests > "Ports_Separated"` - AC4

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

- [ ] AC1: Raw points become 1-minute values, and those become hourly values, both with correct deltas across a server restart (counter reset).
- [ ] AC2: Data older than its retention is deleted once an hour; the times come from `OVS_MANAGER_RETENTION_*` with the defaults of MA7.
- [ ] AC3: Daily statistics are written at the end of each day per server and never deleted.
- [ ] AC4: The database file stays under a size set by `OVS_MANAGER_TELEMETRY_MAX_MB` (default 5000) by deleting the oldest raw data first, with a warning on the overview.

### Planned tests (TDD)

1. `RollupTests > "Counter_Restart_DeltaCorrect"` and `"Minute_ToHour"` - AC1
2. `RetentionTests > "Old_Deleted_ByVariable"` (with `ManualTimeProvider`) - AC2
3. `RollupTests > "DayEnd_DailyStatsWritten"` - AC3
4. `RetentionTests > "OverSize_OldestRawFirst_Warning"` - AC4

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

- [ ] AC1: The page shows tiles (users online, kbit/s in and out, drops per minute, CPU %, RAM MB) and lines for the last 15 minutes, updated at least every 10 s without reload.
- [ ] AC2: Charts have axes with units, a tooltip per point and read correctly in light and dark (M2 tokens).
- [ ] AC3: A server without data for 2 minutes shows "keine Telemetrie" instead of a flat line.

### Planned tests (TDD)

1. `LiveFeedTests > "Points_PushedWithinTenSeconds"` and `ChartTests > "Tiles_Values"` - AC1
2. `ChartTests > "Axes_UnitsAndTooltip"` and `"Colors_FromTokens"` - AC2
3. `LiveFeedTests > "NoData_TwoMinutes_NoTelemetry"` - AC3

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

- [ ] AC1: Per server: connections per day, users per day (new and total), bandwidth per day and per month, peak users per day, for 7 days, 30 days, 12 months and a free range.
- [ ] AC2: The dashboard shows users online over all visible servers, bandwidth now, ports in use (e.g. "37 / 100"), servers by status and updates available.
- [ ] AC3: A manager's dashboard counts only assigned servers.
- [ ] AC4: Every chart can be downloaded as CSV.

### Planned tests (TDD)

1. `HistoryTests > "Ranges_ValuesFromDailyStats"` - AC1
2. `DashboardTests > "Totals_PortsStatusUpdates"` - AC2
3. `DashboardTests > "Manager_OnlyAssigned"` - AC3
4. `HistoryTests > "Csv_Download"` - AC4

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

- [ ] AC1: The errors page lists log records of level Error and above with time, message and error ID, grouped by message with count; one opens with the whole stack trace.
- [ ] AC2: The lookup takes an error ID and opens the error with its trace (spans with name, duration, status), across all servers the user may see.
- [ ] AC3: The server list shows the number of errors of the last 24 hours per server.

### Planned tests (TDD)

1. `ErrorQueryTests > "Errors_GroupedWithStackTrace"` - AC1
2. `ErrorQueryTests > "Lookup_ErrorId_ErrorAndTrace"` and `"Lookup_OtherManagersServer_NotFound"` - AC2
3. `ErrorQueryTests > "Count_LastDay"` - AC3

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

- [ ] AC1: Each server shows CPU and RAM now and over 24 hours, and the size of its volume.
- [ ] AC2: The admin sets CPU (cores) and RAM (MB) limits at creation and later; a change recreates the container after a confirmation.
- [ ] AC3: The overview shows the free disk of the host's Docker data root and warns below 10 %.

### Planned tests (TDD)

1. `ResourceLimitsTests > "Usage_ShownWithVolumeSize"` - AC1
2. `ResourceLimitsTests > "Limits_Change_Recreated"` - AC2
3. `ResourceLimitsTests > "Disk_BelowTenPercent_Warning"` - AC3

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

- [ ] AC1: "Passwort vergessen" takes a user name or e-mail and always shows the same text; a user with e-mail gets a link valid for 1 hour and usable once.
- [ ] AC2: Without SMTP the link "Passwort vergessen" says "Bitte wende dich an den Administrator".
- [ ] AC3: The admin sends a test mail from the settings; failures show the SMTP error.
- [ ] AC4: A reset ends all sessions of the account and writes an audit line.

### Planned tests (TDD)

1. `PasswordResetTests > "Forgot_SameTextAlways_LinkOnceOneHour"` (fake sender) - AC1
2. `PasswordResetTests > "NoSmtp_AskAdmin"` - AC2
3. `MailSenderTests > "TestMail_ErrorShown"` - AC3
4. `PasswordResetTests > "Reset_SessionsEndedAudit"` - AC4

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

- [ ] AC1: Each user picks per event and channel (e-mail, webhook URL) for their servers; the admin also for manager events (disk, manager update).
- [ ] AC2: An event is sent once and again only after it was resolved (no repeat while the server loops); a resolved event sends "wieder ok".
- [ ] AC3: Thresholds (drops %, minutes) are set per server.
- [ ] AC4: A failed send is retried 3 times and shown in the settings.

### Planned tests (TDD)

1. `NotifierTests > "Pick_PerEventAndChannel"` - AC1
2. `NotifierTests > "Loop_SentOnce_ResolvedOk"` - AC2
3. `NotifierTests > "Drops_OverThreshold_FiveMinutes"` (with `ManualTimeProvider`) - AC3
4. `NotifierTests > "SendFails_RetriedAndShown"` - AC4

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

- [ ] AC1: A user turns TOTP on by scanning a QR code and entering a code; 10 recovery codes are shown once.
- [ ] AC2: Login then asks for a code or a recovery code; each recovery code works once.
- [ ] AC3: With "2FA Pflicht" set by the admin, a user without TOTP must set it up right after login.
- [ ] AC4: The admin can turn off a user's TOTP (audit line).

### Planned tests (TDD)

1. `TwoFactorTests > "Enable_CodeChecked_RecoveryCodesOnce"` - AC1
2. `TwoFactorTests > "Login_CodeOrRecoveryOnce"` - AC2
3. `TwoFactorTests > "Required_SetupAfterLogin"` - AC3
4. `TwoFactorTests > "Admin_Disable_Audit"` - AC4

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

- [ ] AC1: The admin sees containers running an `openvoicespeak-server` image without the label and can adopt one after picking a port in range.
- [ ] AC2: Adoption stops the old container, creates the managed one on the old volume as it is (no copy), with the manager's variables and identity, and removes the old container only after a successful health check; on a failed check the old container is started again.
- [ ] AC3: The dialog names the current server admin, who becomes a normal user with their other groups; after the adoption the manager's service session connects.
- [ ] AC4: An audit line names old and new container.

### Planned tests (TDD)

1. `ServerAdoptionTests > "Foreign_Listed_AdoptNeedsPort"` - AC1
2. `ServerAdoptionTests > "Adopt_VolumeKept_OldRemovedAfterHealth"` and `"HealthFails_OldStartedAgain"` - AC2
3. `ServerAdoptionTests > "OldServerAdmin_NamedThenReplaced"` - AC3
4. `ServerAdoptionTests > "Adopt_Audit"` - AC4

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

- [ ] AC1: Creating a server can copy another server; channels, links, groups and settings arrive, users and bans only when ticked.
- [ ] AC2: The clone has its own port, identity, certificate and name; the manager is its server admin.
- [ ] AC3: Audit line with source and clone.

### Planned tests (TDD)

1. `ServerCloneTests > "Clone_ChannelsGroupsSettings_UsersOnlyTicked"` - AC1
2. `ServerCloneTests > "Clone_OwnPortIdentityAdmin"` - AC2
3. `ServerCloneTests > "Clone_Audit"` - AC3

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

- [ ] AC1: A user creates a token with name and expiry; it is shown once and stored only as a hash; it can be revoked.
- [ ] AC2: `GET /api/servers`, `POST /api/servers/{id}/start|stop|restart|update` work with `Authorization: Bearer`, limited to the token owner's servers.
- [ ] AC3: Every API call writes an audit line naming the token.
- [ ] AC4: An OpenAPI document lies at `/api/openapi.json`.

### Planned tests (TDD)

1. `ServerEndpointsTests > "Token_ShownOnceHashedRevoked"` - AC1
2. `ServerEndpointsTests > "Endpoints_OwnServersOnly"` - AC2
3. `ServerEndpointsTests > "Call_AuditWithToken"` - AC3
4. `ServerEndpointsTests > "OpenApi_Served"` - AC4

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

- [ ] AC1: The Caddy compose file serves the manager over HTTPS on 443 for `OVS_MANAGER_DOMAIN`, with images pinned.
- [ ] AC2: Forwarded headers are honoured only from the trusted proxies; links and cookies use `OVS_MANAGER_BASE_URL`.
- [ ] AC3 (manual): The release workflow pushes the manager image with the same version as the server.
- [ ] AC4 (manual): Behind Nginx Proxy Manager with WebSocket support on, login and live pages work.

### Planned tests (TDD)

1. `ManagerComposeTests > "Caddy_Https443_Pinned"` - AC1
2. `ManagerHostTests > "ForwardedHeaders_OnlyTrusted"` and `"BaseUrl_UsedForLinks"` - AC2
3. Manual checks AC3 and AC4.

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
