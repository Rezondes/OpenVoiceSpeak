# Plan: OpenVoiceSpeak

> **Open packages only (since 19 October 2026):** at the user's request this file holds the packages not yet built, from 190 on. The built Packages 1 to 189, their assumptions A1 to A201, the old overview and the implementation notes are unchanged in `docs/archive/PLAN-ARCHIVE.md`. Package and assumption numbers continue across both files and are never reused. When a package here is built, it stays in this file until the user moves it into the archive. `PlanReferenceTests` checks the Tests sections and criteria of both files. The web manager has its own plan, `PLAN-MANAGER.md` (Packages M1 to M32).
>
> **Still open in the archive:** manual checks of Packages 16, 17, 27, 42 to 44, 49 to 53, 57, 60, 61, 95, 170, 182 and 188, and the unchecked criteria 75 AC4, 77 AC1 and AC2, 109 AC5, 122 AC6 and AC8, 123 AC7 and AC9, 124 AC3 and AC7, 127 AC7, 137 AC6, 140 AC6, 174 AC6, 177 AC1, 178 AC2 and AC3, 179 AC1, AC7 and AC8, 180 AC1. They stay in `docs/archive/PLAN-ARCHIVE.md` and are checked off there.
>
> **Packages 190 to 194 (what the web manager needs from server and client):** the web manager itself is planned in `PLAN-MANAGER.md` (the user's decision: PLAN.md stays on OpenVoiceSpeak, every change to server or client gets its package here). 190 sets the server admin through an environment variable, 191 adds the invisible service session of the server admin, 192 the metrics of connections and daily users, 193 names the protocol version of a release, 194 moves the control connection into `OVS.Shared`. 191 needs 190, 194 needs 191; 190, 192 and 193 can start at once. 191 raises the protocol version by one. The test sections are headed "Planned tests (TDD)" until each package is built (see 172 to 180).

## Overview

| # | Package | Goal in one sentence | Depends on |
|---|---------|----------------------|------------|
| 190 | Server admin set by the environment | A server started with `OVS_SERVER_ADMIN_FINGERPRINT` has that fingerprint as its server admin from the first start, without a token. | - |
| 191 | Invisible service session | The server admin can connect as a service session, which no other user sees and which counts against no limit. | 190 |
| 192 | Connections and daily users as metrics | The server exports how many connections it accepted and how many different users were online today, without naming anyone. | - |
| 193 | Protocol version of a release | Every server image and release names its protocol version. | - |
| 194 | Control connection in OVS.Shared | The TLS control connection lives in `OVS.Shared`, so the client and the web manager use the same code. | 191 |

## Assumptions

A1 to A201 are in `docs/archive/PLAN-ARCHIVE.md`.

- **A202 Server admin from the environment (Package 190).** The user's decision (F3, 2026-10-19): the web manager makes itself server admin through `OVS_SERVER_ADMIN_FINGERPRINT`, not by reading the token from the console. The variable only acts while nobody is server admin, so it can stay set; with it unset the token path of 181 is unchanged. A193 (no hand-over in the client) stays.
- **A203 Service session (Package 191).** The user's decision (F2): the manager is the server admin and must not show up as a user. A service session needs Server-Admin, is admitted past the password and `MaxUsers`, is invisible to others, sends no voice and joins no channel, may chat (the manager announces restarts and updates in the server chat, so no new message type is needed). Only the protocol flag in the hello is new; the client never sets it.
- **A204 Daily users (Package 192).** The user's decision (F8): the fingerprints of the current day are kept in memory only and never exported; the day follows `TZ` like the auto-restart. A restart starts the day's count again; the manager keeps the highest value per day.
- **A205 One protocol for client and manager (Packages 191, 194).** The manager speaks the normal protocol through `OVS.Shared` (analysis A3), so a server with another protocol version can only be started, stopped, updated and read through Docker and telemetry until it is updated.
- **A206 Protocol in the release (Package 193).** The image label `org.openvoicespeak.protocol` and the line "Protokoll: N" in the release notes; the version format `DDMMYY.stamp` stays (it is not sortable as text, the manager compares release dates).
- **A207 Analysis.** The requirements, the comparison with the code and the decisions of 19 October 2026 are in the project note `notes/verwaltungstool-analyse.md` and in `PLAN-MANAGER.md`.

---

## Package 190: Server admin set by the environment

**Goal:** A server started with `OVS_SERVER_ADMIN_FINGERPRINT` has that fingerprint as its server admin from the first start, without a token (A202).

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/ServerConfig.cs` (change): `ServerAdminFingerprint`, read from `OVS_SERVER_ADMIN_FINGERPRINT`, checked as 64 lowercase hex characters
- `src/OVS.Server/State/ServerAdminPreset.cs` (new): finds or creates the user of the fingerprint and puts it into Server-Admin
- `src/OVS.Server/State/ServerState.cs` (change): applies the preset before the token check
- `src/OVS.Server/Logging/ServerLogLines.cs` (change): `Server admin set from OVS_SERVER_ADMIN_FINGERPRINT ({Fingerprint})`, `OVS_SERVER_ADMIN_FINGERPRINT ignored: the server already has a server admin`
- `docker-compose.yml`, `README.md` (change): the variable, commented out
- `tests/OVS.Tests/Server/ServerConfigTests.cs`, `tests/OVS.Tests/Server/State/AdminCommandTests.cs` (change)

### Context

The server admin is a user (`UserRecord` with the group `...0c`), and today the only way in is the token that `ServerState` prints on the console while `HasServerAdmin` is false (`ServerState.cs:89`). The web manager (PLAN-MANAGER.md) creates servers and must be their server admin without reading the console. A new class keeps the token path unchanged (CODING-RULES rule 26).

### Acceptance Criteria

- [ ] AC1: With a valid fingerprint and no server admin, the server creates the user (nickname empty until the first login, groups Guest and Server-Admin) or adds Server-Admin to an existing user, persists, writes the log line and prints no token.
- [ ] AC2: With a server admin already in the data, the variable changes nothing, the server logs the "ignored" line and prints no token.
- [ ] AC3: A value that is not 64 hex characters ends the start with a `ConfigException` naming the variable (exit code 1); upper case is accepted and stored in lower case.
- [ ] AC4: Without the variable everything stays as in Package 181 (token at every start without a server admin).
- [ ] AC5: The preset user passes `FindProblem` (one server admin) and keeps the role after a restart without the variable.

### Planned tests (TDD)

1. `ServerConfigTests > "ServerAdminFingerprint_Valid_ReadLowerCase"` and `"ServerAdminFingerprint_NotHex_ConfigException"` - AC3
2. `AdminCommandTests > "Preset_NoServerAdmin_UserCreatedInServerAdminNoToken"` - AC1
3. `AdminCommandTests > "Preset_KnownUser_ServerAdminAddedGroupsKept"` - AC1
4. `AdminCommandTests > "Preset_ServerAdminExists_IgnoredLineNoToken"` - AC2
5. `AdminCommandTests > "Preset_Unset_TokenAsBefore"` - AC4
6. `AdminCommandTests > "Preset_RestartWithoutVariable_RoleKept"` - AC5

Test command: `dotnet test OpenVoiceSpeak.slnx --blame-hang-timeout 60s` (covers rule-lint too)

### Steps

1. Test 1 (red), then `ServerConfig` (green).
2. Tests 2 to 6 (red), then `ServerAdminPreset` and the call in `ServerState` (green).
3. Log lines, compose comment, README; full suite, rule-lint.

### Out of Scope

- Changing the server admin later (A193 stays)
- The invisible session of the manager (Package 191)

---

## Package 191: Invisible service session of the server admin

**Goal:** The server admin can connect as a service session, which no other user sees and which counts against no limit (A203).

**Dependencies:** Package 190

**Affected files:**
- `src/OVS.Shared/Protocol/Messages/Connection/ClientHello.cs` (change): `bool Service` (default false)
- `src/OVS.Shared/Protocol/WireProtocol.cs` (change): version 23
- `src/OVS.Shared/Protocol/ReasonCodes.cs` (change): `ServiceNotAllowed`
- `src/OVS.Server/Session.cs` (change): `IsService`
- `src/OVS.Server/State/ServerState.Connections.cs` (change): admission of a service session
- `src/OVS.Server/State/ServiceSessions.cs` (new): the rules (who may, what is hidden)
- `src/OVS.Server/State/ServerState.Broadcast.cs`, `ServerState.Voice.cs` (change): service sessions are left out of `UserJoined`, `UserLeft`, the user list of others and voice
- `src/OVS.Server/ServerMetrics.cs` (change): `ovs.sessions` counts no service session
- `src/OVS.Client/Net/ClientConnection.cs` (change): sends `Service = false`
- `src/OVS.Client/ViewModels/ChatViewModel.cs` (check): a chat message from an unknown session shows `FromNickname`
- `src/OVS.Client/Localization/Strings.resx`, `Strings.en.resx` (change): text for `ServiceNotAllowed`
- `tests/OVS.Tests/Server/HandshakeTests.cs`, `tests/OVS.Tests/Server/State/StateSyncTests.cs`, `ChatTests.cs`, `tests/OVS.Tests/Server/ServerMetricsTests.cs`, `tests/OVS.Tests/Client/ViewModels/ChatViewModelTests.cs`, `tests/OVS.Tests/Client/Localization/LocalizationTests.cs` (change)

### Context

The manager talks to its servers over the normal protocol (A205). A normal session would show "Verwaltung" in every channel list, take a slot of `MaxUsers` (`ServerState.Connections.cs:90`) and need the server password, which an admin can change in the client. The fingerprint is already proven by the handshake signature, so the server can trust it.

### Acceptance Criteria

- [ ] AC1: A hello with `Service = true` from a member of Server-Admin is admitted even when the server is full or has a password; from anyone else it is refused with `ServiceNotAllowed`.
- [ ] AC2: Other sessions get no `UserJoined` or `UserLeft` for a service session, and it is not in their snapshot or `ListUsers` answer; the known-user list still shows the user.
- [ ] AC3: A service session's `VoiceUp` is dropped, `JoinChannel` is refused with `PermissionDenied`; every request it is allowed by its rights works as for a normal session.
- [ ] AC4: A server chat message of a service session reaches everyone with its nickname, and the client shows that nickname although the session is not in its user list.
- [ ] AC5: `ovs.sessions` and the max-users count leave service sessions out; a second service session of the same user replaces the first (`ReplacedByNewConnection`) and never a normal session of that user.
- [ ] AC6: The protocol version is 23; the client shows a text for `ServiceNotAllowed`.

### Planned tests (TDD)

1. `HandshakeTests > "Service_ServerAdmin_AdmittedFullAndWithPassword"` and `"Service_NotServerAdmin_ServiceNotAllowed"` - AC1
2. `StateSyncTests > "Service_NeverInSnapshotJoinedLeftOrUserList"` - AC2
3. `StateSyncTests > "Service_VoiceDroppedJoinRefused"` - AC3
4. `ChatTests > "Service_ServerChat_DeliveredWithNickname"` - AC4
5. `ChatViewModelTests > "Chat_FromUnknownSession_ShowsNickname"` - AC4
6. `ServerMetricsTests > "Sessions_Service_NotCounted"` and `HandshakeTests > "Service_Second_ReplacesOnlyService"` - AC5
7. `LocalizationTests > "ErrorTexts_ServiceNotAllowed_GermanAndEnglish"` and `HandshakeTests > "Hello_OldVersion_VersionMismatch"` with 23 - AC6

Test command: `dotnet test OpenVoiceSpeak.slnx --blame-hang-timeout 60s`

### Steps

1. Tests 1 and 6 (red), then `ClientHello.Service`, version 23, `ServiceSessions`, admission (green).
2. Tests 2 to 5 (red), then the broadcast, voice and chat parts and the client check (green).
3. Texts; full suite, rule-lint.

### Out of Scope

- A client option to connect as a service session
- Hiding the server admin when they connect normally

---

## Package 192: Connections and daily users as metrics

**Goal:** The server exports how many connections it accepted and how many different users were online today, without naming anyone (A204).

**Dependencies:** none (191 only changes which sessions count)

**Affected files:**
- `src/OVS.Server/ServerMetrics.cs` (change): counter `ovs.connections`, gauges `ovs.users.daily` and `ovs.users.daily.new`
- `src/OVS.Server/DailyUsers.cs` (new): the fingerprints of the current day in memory, reset at local midnight (`TZ`)
- `src/OVS.Server/State/ServerState.Connections.cs` (change): reports each admitted session
- `tests/OVS.Tests/Server/ServerMetricsTests.cs`, `tests/OVS.Tests/Server/DailyUsersTests.cs` (new)

### Context

`ServerMetrics` has `ovs.sessions` (online now) and the refusals, but no count of connections and nothing per day (Package 147). The user's decision (F8): the server may keep the fingerprints of one day in memory; only the counts leave it. The manager draws "Verbindungen pro Tag" and "Nutzer pro Tag" from these.

### Acceptance Criteria

- [ ] AC1: Every admitted session adds 1 to `ovs.connections`; a refused hello does not (it stays in `ovs.admission.rejections`).
- [ ] AC2: `ovs.users.daily` is the number of different fingerprints admitted since local midnight; the same user connecting twice counts once.
- [ ] AC3: `ovs.users.daily.new` counts the users of today whose `FirstSeen` is today.
- [ ] AC4: At local midnight both gauges start at 0 again (time from the run's `TimeProvider`, one clock); a restart starts at 0 as well (memory only).
- [ ] AC5: No instrument carries a fingerprint, nickname or IP as a tag; service sessions (191) are not counted.

### Planned tests (TDD)

1. `ServerMetricsTests > "Connections_AdmittedCounted_RefusedNot"` - AC1
2. `DailyUsersTests > "Add_SameFingerprintTwice_CountsOnce"` - AC2
3. `DailyUsersTests > "Add_FirstSeenToday_CountsAsNew"` - AC3
4. `DailyUsersTests > "Midnight_LocalTime_StartsAtZero"` (with `ManualTimeProvider` and a non-UTC zone) - AC4
5. `ServerMetricsTests > "DailyUsers_NoPersonalTags_ServiceNotCounted"` - AC5

Test command: `dotnet test OpenVoiceSpeak.slnx --blame-hang-timeout 60s`

### Steps

1. Tests 2 to 4 (red), then `DailyUsers` (green).
2. Tests 1 and 5 (red), then the instruments and the call in admission (green).
3. Full suite, rule-lint.

### Out of Scope

- Stored statistics per day (the manager stores them, PLAN-MANAGER.md)

---

## Package 193: Protocol version of a release

**Goal:** Every server image and release names its protocol version, so the manager knows before an update whether clients must update too (A206).

**Dependencies:** none

**Affected files:**
- `src/OVS.Server/Program.cs` (change): `--protocol-version` prints `WireProtocol.Version`
- `src/OVS.Server/CommandLine.cs` (new): the two info switches, testable
- `Dockerfile` (change): `ARG OVS_PROTOCOL`, `LABEL org.openvoicespeak.protocol=$OVS_PROTOCOL`
- `.github/workflows/release.yml` (change): reads the value like the version, passes it to the image build and writes "Protokoll: N" into the release notes
- `tests/OVS.Tests/Server/CommandLineTests.cs` (new)

### Context

`Program.cs` answers `--version` for the release workflow (Package 42). The manager reads the image label (from the registry, without pulling) or the release notes to compare the protocol with its own (A207).

### Acceptance Criteria

- [ ] AC1: `OVS.Server --protocol-version` prints the protocol version and ends with 0; `--version` stays as it is.
- [ ] AC2 (manual): An image built by the release workflow carries `org.openvoicespeak.protocol` with that number, and the release notes contain "Protokoll: N".

### Planned tests (TDD)

1. `CommandLineTests > "ProtocolVersion_Switch_PrintsWireVersion"` and `"Version_Switch_Unchanged"` - AC1
2. Manual check AC2 on the next release run.

Test command: `dotnet test OpenVoiceSpeak.slnx --blame-hang-timeout 60s`

### Steps

1. Test 1 (red), then `CommandLine` and `Program.cs` (green).
2. Dockerfile and workflow; full suite, rule-lint.

### Out of Scope

- The manager's image and release job (PLAN-MANAGER.md, Package M32)

---

## Package 194: Control connection in OVS.Shared

**Goal:** The TLS control connection with handshake, ping and message pump lives in `OVS.Shared`, so the client and the web manager use the same code (A205).

**Dependencies:** Package 191 (the hello carries `Service`)

**Affected files:**
- `src/OVS.Shared/Net/ControlConnection.cs` (new): connect, TLS with a certificate check given by the caller, handshake, `SendAsync`, `MessageReceived`, `Disconnected`, ping, graceful close
- `src/OVS.Shared/Exceptions/` (new or moved): the connection exceptions the client has in `OVS.Client.Exceptions`
- `src/OVS.Client/Net/ClientConnection.cs` (change): keeps voice (UDP, TCP fallback) and builds on `ControlConnection`
- `src/OVS.Client/Net/TrustedServerFingerprints.cs` (unchanged): passed in as the certificate check
- `tests/OVS.Tests/Shared/Net/ControlConnectionTests.cs` (new), `tests/OVS.Tests/Client/Net/ClientNetTests.cs` (unchanged, stays green)

### Context

`ClientConnection` (285 lines, `src/OVS.Client/Net/`) mixes the control channel with voice and is internal to the Avalonia client. The manager is an ASP.NET Core app and must not reference the client. The control part depends only on `OVS.Shared` already (framing, identity, messages), so it moves; this is a deep change in the sense of rule 26.

### Acceptance Criteria

- [ ] AC1: `ControlConnection` connects to a real in-process server, completes the handshake (also with `Service = true` for a server admin) and receives the `Welcome`.
- [ ] AC2: A certificate the caller's check refuses ends the connect with the same exception the client shows today (TOFU prompt unchanged).
- [ ] AC3: Ping, idle timeout, `Disconnected` with reason and the TCP half-close of `7b8f52c` behave as before.
- [ ] AC4: Every existing client network test stays green without a change of its assertions.

### Planned tests (TDD)

1. `ControlConnectionTests > "Connect_RealServer_WelcomeReceived"` and `"Connect_Service_WelcomeWithoutSlot"` - AC1
2. `ControlConnectionTests > "Connect_CertificateRefused_SameExceptionAsClient"` - AC2
3. `ControlConnectionTests > "Disconnected_ServerShutdown_ReasonGiven"` and `"Close_HalfClose_ServerSeesEnd"` - AC3
4. `ClientNetTests` (whole class) - AC4

Test command: `dotnet test OpenVoiceSpeak.slnx --blame-hang-timeout 60s`

### Steps

1. Tests 1 to 3 against the new class (red), move the control part (green).
2. `ClientConnection` on top of it, test 4 green; full suite, rule-lint.

### Out of Scope

- Voice for the manager (it never sends voice)
