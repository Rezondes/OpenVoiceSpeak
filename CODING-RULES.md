# Coding and layout rules

How the source of OpenVoiceSpeak is laid out: one type per file, namespace = folder, view and view model suffixes, and where shared helpers live. The rules and the decisions behind them were made on 5 October 2026 (§Decisions (5 October 2026)); this file is meant to be read on its own, without PLAN.md.

**Status.** The layout below is the target of a structural refactoring. Until that refactoring is finished, parts of the code still follow the old layout; new and changed code follows these rules. Items marked "introduced by the refactoring" do not exist before the refactoring adds them.

## Purpose and scope

- Every type is found by its name: the file is named after the type, the folder after the namespace.
- The rules apply to every `.cs` and `.axaml` file in `src/`, `tests/` and `tools/render-icon` (A1 c). They do not apply to `website/`, `Assets/`, `Localization/*.resx`, `Styles/` as resources, or `runtimes/` (their paths are fixed in the csproj, XAML, tools and tests, F6).
- Code is moved and renamed only behaviour-neutrally. Persisted files and the wire carry no CLR type names, so moves do not change them; full CLR type names in diagnostic texts (exception messages, crash logs) may change with a namespace (accepted under A4).

## General rules

1. **One top-level type per file, file name = type name**, without exceptions (B1, B8): also exceptions, coupled pairs (`FrameReader`/`FrameWriter`, `VoiceEncoder`/`VoiceDecoder`, `UpdateChecker`/`UpdateInstaller`, `TransmitMode`/`Transmission`) and internal helpers (B12).
2. **Generics:** a generic type's file has the bare name, without arity or parameters (`Choice.cs` for `Choice<T>`, `ListPages.cs`, B5).
3. **Interfaces** keep the `I` prefix in type and file name (`IMotionKey.cs`, B6).
4. **Partial types:** a type may span several files only as `Type.Part.cs` in one folder (B2 a). Only `ServerState` does this (§ServerState). Partials that source generators add need no file.
5. **Nested types:** only `private` nested types used by their container alone stay nested (B9). Every other nested type is a top-level type of its own, `internal` when it was private and must be reachable from outside. The ones kept nested are listed in §Nested types.
6. **Namespace = folder** in every project (C1 a), with explicit `using` lines and no new `global using`. The root namespaces are `OVS.Shared`, `OVS.Server`, `OVS.Client`, `OVS.Tests`, and `OVS.Tools` for `tools/render-icon` (its folder name is no identifier).
7. **No git-ignored folder names.** No folder is named like a pattern the `.gitignore` ignores at any depth: `Backup*`, `Log`, `Logs`, `Debug`, `Release`, `bin`, `obj`, `x64`, `artifacts`, `publish`, `Generated_Code` (C2). This is why the client folder `Debug/` is `Diagnostics/` (C4) and the `.gitignore` needs no exception for a source folder.
8. **No namespace segment named like a type** of the same project, or like a type the code in that namespace uses (C3). Every new folder name is checked before it is used. Examples of names chosen for this reason: `LogAccess` and `ServerInfo` (not `LogFiles`, `ServerSettings`), `Animations/` (not `Animation/` or `Motion/`), `State/` for `ServerState`, `ValueConverters` in `Converters/`, `DialogCallbacks` in `Dialogs/`.
9. **Views end in `View`** (`AdminView`, `TitleBarView`), except `MainWindow` and `App`. Reusable controls live in `Controls/` and keep their names (`BusySpinner`, `LogoMark`, `OverlayHost`, G1).
10. **Every type in `ViewModels/` is a view model and ends in `ViewModel`** (H1). Bound records and enums go to `Models/`, dialog contracts to `Dialogs/`, non-UI helpers to `Services/` and `Collections/`. Item view models that are read-only (`KnownUserViewModel`, `LogLineViewModel`, `LogHitViewModel`) keep their names (H4).
11. **Renames are allowed** when a rule needs them (A2). Test method names do not change (L4).
12. **Comments name types, not paths** (L3). A document that names a source path (README, THIRD-PARTY-NOTICES, website comments) changes in the same commit as the move (L2).

## Exceptions

Each project has an `Exceptions/` folder (B7 b). A base exception and its subclass get one file each.

| Namespace | Types |
|---|---|
| `OVS.Shared.Exceptions` | `ProtocolException`, `InvalidRequestException` |
| `OVS.Server.Exceptions` | `ConfigException`, `BackupQuotaException` |
| `OVS.Client.Exceptions` | `ConnectionRejectedException`, `TofuRejectedException`, `DebugApiException` |

## Target layout

### OVS.Shared

- `Protocol/` keeps `ProtocolInfo`, `Codes`, `TextRules`, `ServerIconFormat`, and later `Base64Size` (§Shared helpers).
- `Protocol/Framing/`: `ProtocolJson`, `FrameReader`, `FrameWriter` (D7).
- `Protocol/Messages/`: see §Protocol messages.
- `Voice/` stays flat, one type per file (D8): `PacketType`, `Direction`, `VoiceHeader`, `VoiceCrypto`, `SeqCounter`, `ReplayWindow`, `LossReport`, `PingPayload`, `LossWindow`, `RelayPayload`.
- `Permissions/`: `Permission`, `WellKnownGroups`, `PermissionExtensions`. `Identity/`: `ClientIdentity`, `CertFingerprint` (D9).
- `Logging/`, `Exceptions/`, and later `IO/` and `Net/` (§Shared helpers).

### Protocol messages

One type per file under `src/OVS.Shared/Protocol/Messages/` (D1). `Message.cs` (with `[JsonPolymorphic]`, all 75 `[JsonDerivedType]` lines and their group comments, D5; System.Text.Json needs every derived type listed on the base) and `Request.cs` are in `OVS.Shared.Protocol.Messages`. The others are in `Messages/<Area>/` with namespace `OVS.Shared.Protocol.Messages.<Area>` (D2, D3, D4):

| Area | Types |
|---|---|
| `Connection` | Ping, Pong, Error, ClientHello, Challenge, ClientProof, Welcome, Rejected, Disconnected, ServerSnapshot |
| `VoiceRelay` | VoiceUp, VoiceDown |
| `Channels` | ChannelInfo, ChannelKind, JoinChannel, CreateChannel, EditChannel, ReorderChannels, DeleteChannel, ChannelAdded, ChannelUpdated, ChannelRemoved |
| `Users` | UserInfo, KnownUserInfo, MoveUser, SetSelfState, UserJoined, UserUpdated, UserLeft, ListUsers, UserList |
| `Groups` | GroupInfo, CreateGroup, UpdateGroup, ReorderGroups, DeleteGroup, AssignGroup, UnassignGroup, GroupsChanged, RedeemAdminToken |
| `ServerInfo` | ServerSettingsInfo, ServerLimits, UpdateServerSettings, ServerSettingsChanged, SetServerIcon, GetServerIcon, ServerIcon |
| `ServerBackups` | BackupInfo, ListBackups, CreateBackup, DeleteBackup, RestoreBackup, BackupList, DownloadBackup, BackupChunk, UploadBackupChunk, UploadBackupAck, BackupUploaded |
| `LogAccess` | LogKind, LogFileInfo, ListLogs, LogList, ReadLog, LogPage, SearchLogs, LogHit, LogSearchResult, PrepareLogDownload, LogDownloadReady, DownloadLogChunk, LogChunk |
| `Moderation` | BanInfo, Kick, Ban, Unban, BanUser, DeleteUser, ListBans, BanList, SetServerMute, SetStoredServerMute |
| `Links` | LinkInfo, LinkChannels, SetChannelLinks, UnlinkChannels, ChannelsLinked, ChannelsUnlinked |
| `Chat` | ChatTarget, SendChat, ChatMessage |

That is 2 + 90 = 92 types. Data records and enums go to the area they belong to (`LinkInfo` to `Links`, `UserInfo` to `Users`, `ServerSnapshot` to `Connection`). A comment that describes a group of records goes into the summary of each record it describes (D6), since one type per file leaves no shared place for it. The area names avoid every clash (C2, C3): `ServerBackups` instead of `Backups` or `BackupFiles` (both ignored by `Backup*/`), `LogAccess` instead of `LogFiles` (the class `OVS.Shared.Logging.LogFiles`), `ServerInfo` instead of `ServerSettings` (the server class `ServerSettings`), `VoiceRelay` instead of `Voice` (the namespace `OVS.Shared.Voice`). New messages go into the area they belong to and get their `[JsonDerivedType]` line on `Message`.

### OVS.Server

- The root keeps `Program`, `ServerConfig` (E4), `ControlServer`, `Session`, `ServerHost`, `AdminToken`, `Limits` (it holds all limits, not only throttling, E5), `RequestShape` and `MalformedRequest` (a top-level `internal sealed record`, E6; its name in the server log stays "MalformedRequest").
- `Data/`: `DataStore` and the backup types `BackupStore`, `BackupManifest`, `BackupContent`, `ServerIconStore` (E2). `Data/Records/`: the persisted types `ServerSettings`, `ChannelRecord`, `ChannelLink`, `UserRecord`, `BanRecord`, `ServerData` (E1).
- `State/`: `ServerState` and its parts (§ServerState).
- `Permissions/`: `PermissionRules`, `Group` (E3). `Throttling/`: `RateLimiter` (it limits requests too, so it is not in `Voice/`, E5). `Logging/`: `LogReader`, `LogExport`, `ServerLogs`. `Security/`: `PasswordHash` (E7, introduced by the refactoring). `Tls/`, `Voice/`, `Exceptions/`.

### ServerState

All parts of `ServerState` live in `src/OVS.Server/State/` with namespace `OVS.Server.State` (B2 a, B3, B4 a). A folder named `ServerState` would clash with the class (C3), and the parts must share one namespace.

| File | Holds |
|---|---|
| `ServerState.cs` | fields, constructor, settings, `Handle` and `Dispatch`, the helpers without broadcast and persistence |
| `ServerState.Connections.cs` | connections and password guessing: `Admit`, `Remove`, `CloseAll`, `LogRejected`, `RemoveLocked`, the statistics, `ChannelLog` |
| `ServerState.Throttling.cs` | the request limits: `Admissible`, `Throttle`, `ThrottleList`, `ThrottleHeavy`, `OnGetServerIcon`, `CountNewIdentity` |
| `ServerState.Persistence.cs` | `Persist`, `PersistSoon`, `FlushPendingSave`, `BeforeSave`, `PruneGuests`, `RecordBlockedAttempt` |
| `ServerState.Broadcast.cs` | `Broadcast`, `BroadcastExcept` |
| `ServerState.Voice.cs` | voice |
| `ServerState.Admin.cs`, `.Backup.cs`, `.Channel.cs`, `.Chat.cs`, `.Link.cs`, `.Log.cs`, `.Moderation.cs` | the command handlers of each area (formerly `Commands/*Commands.cs`) |

The global lock stays one lock. A new command area gets a new part `ServerState.<Area>.cs` in `State/`.

### Client

- **Root:** `Program`, `ClientOptions`, `App`, `Splash` (F5).
- **`Audio/`** keeps `AudioEngine` and `SpeakerReports` and has subfolders (F2):
  - `Capture/`: CapturePipeline, StereoInput, FrameChunker, SincResampler
  - `Processing/`: AutoGain, NoiseSuppressor, EchoCanceller, VolumeCurve
  - `Transmit/`: TransmitController, TransmitMode, Transmission, VoiceActivityDetector, LossAdapter
  - `Codec/`: AudioFormat, VoiceEncoder, VoiceDecoder
  - `Playback/`: Mixer, SoftLimiter, JitterBuffer, ActiveSpeaker, ReceiveStats
  - `Devices/`: AudioDevice, AudioDevices, IAudioDeviceFactory, WasapiDevices, DeviceWatcher
  - `Sounds/`: SoundEvent, SoundSynth, SoundQueue, SoundImport, SoundLibrary
- **`Net/`** is flat; `Net/Updates/` holds the six update types (`UpdateOffer`, `UpdatePhase`, `UpdateProgress`, `UpdateCheckResult`, `UpdateChecker`, `UpdateInstaller`) (F3).
- **`Input/`** and **`Settings/`** are flat; `PasswordProtector` is in `Settings/`, as it only protects stored settings (F3, F4).
- **`Localization/`:** `ErrorTexts`, `PermissionLabels`, `Language`, `AppLanguage`, and `Formats` (§Shared helpers) (F5, H10).
- **`Diagnostics/`** (was `Debug/`, C4): `DebugApi`, `AudioDebugLog`.
- **`Interop/`:** `NativeMethods` (an `internal static partial class` with every `[LibraryImport]` of the client; a function used in several places is declared once) and one file per Win32 struct under its Win32 name (`POINT`, `SIZE`, `BLENDFUNCTION`, `MSG`, `BITMAPINFOHEADER`, `WNDCLASSEXW`), with unchanged layout (B13).
- **`Views/`:** `MainWindow`, `AdminView`, `ChatView`, `SettingsView`, `TitleBarView`, `ChannelDialogView` (G2, G3).
- **`Controls/`:** `BusySpinner`, `LogoMark`, `OverlayHost` (G1, G4).
- **`Dialogs/`:** `SimpleDialogs` (kept whole, G5), `DialogKind`, `DialogCallbacks` (H6), and the dialog contracts `PasswordAnswer`, `BookmarkEdit`, `ConnectChoice`, `ChannelEdit`, `BanChoice` with their enums `ChannelDialogMode`, `LeaveChoice` (H7).
- **`Animations/`:** `Motion`, `ItemMotion`, `LiveMotion`, `PageMotion`, `FlyGhost` (G6, G11). Not `Animation/`: a namespace `OVS.Client.Animation` would hide Avalonia's class `Avalonia.Animation.Animation` from every file in `OVS.Client.*` (C# finds a namespace in the enclosing `OVS.Client` before the file's `using Avalonia.Animation;`), and several client files create that class.
- **`Behaviors/`:** `ReorderDrag`, `UserDrag`, `DragVisuals` (G7).
- **`Layout/`:** `Responsive`, `SidebarWidth`, `SidebarRow` (G8).
- **`Converters/`:** `ValueConverters` (was `Ui`, G9).
- **`Appearance/`:** `WindowAppearance` (G11).
- **`Services/`:** `IconImport`, `LeaveTimer`, `Reconnector`, `ListRefresh`, `ChunkedDownload`, `DownloadChunk` (G10, H5).
- **`Collections/`:** `CollectionSync`, `ListPages`, `IMotionKey` (the key `CollectionSync` syncs by) (H5, H9).
- **`Models/`:** the immutable records bound as items, `ChatEntry`, `BookmarkItem`, `MoveTarget`, `Notice`, `LogSegment`, `LogSource`, `AudioDeviceOption`, `ThemeOption`, `DisplayOption`, `LanguageOption`, `Choice` (H3), and all enums of the UI, flat: `ChatTabKind`, `NoticeKind`, `Page`, `StateChange`, `SignalRating`, `UserStatusFilter`, `UserSortOrder`, `BanStatusFilter`, `BanTypeFilter`, `BanSortOrder`, `BanStatus`, `AdminPlace` (H8).
- **`ViewModels/`:** only view models, 28 of them, among them the renamed `PermissionToggleViewModel`, `GroupToggleViewModel`, `GroupChoiceViewModel`, `ChatTabViewModel`, `ChatSendingViewModel`, `SoundRowViewModel`, `KeyBindingViewModel`, `ConnectionStatsViewModel`, `PendingViewModel` (H2). `Page`, `Notice`, `Choice`, `ListRefresh` and `KeyBinding` keep their names (H11).
- **Unchanged places:** `Assets/`, `Localization/*.resx`, `Styles/`, `runtimes/` (F6).

**XAML prefixes:** `v` Views, `vm` ViewModels, `m` Models, `ctl` Controls, `anim` Animations, `lay` Layout, `conv` Converters, `s` Settings, `l` Localization. XAML has no compiler `using`: a type in a new namespace needs its prefix in every file that names it, also in type selectors (`ctl|BusySpinner`).

### tools/render-icon

Root namespace `OVS.Tools`, one type per file: `LogoRenderer`, `SmallArt`. The test project links both files under `Tools\`.

### Tests

- The test folders mirror src (J2): `Shared/...`, `Server/...`, `Client/...`, each with the folders of its project; namespaces follow the folders. `Website/` and `TestSupport/` stay; `SmokeTests.cs` and `SourceLayoutTests.cs` are at the root.
- A test file named after a type is named `<Type>Tests.cs` and sits in that type's folder; it follows renames (J3: `ConnectionStatsViewModelTests`, `PendingViewModelTests`). A test file named after a feature keeps its name (J4) and sits in the folder of the type it mostly drives.
- Helpers used by several test files go to `TestSupport/` (`MotionWait`, `FakeHttp`, `DeviceRig`, `UiTestApp` with the `[assembly: AvaloniaTestApplication]` attribute, and the helpers of §Shared helpers). Single-use helpers get their own file next to their test (`CollectingSink`, `Unseekable`, `ShotVariantsAttribute`, `WebsiteScene`) (J1). A test of a test helper sits next to it (`TestSupport/LayoutAssertTests.cs`).
- No test method changes its name, and `WebsiteShots` keeps its name for the README filter (L4).

## Nested types

Nested types that stay nested (private and used only by their container, B9, B14 to B16):

- Server: `ServerHost.RunEnd`, `ServerConfig.FileValues`
- Shared: `LossWindow.Stream`
- Client: `MainViewModel.Restore`, `AudioEngine.Endpoint`, `Mixer.Speaker`, `SoundQueue.Playing`, `ItemMotion.Departure` and `Tracker`, `LiveMotion.Follower`, `PageMotion.Switching` and `Revealer`, `EchoCanceller.Native`, `NoiseSuppressor.Native`
- Tests: `ManualTimeProvider.Timer`, `ChatViewModelTests.Fixture`, `ServerViewModelTests.Fixture`, `DebugApiTests.Instance`, `LiveMotionTests.ToleranceComparer`, `MotionCoverageTests.Walk`, `Step` and `Result`, `ReorderDragTests.Tree`, `UserDragTests.Tree`, `SendPathTests.TalkRig` and `MusicRig`, `VoiceFilterTests.PinkNoise`, `BackupTests.Host`, `FakeAudioDevices.FakeOutput`, `TestDispatcher.Context`

Hoisted, because they are not private or are used outside their container: `AdminPlace` (was `AdminViewModel.Place`), `MalformedRequest` (was `ServerState.MalformedRequest`), `WebsiteScene` (was `WebsiteShots.Scene`), `DeviceRig` (was `SendPathTests.DeviceRig`), `LogExport` (was `LogReader.LogExport`), `DownloadChunk` (was `ChunkedDownload.Chunk`), the Win32 structs of `Splash`, `DialogKind` (was `SimpleDialogs.Kind`) and `SmallArt` (was `LogoRenderer.SmallArt`) (B10, B11).

## Shared helpers

Where code that several places need lives. All of these are introduced by the refactoring; each replaces copies that did exactly the same thing, so none changes behaviour. Use them instead of writing another copy.

| Helper | Holds | Decision |
|---|---|---|
| `OVS.Shared.IO.SafeFile` | `DeleteQuietly` (swallows `IOException` and `UnauthorizedAccessException`), `WriteAtomic` (to `.tmp`, then move; with a `flushToDisk` parameter, each caller keeping whether it flushes), `ReadChunk` (at most `ProtocolInfo.BackupChunkBytes`) | E8, I1, I2 |
| `OVS.Shared.Logging.LogFiles.NameFormat` | the log name format `yyyy-MM-dd_HH-mm-ss` | I4 |
| `OVS.Shared.Protocol.Framing.ProtocolJson.Indented` | indented JSON options | I5 |
| `OVS.Shared.Protocol.Base64Size` | `Encoded(bytes)` and `Decoded(chars)`; base64 input is decoded with `Convert.TryFromBase64String` into a buffer of `Decoded` bytes, then sliced to the bytes written | I6, I7 |
| `OVS.Shared.Net.IpAddresses.Unmap` | IPv4-mapped IPv6 addresses to IPv4 | I13 |
| `ProtocolInfo` constants | the name and text lengths (`MaxNameLength`, `MaxTextLength`, `MaxGroupNameLength`, `MaxNicknameLength`), `MaxUsersMin`, `MaxUsersMax`, `MaxUsersDefault`, `LogDaysMax`, `LogDaysDefault` | I8, I9 |
| `OVS.Server.Security.PasswordHash` | PBKDF2 hashing, verifying and `IsValidHash`, with a byte-identical stored format | E7 |
| `ServerState` private helpers | `PasswordTooLong`, `NextChannelOrder`, `UsersAsAdminTuples` | I10, I11, I12 |
| `OVS.Client.Localization.Formats` | `Duration`, `Size`, `ClockTime` (`"HH:mm"`) | H10, I18 |
| `AudioFormat.HalfFrameSamples` | the half frame of 480 samples | I16 |
| `IMotionKey.KeyOf` | the motion key of an item (static interface method) | I17 |
| `FlyGhost.ScrollViewport` | the area the nearest `ScrollViewer` shows, in overlay coordinates | I21 |
| `OVS.Client.Behaviors.DragVisuals` | `Lift`, `Follow`, `Snapshot` | G7 |
| `TestSupport/KeyedRow` | the keyed row record of the collection and motion tests | J5 |
| `TestSupport/Snapshots` | the `UserInfo` builder and the snapshot and `StateMirror` construction of the client tests | J5 |
| `TestSupport/MotionWait` | `Settle(int)` and `WaitFor(Func<bool>, int)` | J5 |
| `TestSupport/AsyncWait` | `Until(Func<bool>)` | J5 |
| `TestSupport/RepoPaths` | the repository root and `src/OVS.Client` | J5 |
| `TestSupport/TempDirectoryTest` | base class for tests whose `Dispose` only deletes their temporary folder | J5 |
| `TestSupport/Rows` | row lookups and the row center of the UI tests | J5 |

Deliberately not shared, because sharing would change behaviour or coupling (A4): flushing every atomic write (I3), the bound flags `IsInfo`, `IsWelcome`, `IsWarning`, `IsError` of `ChatEntry` and `Notice` (a shared base record would change both records' `ToString` and equality), the native library loader of the audio filters (I16), one type for the failure counters (I14, semantics differ), I15, I19, I20, I22 (it would tie the client to the tools project), I23 (output changes), I24, I25.

## Rule checker

The refactoring introduces `tests/OVS.Tests/SourceLayoutTests.cs` with its scanner `tests/OVS.Tests/SourceLayout.cs` (K5: a custom xUnit test, no analyzer package and no new dependency). Once it exists, it guards these rules on every test run. It checks every `.cs` and `.axaml` under `src/`, `tests/` and `tools/render-icon` (without `bin/`, `obj/` and `*.Designer.cs`):

- one top-level type per file;
- file name = type name (`.axaml.cs` and `.axaml` without their extensions, a generic type without its parameters);
- the parts of `ServerState` named `ServerState.<Part>.cs` in `State/`;
- namespace = folder;
- nested types only `private`;
- in `src/` only (the test folders `Client/Views/` and `Client/ViewModels/` hold `*Tests` classes): every type in a `Views/` folder ends in `View` except `MainWindow`, and every type in a `ViewModels/` folder ends in `ViewModel`;
- no folder named like an ignored pattern (§General rules, rule 7);
- no namespace segment equal to the simple name of a type declared in the same project.

These rules have no exceptions beyond the ones listed. Files without a type (the top-level statements of `src/OVS.Server/Program.cs` and `tools/render-icon/Program.cs`) pass. The scanner strips comments, strings and preprocessor lines and counts braces; it is no C# parser, so its own tests pin the forms the code base uses (records with primary constructors, file-scoped namespaces, generic constraints, attributes before types).

## Review checklist: design principles

Every review of new or changed code answers each point below for that change, names the file and line for every "no", and either fixes it or records why not. The red flags come from the audit of 5 October 2026 (weakest points: single responsibility, domain rules inside view models and `ServerState`, behaviour specs not linked to tests).

**DRY (don't repeat yourself)**
- Does the change copy logic that exists elsewhere (a helper in §Shared helpers, a rule in `PermissionRules`, a settings mapping in `AudioEngine`)? Use or extend the one place.
- Does a new setting, sound, key action or request need edits in several lists that must agree? Prefer one table or one mapping method over parallel switches.
- Red flag: the same predicate in client and server (e.g. "is this ban active"); put it in `OVS.Shared`.

**DDD (domain-driven design)**
- Is a domain rule (who may do what, when a channel is full or locked, which sound fits which event, reconnect policy) placed in a pure rule class next to its siblings, not inside a view model, a view, `ServerState` or `Session`?
- Does the code use the domain words of the protocol and PLAN.md (Channel, Link, Group, Lock, Separator, Ban, ServerMute, Deafen)?
- Are invariants of persisted records kept by one method on the record or a rule class, not only by a handler?

**TDD (test-driven development)**
- Were the tests written first and seen red (state it in the review notes)?
- Is every new rule tested where it lives, as a fast unit test, with end-to-end tests (TLS, headless UI) only for the acceptance path?
- Does every test use `ManualTimeProvider` or frame counts instead of `Task.Delay`, `Thread.Sleep`, `DateTime.Now` or wall-clock bounds? Is no new static state shared between test classes?

**BDD (behaviour-driven development)**
- Does each acceptance criterion of the package have a test whose name states the behaviour (`Subject_Condition_Outcome`) and that references its criterion (`// Package N ACm` or a trait)?
- Does each test check one behaviour, so its name says what broke when it fails?
- Is each manual check listed as manual in PLAN.md?

**KISS (keep it simple)**
- Is this the simplest code that meets the criteria (no framework, interface or generic layer with one user)?
- Are deliberate shortcuts marked with a `ponytail:` comment naming the limit and the upgrade path?
- Red flags: closure state machines with several captured mutable variables, a new synchronisation scheme, mixed clock sources in one class.

**SOLID**
- **Single responsibility:** does a class gain a responsibility it did not have? Red flags: `ServerState`, `Session`, `MainViewModel`, `AdminViewModel`, `ServerViewModel`, `AudioEngine` growing further; a class over about 400 lines; a new section in a partial file. Extract a collaborator or a child view model instead (the admin page already composes `LogsViewModel` and `LinkMatrixViewModel`).
- **Open/closed:** how many places change to add one more request, dialog, sound, setting or key action? A new `case` in a central switch needs a test that every member is handled (e.g. every `Request` subtype is dispatched).
- **Liskov substitution:** does a subtype keep every promise of its base (no `NotSupportedException` overrides, no stricter preconditions)?
- **Interface segregation:** does a consumer receive more than it uses (the whole `ServerState`, `Session`, `DialogCallbacks` or `AudioEngine`)? Pass the narrow piece (a delegate, a record, a small interface with a real second implementation).
- **Dependency inversion:** are time (`TimeProvider`), the UI thread (`post`), the network (`send` or a connection factory), files and devices passed in rather than created inside the class or read from statics (`DateTime.Now`, `Environment.TickCount64`, singletons, static mutable state such as `Motion.IsAnimated`)?

## History

- These rules supersede the earlier deliberate decision in PLAN.md for fewer small files ("weniger Kleinstdateien", in its table of deliberate deviations from the plan text), under which several types were merged into one file on purpose (A5). PLAN.md itself is not edited for this.
- Paths named in PLAN.md Packages 1 to 142 describe the layout before this refactoring. They are history and are not updated (L1).
- `git blame` history of split files is lost (M6); `.git-blame-ignore-revs` lists the pure move commits of the refactoring. Run `git config blame.ignoreRevsFile .git-blame-ignore-revs` once to make a local `git blame` skip them (GitHub reads the file by itself).

## Decisions (5 October 2026)

The question IDs (A1 to M9) used in this file refer to these decisions.

Answered by the user:

| # | Answer | Decision |
|---|---|---|
| A1 | c | Scope: `src/`, `tests/` and `tools/render-icon` |
| A2 | b | Renames are in scope (needed for the View and ViewModel suffixes) |
| A3 | a | DRY is part of the series, in separate commits after the pure moves |
| A4 | b | Small behaviour changes only when listed and approved one by one |
| A5 | a | The new rule supersedes the earlier decision for fewer small files |
| B1 | a | File name always equals type name |
| B2 | a | Partial classes are allowed, files named `Type.Part.cs` |
| B4 | a | `ServerState.cs` itself is split into more parts |
| B5 | a | Generic types: file named without arity (`Choice.cs`) |
| B6 | a | Interface files keep the `I` prefix |
| B7 | b | Exceptions: one file each, in an `Exceptions/` folder per project |
| B8 | a | Strictly one type per file, also for tightly coupled pairs |
| B12 | a | Top-level internal helpers get their own files |
| B13 | b | Win32 interop moves into a shared `Interop/` with `NativeMethods` |
| B16 | a | Single-use private helpers stay nested |
| B17 | b | Small records of one class's API: own files in a subfolder per area |
| C1 | a | Namespace = folder |
| C2 | a | No folder names that `.gitignore` ignores |
| C3 | a | New folder names are checked against type names before moving |
| C4 | b | The client folder `Debug/` is renamed |
| Error texts |  | Full .NET type names in diagnostic texts may change (accepted under A4) |
| M4 |  | Each step of the refactoring is committed autonomously after its independent review |

Decided from those answers:

| # | Decision | Why |
|---|---|---|
| B3 | All `ServerState` parts move into a folder `State/` (namespace `OVS.Server.State`) as `ServerState.cs`, `ServerState.Admin.cs`, `ServerState.Backup.cs`, ... | The parts must share one namespace. A folder named after the type would clash (C3), and the root would get 9+ files. `State/` keeps namespace = folder |
| B9 | Only `private` nested types used by their container alone may stay nested | The strictest reading consistent with B1/B8 |
| B10 | Hoist all four: `AdminPlace` (was `AdminViewModel.Place`), `MalformedRequest`, `WebsiteScene` (was `WebsiteShots.Scene`), `DeviceRig` (to `TestSupport/`) | They are used outside their container, so B9 does not cover them |
| B11 | Hoist both: `LogExport`, `DownloadChunk` (was `ChunkedDownload.Chunk`) | They are not private, so B9 does not cover them |
| B14 | `EchoCanceller.Native` and `NoiseSuppressor.Native` stay nested | Private and single use (B9) |
| B15 | `LossWindow.Stream` and `ManualTimeProvider.Timer` stay nested and unchanged | Private and single use; the clash would only matter if they were hoisted |
| B13 detail | An `Interop/` folder: `NativeMethods` (all user32/gdi imports of Splash, KeyPoller, ReorderDrag) and one file per Win32 struct, keeping the Win32 names (`POINT.cs` ...) | B13 b plus B1 |
| C4 detail | `Debug/` becomes `Diagnostics/` (namespace `OVS.Client.Diagnostics`). The `.gitignore` exception for `src/OVS.Client/Debug/` goes away | C4 b |
| D1 | One record per file | B1 |
| D2/D3 | `Protocol/Messages/<Area>/`, with `Message.cs` and `Request.cs` in `Protocol/Messages/`. The areas:<br>• `Connection`: Ping, Pong, Error, ClientHello, Challenge, ClientProof, Welcome, Rejected, Disconnected, ServerSnapshot<br>• `VoiceRelay`: VoiceUp, VoiceDown<br>• `Channels`<br>• `Users`: incl. MoveUser, SetSelfState, ListUsers, UserList<br>• `Groups`: incl. RedeemAdminToken<br>• `ServerInfo`: ServerSettingsInfo, ServerLimits, UpdateServerSettings, ServerSettingsChanged, incl. the server icon<br>• `ServerBackups`, `LogAccess` (the log messages), `Moderation`, `Links`, `Chat` | The feature folders follow the existing section comments. `Backups`, `BackupFiles` (both match `Backup*/`) and `Logs` are git-ignored names (C2). `Voice` would clash with `OVS.Shared.Voice`, `LogFiles` with the class `OVS.Shared.Logging.LogFiles`, `ServerSettings` with the server class `ServerSettings` (C3) |
| D4 | DTOs and enums go in their area folder; `ServerSnapshot` in `Connection` | Keeps each area together |
| D5 | The discriminator list stays on `Message` | Required by System.Text.Json |
| D6 | Comments that describe a group of records move into each record's summary | One type per file leaves no shared place for them |
| D7 | `ProtocolJson`, `FrameReader`, `FrameWriter` go to `Protocol/Framing/`; the exceptions go to `Exceptions/` (B7) | |
| D8 | `Voice/` stays flat (10 files) | Small enough |
| D9 | Confirmed | |
| E1 | `Data/Records/` for the persisted types; `DataStore` stays in `Data/` | |
| E2 | Backup types stay flat in `Data/`; `BackupQuotaException` goes to `Exceptions/` | No ignored folder name needed |
| E3 | Confirmed | |
| E4 | `ServerConfig` stays in the root; `ConfigException` goes to `Exceptions/` | |
| E5 | `RateLimiter` goes to `Throttling/`; `Limits.cs` stays in the root | `Limits.cs` holds all limits, not only throttling |
| E6 | `MalformedRequest` becomes a top-level internal type in the server root | B10 |
| E7 | PBKDF2 hashing moves to `Security/PasswordHash.cs` (server); the stored hash format stays byte-identical | DRY (A3), neutral |
| E8 | `DeleteQuietly`, the atomic write (with a flush parameter) and `ReadChunk` move to Shared `IO/SafeFile.cs` (namespace `OVS.Shared.IO`) | DRY across Client and Server |
| F1 | Yes, everywhere (A1 c) | |
| F2 | `Audio/` gets subfolders:<br>• `Capture`: CapturePipeline, StereoInput, FrameChunker, SincResampler<br>• `Processing`: AutoGain, NoiseSuppressor, EchoCanceller, VolumeCurve<br>• `Transmit`: TransmitController, TransmitMode, Transmission, VoiceActivityDetector, LossAdapter<br>• `Codec`: AudioFormat, VoiceEncoder, VoiceDecoder<br>• `Playback`: Mixer, SoftLimiter, JitterBuffer, ActiveSpeaker, ReceiveStats<br>• `Devices`: AudioDevice, AudioDevices, IAudioDeviceFactory, WasapiDevices, DeviceWatcher<br>• `Sounds`: SoundEvent, SoundSynth, SoundQueue, SoundImport, SoundLibrary<br>`AudioEngine` and `SpeakerReports` stay in `Audio/` | 30 files flat in one folder would be hard to read |
| F3 | `Net/Updates/` for the 6 update types; `Input/` and `Settings/` stay flat | |
| F4 | `PasswordProtector` stays in `Settings/` | It only protects stored settings |
| F5 | `Program`, `ClientOptions`, `App`, `Splash` stay in the root; `ErrorTexts` and `PermissionLabels` go to `Localization/` | |
| F6 | `Assets/`, `Localization/*.resx`, `Styles/` and `runtimes/` stay where they are | Their paths are fixed in the csproj, XAML, tools and tests |
| G1 | Reusable controls go to `Controls/` and keep their names (`BusySpinner`, `LogoMark`, `OverlayHost`); views get the `View` suffix | |
| G2 | `TitleBar` becomes `Views/TitleBarView` | It binds `MainViewModel`, so it is a view |
| G3 | `ChannelDialog` becomes `Views/ChannelDialogView` | |
| G4 | `OverlayHost` goes to `Controls/` | |
| G5 | `SimpleDialogs` goes to `Dialogs/` with its name kept, not split | Splitting it would be a redesign |
| G6 | The motion family goes to `Animations/` (namespace `OVS.Client.Animations`) | A folder `Motion/` would clash with the class `Motion`, and `Animation/` with Avalonia's class `Avalonia.Animation.Animation`, which six client files create (C3) |
| G7 | `ReorderDrag` and `UserDrag` go to `Behaviors/`; `Lift`/`Follow`/`Snapshot` are extracted into `Behaviors/DragVisuals.cs` (in the DRY phase) | |
| G8 | `Responsive`, `SidebarWidth` and `SidebarRow` go to `Layout/` | |
| G9 | `Ui` becomes `Converters/ValueConverters.cs` | B1; `Converters` as a class name would clash with its folder (C3) |
| G10 | `IconImport` goes to `Services/` | |
| G11 | `WindowAppearance` goes to `Appearance/`; `Motion.Apply` stays in `Animations/` | |
| H1 | Strict: only view models stay in `ViewModels/` | |
| H2 | Renamed: `PermissionToggleViewModel`, `GroupToggleViewModel`, `GroupChoiceViewModel`, `ChatTabViewModel`, `ChatSendingViewModel`, `SoundRowViewModel`, `KeyBindingViewModel`, `ConnectionStatsViewModel`, `PendingViewModel` | They are observable and bound, so they are view models |
| H3 | Immutable records bound as items move to `Models/` | H1 |
| H4 | `KnownUserViewModel`, `LogLineViewModel` and `LogHitViewModel` keep their names | They are bound as item view models; read-only data needs no change notification |
| H5 | `Services/`: `LeaveTimer`, `Reconnector`, `ListRefresh`, `ChunkedDownload`, `IconImport`. `Collections/`: `CollectionSync`, `ListPages<T>`, `IMotionKey` | `IMotionKey` is the key `CollectionSync` syncs by |
| H6 | `Dialogs` becomes `Dialogs/DialogCallbacks`, with the same shape | A class `Dialogs` would clash with its folder (C3) |
| H7 | Dialog contracts and their enums go to `Dialogs/` | |
| H8 | All enums go to `Models/` (flat); `AdminViewModel.Place` becomes `AdminPlace` | H1, B10 |
| H9 | See H5 | |
| H10 | `Duration` and `Size` move to `Localization/Formats.cs`, behaviour-identical | DRY |
| H11 | No further renames (`Page`, `Notice`, `Choice`, `ListRefresh` and `KeyBinding` stay) | No real clash remains after the moves |
| I | Done: I1, I2 (flush kept per call site), I4, I5, I6, I7, I8, I9, I10, I11, I12, I13 (Shared `Net/IpAddresses`), I16 (the shared constant only), I17, I18, I21.<br>Not done: I3, I14, I15, the I16 loader, I19, I20, I22, I23, I24, I25 | Only behaviour-neutral items are done (A4). I22 would tie the client to the tools project |
| J1 | Shared helpers go to `TestSupport/`; single-use ones get their own files next to their test | |
| J2 | Test folders mirror src | |
| J3 | Test files follow moved and renamed classes | |
| J4 | Feature-named test files keep their names | |
| J5 | All six test DRY items are done | Test-only and neutral |
| K1 | The XAML text test is made recursive over the whole client (incl. App and Styles) before anything moves | |
| K2 | `Motion.axaml` is found by name | |
| K3 | a, b and c: the suite stays green after every step, and the reflection dump and JSON snapshot are diffed before and after | |
| K4 | The dump tools, rename maps and baselines stay outside the repo, persistently in `%LOCALAPPDATA%\OVS-Refactor\`; a "before" build is redone in a git worktree there when needed | No tool in the repo, but the checks must survive across sessions |
| K5 | A custom xUnit test enforces the rules at the end | No new dependency |
| K6 | `GenerateDocumentationFile` is on only during the refactoring | Catches broken crefs without changing the published output |
| L1 | PLAN.md paths stay as history, plus one note (now §History of this file) | |
| L2 | Updated in the same commit as the move | |
| L3 | Comments name types instead of paths | |
| L4 | `WebsiteShots` keeps its name | |
| M1 | Numbered steps of a working plan | First planned as numbered packages in PLAN.md; moved out of PLAN.md on 5 October 2026, as the refactoring is a necessary side refactor, not a set of official packages. The working plan is kept outside the repository |
| M2 | Per project, each step behaviour-neutral and green: safety nets, Shared, Server, Client (non-UI), Client (UI), Tests, DRY, enforcement | Each file is split and moved once, never moved twice |
| M3 | One commit per step | |
| M4 | Commit autonomously after each step's independent review | The user's answer |
| M5 | Directly on `main`; the `safety/before-restoring-94` branch is left untouched | |
| M6 | Lost blame history is accepted; a `.git-blame-ignore-revs` lists the refactoring commits | |
| M7 | An independent review per step | |
| M8 | The tool artifacts are cleaned up | |
| M9 | The server image is rebuilt and restarted at the end | |
