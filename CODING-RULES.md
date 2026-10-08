# Coding and layout rules

How the source of OpenVoiceSpeak is laid out and named, and which design rules every change follows: one type per file, namespace = folder, view and view model suffixes, names that say exactly what a type is, and where shared helpers live. The rules and the decisions behind them were made on 5 October 2026 (§Decisions (5 October 2026), IDs A1 to M9) and on 6 October 2026 (§Decisions (6 October 2026), IDs N1 to N30); where they differ, the decisions of 6 October 2026 win (N1). This file is meant to be read on its own, without PLAN.md.

**Status.** The first, structural refactoring (5 October 2026) is finished; items marked "introduced by the refactoring" are the ones it added. A second refactoring (6 October 2026) applies the design principles and the naming rule to the existing code (N1, N2). Until it is finished, parts of the code and some type names in the tables below still show the state before it; the tables name the types as they are and follow each rename and move in the commit that makes it. Items marked "not yet introduced" do not exist before that refactoring adds them. New and changed code follows these rules.

## Purpose and scope

- Every type is found by its name: the file is named after the type, the folder after the namespace.
- The rules apply to every `.cs` and `.axaml` file in `src/`, `tests/` and `tools/render-icon` (A1 c). They do not apply to `website/`, `Assets/`, `Localization/*.resx`, `Styles/` as resources, or `runtimes/` (their paths are fixed in the csproj, XAML, tools and tests, F6).
- Moves and renames are behaviour-neutral and never share a commit with a logic change (N4). Behaviour changes where the design principles need it, listed in the commit that makes it (N1). Persisted files and the wire carry no CLR type names, so moves and renames do not change them: wire discriminators, `ReasonCodes` values and JSON property names stay (N9), and persisted files stay loadable with equal stored values (N11). Full CLR type names in diagnostic texts (exception messages, crash logs) may change with a namespace or a name (accepted under A4).
- A public getter of a persisted record is written to its file, so new checks on records are methods, never getters, and a commit that adds or removes such a getter lists it (N11).

## General rules

1. **One top-level type per file, file name = type name**, without exceptions (B1, B8): also exceptions, coupled pairs (`FrameReader`/`FrameWriter`, `VoiceEncoder`/`VoiceDecoder`, `UpdateChecker`/`UpdateInstaller`, `TransmitMode`/`AudioContent`) and internal helpers (B12).
2. **Generics:** a generic type's file has the bare name, without arity or parameters (`Choice.cs` for `Choice<T>`, `ListPages.cs`, B5).
3. **Interfaces** keep the `I` prefix in type and file name (`IMotionKey.cs`, B6). An interface exists only when the tests use a real fake of it or when it has several production implementations; an interface with one implementation that nothing fakes is replaced by the concrete class or a delegate (N3).
4. **Partial types:** a type may span several files only as `Type.Part.cs` in one folder (B2 a). Only `ServerState` does this (§ServerState). Partials that source generators add need no file.
5. **Nested types:** only `private` nested types used by their container alone stay nested (B9). Every other nested type is a top-level type of its own, `internal` when it was private and must be reachable from outside. The ones kept nested are listed in §Nested types.
6. **Namespace = folder** in every project (C1 a), with explicit `using` lines and no new `global using`. The root namespaces are `OVS.Shared`, `OVS.Server`, `OVS.Client`, `OVS.Tests`, and `OVS.Tools` for `tools/render-icon` (its folder name is no identifier).
7. **No git-ignored folder names.** No folder is named like a pattern the `.gitignore` ignores at any depth: `Backup*`, `Log`, `Logs`, `Debug`, `Release`, `bin`, `obj`, `x64`, `artifacts`, `publish`, `Generated_Code` (C2). This is why the client folder `Debug/` is `Diagnostics/` (C4) and the `.gitignore` needs no exception for a source folder.
8. **No namespace segment named like a type** of the same project, or like a type the code in that namespace uses (C3). Every new folder name is checked before it is used. Examples of names chosen for this reason: `LogAccess` and `ServerInfo` (not `LogFiles`, the class now named `LogFileWriter`, and not `ServerSettings`), `Animations/` (not `Animation/` or `Motion/`), `State/` for `ServerState`, `ValueConverters` in `Converters/`, `DialogCallbacks` in `Dialogs/`.
9. **Views end in `View`** (`AdminView`, `TitleBarView`), except `MainWindow` and `App`. Reusable controls live in `Controls/` and keep their names (`BusySpinner`, `LogoMark`, `OverlayHost`, G1).
10. **Every type in `ViewModels/` is a view model and ends in `ViewModel`** (H1). Bound records and enums go to `Models/`, dialog contracts to `Dialogs/`, non-UI helpers to `Services/` and `Collections/`. Item view models that are read-only (`KnownUserViewModel`, `LogLineViewModel`, `LogHitViewModel`) keep their names (H4).
11. **Renames are allowed** when a rule needs them (A2, N2). Test methods may be renamed and split (N21, N24); the test class `WebsiteShots` keeps its name for the README filter (L4).
12. **Comments name types, not paths** (L3). A document that names a source path (README, THIRD-PARTY-NOTICES, website comments) changes in the same commit as the move (L2).
13. **Names say exactly what a thing is** (N2). A type contains only what its name says; a type that does more is split. No project type name is the plural of another project type name (`X`+`s`, `X`+`es`, `Xy`/`Xies`; framework types do not count). No bag names: no type name ends in `Helper`, `Helpers`, `Util`, `Utils`, `Manager`, `Common` or `Misc`, and `Service`, `Info` and `Data` appear only with a subject (wire records such as `ChannelInfo` keep their names; the server has no `*Service` classes at all, N7). Methods are verbs and do not repeat the name of their type. Methods that return a `Task` end in `Async`. One concept may have a code name that differs from its wire name (N9).
14. **English in code** (N5). Identifiers, log lines and exception messages are English. The wire carries codes, and the texts the client shows for them are localized in the client. New and moved text is English at once; the remaining German log and exception texts are converted by the refactoring.
15. **The compiler enforces naming where it can** (N6): an `.editorconfig` with naming rules (the `I` prefix of interfaces, the `Async` suffix, PascalCase types and members) and `EnforceCodeStyleInBuild` (the built-in analyzers, no package, so K5 holds). Not yet introduced; until then the review checks these names (§Review checklist).

## Exceptions

Each project has an `Exceptions/` folder (B7 b). A base exception and its subclass get one file each.

| Namespace | Types |
|---|---|
| `OVS.Shared.Exceptions` | `ProtocolException`, `InvalidRequestException` |
| `OVS.Server.Exceptions` | `ConfigException`, `BackupQuotaException` |
| `OVS.Client.Exceptions` | `ConnectionRejectedException`, `TofuRejectedException`, `DebugApiException` |

## Target layout

### OVS.Shared

- `Protocol/` keeps `WireProtocol` (what client and server must agree on: the version and the log page size), `ProtocolLimits` (the bounds both keep: lengths, ranges, transfer sizes) and `ProtocolDefaults` (the start values); together they were `ProtocolInfo` (N18), whose server-only values went to `ServerTuning`. It also keeps `ReasonCodes` (was `Codes`), `TextRules`, `ServerIconFormat`, and later `Base64Size` (§Shared helpers).
- `Protocol/Framing/`: `ProtocolJson`, `FrameReader`, `FrameWriter` (D7).
- `Protocol/Messages/`: see §Protocol messages.
- `Voice/` stays flat, one type per file (D8): `PacketType`, `PacketDirection` (was `Direction`), `VoiceHeader`, `VoiceCrypto`, `SeqCounter`, `ReplayWindow`, `LossReport`, `PingPayload`, `LossWindow`, `RelayPayload`.
- `Permissions/`: `Permission`, `WellKnownGroups`, `PermissionExtensions`, and the rules server and client share, `GroupRules`, `ChannelAccess` and `ChannelCapacity` (N1; `ChannelCapacity` moved from the server's `Rules/` in Step 22). `Identity/`: `ClientIdentity`, `CertFingerprint` (D9).
- `Moderation/`: `BanStatus` (moved from the client, N18) and its rule `BanStatusRule`.
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
| `Moderation` | BanInfo, KickSession, BanSession, Unban, BanKnownUser, DeleteKnownUser, ListBans, BanList, SetSessionServerMute, SetKnownUserServerMute |
| `Links` | LinkInfo, LinkChannels, SetChannelLinks, UnlinkChannels, ChannelsLinked, ChannelsUnlinked |
| `Chat` | ChatTarget, SendChat, ChatMessage |

That is 2 + 90 = 92 types. Data records and enums go to the area they belong to (`LinkInfo` to `Links`, `UserInfo` to `Users`, `ServerSnapshot` to `Connection`). A comment that describes a group of records goes into the summary of each record it describes (D6), since one type per file leaves no shared place for it. The area names avoid every clash (C2, C3): `ServerBackups` instead of `Backups` or `BackupFiles` (both ignored by `Backup*/`), `LogAccess` instead of `LogFiles` (the class `OVS.Shared.Logging.LogFiles`, now `LogFileWriter`), `ServerInfo` instead of `ServerSettings` (the server class `ServerSettings`), `VoiceRelay` instead of `Voice` (the namespace `OVS.Shared.Voice`). New messages go into the area they belong to and get their `[JsonDerivedType]` line on `Message`. Renaming a record never changes its discriminator, the `ReasonCodes` values or the JSON property names, and needs no protocol version bump (N9); `WireFreezeTests` pins them with literal lists.

### OVS.Server

- The root keeps `Program`, `ServerConfig` (E4), `ControlServer`, `Session` and its `SessionOutbox` (the frames for the write loop; no folder named `Session/`), `ServerHost`, `ServerStores` (the file stores of one run: `Open` makes them and runs the crash cleanups of uploads, half written backups and log exports, in that order; `ServerState` receives them and creates none), `AdminToken`, `ServerTuning` (it holds all limits, not only throttling, E5, and the server-only values that were in `ProtocolInfo`: the chat burst and window, the log line cut, the search timeout and the log download cap), `RestartSchedule` (when the daily automatic restart is due and the wait for it, N8) and `RequestShape`. A frame that is a JSON object but no valid request gets no stand-in type: `ControlServer` passes its request id and problem to `ServerState.HandleMalformed` (overrides E6).
- `Data/`: `DataStore` and the backup types `BackupStore` (create, list, find, delete, the quota, adopting a finished upload, removing its own half written archives at start), `BackupArchive` (one archive: write, read and check, manifest, entries), `BackupManifest`, `BackupContent` (the checked contents; applies itself to the data folder), `ServerIconStore` (E2), `UploadFolder` (upload ids, their hidden files, removing unfinished uploads at start), `BackupUpload` (one session's upload in progress, N8) and `SaveScheduler` (when the data file is written: at once, debounced, retried, at most once a minute per key, and on close; N8). `Data/Records/`: the persisted types `ServerSettings`, `ChannelRecord`, `ChannelLinkRecord`, `UserRecord`, `BanRecord`, `ServerData` (E1).
- `State/`: `ServerState` and its parts (§ServerState).
- `Permissions/`: `PermissionRules`, `GroupRecord` (E3). `Rules/`: the pure request rules of the server, one class per concept (`ChannelSettingsRule`, `ChannelLinkRule`, `ModerationRule`, `ServerLimitsRule`) and the `Refusal` they return (N7). `Throttling/`: `RateLimiter` (it limits requests too, so it is not in `Voice/`, E5), `SessionThrottle` (one session's request, list, costly and chat budgets and the flood rule, N8), the guessing limits `PasswordGuessLimit` (wrong server passwords per address group), `AdminTokenGuessLimit` (wrong admin tokens per address group) and `ChannelPasswordGuessLimit` (wrong channel passwords per session and channel), and `NewIdentityLimit` (new identities per address group), N8. `Logging/`: `LogReader` (lists, pages and searches the log files), `LogExporter` (writes a download snapshot from the reader's listing and removes what a crash left behind, N8) with its result record `LogExport`, `ServerLogWriter` (`WriteServerLine`, `WriteChannelLine`, through the shared `LogFileWriter`), and one session's `LogJobQueue` and `LogDownload` (N8). `Security/`: `StoredPassword` (E7, introduced by the refactoring as `PasswordHash`; a value object since N10). `Tls/`, `Voice/` (`UdpVoiceServer`, `VoiceRouting` and one session's `SessionVoice`, N8), `Exceptions/`.

### ServerState

All parts of `ServerState` live in `src/OVS.Server/State/` with namespace `OVS.Server.State` (B2 a, B3, B4 a). A folder named `ServerState` would clash with the class (C3), and the parts must share one namespace.

What `ServerState` holds (N7): the request handlers of every area, also of backups and logs, because they need the sessions, persistence, the one lock and the shared `Require`, `Fail`, `Throttle*` and `Page`. What has its own state or is pure leaves `ServerState`: a pure rule goes into a rule class in `Rules/` (or `PermissionRules` for rights), which the handler calls before it changes anything; the invariants of the persisted data are methods of `ServerData` (`FindProblem`), a piece with its own state (saving, guessing limits, upload folders, log export) becomes a class of its own that `ServerState` holds. There are no `*Service` classes and no role interfaces for `ServerState` (nothing fakes them, N3). The global lock stays one lock.

| File | Holds |
|---|---|
| `ServerState.cs` | fields, constructor, settings, `Handle` with the table `Routes` (one line per request type: its handler and whether it is a transfer, which the request budget exempts; `DispatchTests` fails for a request type without a line or without a test factory), `HandleMalformed`, the helpers without broadcast and persistence; `BroadcastUser`, `BroadcastSettings` and `BroadcastGroups` stay here next to the per-recipient helpers they use (`Info`, `Moderates`, `SettingsInfo`, `GroupInfos`) |
| `ServerState.Connections.cs` | connections: `Admit`, `Remove`, `CloseAll`, `LogRejected`, `RemoveLocked`, the statistics, `ChannelLog`, `ChannelName` |
| `ServerState.Throttling.cs` | the request limits: `Admissible`, `Throttle`, `ThrottleList`, `ThrottleHeavy`, `OnGetServerIcon` |
| `ServerState.Persistence.cs` | `WriteData` (the save action of `SaveScheduler`: the pruning, `BeforeSave`, the write), `FlushPendingSave`, `BeforeSave`, `PruneGuests` (the daily gate `nextPrune` and its log line) |
| `ServerState.Broadcast.cs` | `Broadcast`, `BroadcastExcept` |
| `ServerState.Voice.cs` | voice |
| `ServerState.Admin.cs`, `.Backup.cs`, `.Channel.cs`, `.Chat.cs`, `.Link.cs`, `.Log.cs`, `.Moderation.cs` | the command handlers of each area (formerly `Commands/*Commands.cs`) |

The saving (`Persist`, `PersistSoon` with the save timer and retry, close) is such a piece and is `SaveScheduler` (N8): `ServerState` holds it as `saves`, gives it the one lock and `WriteData`, and its timer takes the same lock; the pruning before a save is `ServerData.PruneEndedBans` and `ServerData.PruneGuests`, the count of a blocked join `BanRecord.CountBlockedAttempt`. The guessing limits are such pieces too, one class per limit: `PasswordGuessLimit` (held as `passwordGuesses`), `AdminTokenGuessLimit` (`adminTokenGuesses`), `ChannelPasswordGuessLimit` (`channelPasswordGuesses`, `internal` so a test sees that `RemoveLocked` and `CloseAll` forget a session's counts) and `NewIdentityLimit` (`newIdentities`), all under the one lock (N8); their numbers are in `ServerTuning`, their log lines stay in the handlers. A new request area gets a part `ServerState.<Area>.cs` in `State/` for its handlers only; its state and its pure rules go into classes of their own (N7).

### Client

- **Root:** `Program`, `ClientOptions`, `App`, `Splash` (F5). `Program.Main` and `App.OnFrameworkInitializationCompleted` are the composition roots (N14): `Program` holds the one clock (`Program.Time`), makes the client log and reads the settings once (`Program.Settings` with `Program.SettingsWarning`; the UI language comes from them); `App` builds the system's audio hardware (`WasapiHardware`; the view model gets none with `--no-audio`, the debug API lists its devices anyway) and hands it with them to `MainViewModel.Create`, which builds the key poller, the own tones (`SoundLibrary`, whose samples it hands to the engine), the capture filters on the client log (`NativeCaptureFilters`), the audio engine on that hardware, the trusted server fingerprints (`KnownServers`), the logo cache and the leave timer on that clock, then the view model, which lists the hardware's devices on the settings page. The view model builds none of them, reads the clock as `Time` and disposes the key poller and the engine with itself. The dialogs (`Dialogs`, `ConfirmTofu`) and the updater are set on it once the window exists, since every dialog lives in the window's overlay. Outside the two roots the client reads time only through that clock, except on a line marked with one of the kinds of N14; `OneClockTests` enforces it, and that only the roots read their hand-over statics (`Program.Log`, `Options`, `Settings`).
- **`Audio/`** keeps `AudioEngine` and `SpeakerReports` and has subfolders (F2):
  - `Capture/`: CapturePipeline, StereoInput, FrameChunker, SincResampler, SampleConversion (static: the capture buffer read as mono (`ReadMono`, with the one-sided rule) or stereo (`ReadStereo`), and the stereo `Downmix` that the capture and the echo canceller's far end in `PlaybackLoop` share)
  - `Processing/`: AutoGain, NoiseSuppressor, EchoCanceller, VolumeCurve, NativeCaptureFilters (the noise suppressor and echo canceller each new capture gets, a missing library logged once and not tried again; `MainViewModel.Create` builds it on the client log and hands it to the engine, which attaches it in `ConfigureCapture` with its `AudioEngine.EchoDelayMs`, the playback target `PlaybackLoop.TargetBufferMs` plus `WasapiHardware.SpeakerBufferMs` and `CapturePipeline.MicBufferMs`)
  - `Transmit/`: TransmitRule, TransmitMode, AudioContent, VoiceActivityDetector, LossAdapter
  - `Codec/`: AudioFormat, VoiceEncoder, VoiceDecoder
  - `Playback/`: VoiceMixer, SoftLimiter, JitterBuffer, ActiveSpeaker, ReceiveStats, PlaybackLoop (the playback thread: started on demand, it mixes the voices and the tones for the speaker up to its `TargetBufferMs` or on its own 20 ms clock, feeds the echo canceller's far end and ends when nothing is needed; the speaker is the `DeviceLifecycle`'s, closed under the engine's lock; the engine holds it in its private field `playbackLoop` and keeps the speakers event, the output level and the tests' hooks as one-line accessors)
  - `Devices/`: AudioDevice, AudioDirection, IAudioHardware, WasapiHardware, PlaybackBuffer, DeviceWatcher, DeviceLifecycle (opens a configured capture and the speaker, closes them and reopens them after device changes, under the engine's lock, which its "under gate" members assert; the engine holds it as `AudioEngine.DeviceLifecycle`, chooses the devices and restarts them through it, configures each new capture (its filters, the send path, the settings) through a callback and joins its warnings into one notice in `AudioEngine.Report`), CaptureSource (where the capture comes from, the microphone or the test tone in its place (`ToneHz`), and the capture opened from there (`Capture`): opened, set up through the engine's callback and started, and closed, under the same lock; when it opens and when the tone changes (`DeviceLifecycle.SetTone`) stay the lifecycle's, which builds it and holds it as `DeviceLifecycle.CaptureSource`)
  - `Sounds/`: SoundEvent, SoundSynth, SoundQueue, SoundPlayer (which notification sound plays, how loud, from which samples, and the preview of one sound's setting; the engine holds it as `AudioEngine.Sounds`), SoundImport, SoundLibrary
- **`Net/`** is flat; `Net/Updates/` holds the six update types (`UpdateOffer`, `UpdatePhase`, `UpdateProgress`, `UpdateCheckResult`, `UpdateChecker`, `UpdateInstaller`) (F3).
- **`Input/`** and **`Settings/`** are flat; in `Input/`, `KeyPoller` polls the keys and captures the next chord, `KeyBindingMatcher` resolves which bindings the pressed keys trigger, `KeyActionTraits` lists the actions and the hold ones, `KeyActionLabels` and `VirtualKeys` (the known key codes, the modifiers, a key's shown name) hold the texts, `DefaultPushToTalk` chooses the key of a new profile; `SettingsViewModel` gets only the poller's chord capture as a `Func`; `PasswordProtector` is in `Settings/`, as it only protects stored settings (F3, F4).
- **`Localization/`:** `ErrorTexts`, `PermissionLabels`, `Language`, `AppLanguage`, and `Formats` (§Shared helpers) (F5, H10).
- **`Diagnostics/`** (was `Debug/`, C4): `DebugApi`, `AudioDebugLog`.
- **`Logging/`:** `ClientLog` writes the client log (queued lines, one file per start, through the shared `LogFileWriter`); `ProtocolLogText` describes a server message or an own request in words, with the names from the `StateMirror` (N2).
- **`Interop/`:** `NativeMethods` (an `internal static partial class` with every `[LibraryImport]` of the client; a function used in several places is declared once) and one file per Win32 struct under its Win32 name (`POINT`, `SIZE`, `BLENDFUNCTION`, `MSG`, `BITMAPINFOHEADER`, `WNDCLASSEXW`), with unchanged layout (B13).
- **`Views/`:** `MainWindow`, `AdminView`, `ChatView`, `SettingsView`, `TitleBarView`, `ChannelDialogView` (G2, G3).
- **`Controls/`:** `BusySpinner`, `LogoMark`, `OverlayHost` (G1, G4).
- **`Dialogs/`:** `DialogFrame` (the card every dialog is shown in: icon and title, body, button bar, the busy send) with its `DialogOptions<T>` (buttons, Enter, colour, send), `DialogControls` (the icons, texts, labelled fields and stacks the dialogs are built of), one type per dialog with a `ShowAsync` (`ConnectDialog`, `TofuDialog`, `ServerPasswordDialog`, `ChannelPasswordDialog`, `BookmarkDialog`, `KeyBindingDialog`, `UpdateOfferDialog`, `ConnectionDetailsDialog`, `LeaveDialog`, `TextDialog`, `ChannelPickerDialog`, `BanDialog`, `ConfirmDialog`, `DeleteUserDialog`, `RestoreDialog`, `BackupDownloadDialog`; the channel dialog is `Views/ChannelDialogView`), all split from `SimpleDialogs` (N16, which overrides G5), `OverlayDialogs` (`Create`: the overlay's dialogs as `DialogCallbacks`), `DialogKind`, `DialogCallbacks` (H6; to be split into one delegate record per consuming class, N16), and the dialog contracts `PasswordAnswer`, `BookmarkEdit`, `ConnectChoice`, `ChannelEdit`, `BanChoice` with their enums `ChannelDialogMode`, `LeaveChoice` (H7).
- **`Animations/`:** `Motion` (lengths, curves, `Play`, the display switch and the OS reduced-motion setting as attached properties of the window, N15), `PopupMotion` (every popup's fade, slide and closing picture), `ShakeMotion`, `AnimationCounter` (the animations started from code, a test hook), `ItemMotion` (a list's entries come, leave, fly and glide), with one `EntryTracker` per list with `Enter` (which entries are new: they slide in, a first fill fades in as one, the channel tree builds up) and one `PlaceTracker` per list with `Flip` (where the entries sit, and the glide of those that moved), `LiveMotion` (the slot counter takes its numbers from the bound `ChannelSlots`, N17), `PageMotion`, `FlyGhost` (G6, G11, N18). Not `Animation/`: a namespace `OVS.Client.Animation` would hide Avalonia's class `Avalonia.Animation.Animation` from every file in `OVS.Client.*` (C# finds a namespace in the enclosing `OVS.Client` before the file's `using Avalonia.Animation;`), and several client files create that class.
- **`Behaviors/`:** `ReorderDrag`, `UserDrag`, `DragVisuals` (G7).
- **`Layout/`:** `Responsive`, `SidebarWidth`, `SidebarRow` (G8).
- **`Converters/`:** `ValueConverters` (was `Ui`, G9).
- **`Appearance/`:** `WindowAppearance` (G11).
- **`Services/`:** `IconImport`, `LeaveTimer`, `Reconnector`, `ListRefresh`, `ChunkedDownload`, `DownloadChunk` (G10, H5), `AdminTransfer` (the administration's file transfers, one at a time: a backup down or up, the chosen log files down; each request waits on the injected clock for its answer, which the server routes by request id to the route the request was sent with (it stays until the next request or `Cancel`, so the backup list after an upload's last chunk reaches the page); `AdminViewModel` builds it on the server's routed send, its route removal, notice and clock, cancels it on `Detach` and hands it to `LogsViewModel`, so both tabs share it and its progress, N13), `AnswerRoutes` (who gets the server's answers to a request, by its request id: `Add`, `Deliver`, `RemoveAll(handler)`, `Find`; held as `ServerViewModel.Routes`; a route stays until its owner removes it, since a request can be answered more than once, N13).
- **`Collections/`:** `CollectionSync`, `ListPages`, `IMotionKey` (the key `CollectionSync` syncs by) (H5, H9).
- **`Models/`:** the immutable records bound as items, `ChatEntry`, `BookmarkItem`, `MoveTarget`, `Notice`, `ChannelSlots` (a channel's user count and limit, bound to the slot counter, N17), `LogSegment`, `LogSource`, `AudioDeviceOption`, `ThemeOption`, `DisplayOption`, `LanguageOption`, `Choice` (H3), and all enums of the UI, flat: `ChatTabKind`, `NoticeKind`, `Page`, `StateChange`, `SignalRating`, `UserStatusFilter`, `UserSortOrder`, `BanStatusFilter`, `BanTypeFilter`, `BanSortOrder` (H8); `AdminPlace`, the delegate record of a place of the administration that needs a manual save, carried by its tab (N13); `BanStatus` moved to Shared `Moderation/` (N18).
- **`ViewModels/`:** only view models, 28 of them, among them the renamed `PermissionToggleViewModel`, `GroupToggleViewModel`, `GroupChoiceViewModel`, `ChatTabViewModel`, `ChatSendingViewModel`, `SoundRowViewModel`, `KeyBindingViewModel`, `ConnectionStatsViewModel`, `PendingViewModel` (H2). `Page`, `Notice`, `Choice`, `ListRefresh` and `KeyBinding` keep their names (H11).
- **Unchanged places:** `Assets/`, `Localization/*.resx`, `Styles/`, `runtimes/` (F6).

**XAML prefixes:** `v` Views, `vm` ViewModels, `m` Models, `ctl` Controls, `anim` Animations, `lay` Layout, `conv` Converters, `s` Settings, `l` Localization. XAML has no compiler `using`: a type in a new namespace needs its prefix in every file that names it, also in type selectors (`ctl|BusySpinner`).

### tools/render-icon

Root namespace `OVS.Tools`, one type per file: `LogoRenderer`, `SmallArt`. The test project links both files under `Tools\`.

### Tests

- The test folders mirror src (J2): `Shared/...`, `Server/...`, `Client/...`, each with the folders of its project; namespaces follow the folders. `Website/` and `TestSupport/` stay; `SmokeTests.cs`, `SourceLayoutTests.cs` and `PlanReferenceTests.cs` are at the root, the last two with their scanners `SourceLayout.cs` and `PlanReference.cs`.
- A test file named after a type is named `<Type>Tests.cs` and sits in that type's folder; it follows renames (J3: `ConnectionStatsViewModelTests`, `PendingViewModelTests`). A test file named after a feature keeps its name (J4) and sits in the folder of the type it mostly drives.
- Helpers used by several test files go to `TestSupport/` (`MotionWait`, `FakeHttp`, `DeviceRig`, `UiTestApp` with the `[assembly: AvaloniaTestApplication]` attribute, and the helpers of §Shared helpers). Single-use helpers get their own file next to their test (`CollectingSink`, `Unseekable`, `ShotVariantsAttribute`, `WebsiteScene`) (J1). A test of a test helper sits next to it (`TestSupport/LayoutAssertTests.cs`).
- Test methods may be renamed and split (N21); a test is split only when its name names two outcomes and its setup is cheap (no TLS server, under 100 ms, N24). Every test name has three parts, `Subject_Condition_Outcome`; without a condition the middle part names the state (N22). The test class `WebsiteShots` keeps its name for the README filter (L4).
- Links from acceptance criteria to tests live only in PLAN.md, in the Tests section of each package, in these forms: `Class > "Method"`, `File.cs > "Method"`, a header line with the methods as bullets below it, `[Theory] "Name"`, a class alone (it covers the criterion), and `file > "text"` for the website (the file exists and contains the text). `PlanReferenceTests` resolves every reference by reflection (N19); it also reads `Class.Method`, takes a method without a class from the class named before it on its line or on the last header or numbered item, skips note lines starting with "(" (such as "(as built: ...)", also indented) and a test named after "ersetzt" or "replaces", lists a reference that no test covers any more, with its reason, in `Uncovered`, and a span that looks like a test but is none, with its reason, in `NoTest`. That every checked criterion has a reference, and the exemption list for a criterion that no test can check and that has no `(manuell|manual|spike ...)` marker right after its ID (with a one-line reason each, N20), are not yet introduced.
- PLAN.md changes only in the test name and path references of its Tests sections, never in the text of an acceptance criterion (N21, L1).
- Waiting (N23): no src seam exists only for waiting. A fixed wait becomes a clock advance plus a drain; a check that something does not happen uses `AssertStaysFalse(condition, duration, reason)` (not yet introduced); poll loops use `AsyncWait`; an awaited task that a broken build would leave pending (a test on a manual clock) is bounded with `HangGuard.Bound`; the `Thread.Sleep(1)` yields in UI poll loops stay.

## Nested types

Nested types that stay nested (private and used only by their container, B9, B14 to B16):

- Server: `ServerHost.RunEnd`, `ServerConfig.FileValues`
- Shared: `LossWindow.Stream`
- Client: `MainViewModel.Restore`, `DeviceLifecycle.Endpoint`, `VoiceMixer.Speaker`, `SoundQueue.Playing`, `ItemMotion.Departure`, `LiveMotion.Follower`, `PageMotion.Switching` and `Revealer`, `EchoCanceller.Native`, `NoiseSuppressor.Native`
- Tests: `ManualTimeProvider.Timer`, `ChatViewModelTests.Fixture`, `ServerViewModelTests.Fixture`, `DebugApiTests.Instance`, `LiveMotionTests.ToleranceComparer`, `MotionCoverageTests.Walk`, `Step` and `Result`, `ReorderDragTests.Tree`, `UserDragTests.Tree`, `SendPathTests.TalkRig` and `MusicRig`, `VoiceFilterTests.PinkNoise`, `BackupTests.Host`, `FakeAudioHardware.FakeOutput`, `TestDispatcher.Context`

Hoisted, because they are not private or are used outside their container: `AdminPlace` (was `AdminViewModel.Place`), `WebsiteScene` (was `WebsiteShots.Scene`), `DeviceRig` (was `SendPathTests.DeviceRig`), `LogExport` (was `LogReader.LogExport`), `DownloadChunk` (was `ChunkedDownload.Chunk`), the Win32 structs of `Splash`, `DialogKind` (was `SimpleDialogs.Kind`) and `SmallArt` (was `LogoRenderer.SmallArt`) (B10, B11).

## Shared helpers

Where code that several places need lives. All of these are introduced by the refactoring; each replaces copies that did exactly the same thing, so none changes behaviour. Use them instead of writing another copy.

| Helper | Holds | Decision |
|---|---|---|
| `OVS.Shared.IO.SafeFile` | `DeleteQuietly` (swallows `IOException` and `UnauthorizedAccessException`), `WriteAtomic` (to `.tmp`, then move; with a `flushToDisk` parameter, each caller keeping whether it flushes), `ReadChunk` (at most `ProtocolLimits.BackupChunkBytes`) | E8, I1, I2 |
| `OVS.Shared.Logging.LogFileWriter.NameFormat` | the log name format `yyyy-MM-dd_HH-mm-ss` | I4 |
| `OVS.Shared.Protocol.Framing.ProtocolJson.Indented` | indented JSON options | I5 |
| `OVS.Shared.Protocol.Base64Size` | `Encoded(bytes)` and `Decoded(chars)`; base64 input is decoded with `Convert.TryFromBase64String` into a buffer of `Decoded` bytes, then sliced to the bytes written | I6, I7 |
| `OVS.Shared.Net.IpAddresses` | `Unmap` (IPv4-mapped IPv6 addresses to IPv4); `ToAddressGroup` (the IPv4 address or the IPv6 /64, was `ServerState.AddressGroup`) | I13, N8 |
| `OVS.Shared.Permissions.GroupRules` | `IsProtected`, `CanSave`, `CanDelete`, `CanAssign` (on ids and permissions), `IsLastAdmin`; the server's `PermissionRules` passes its `GroupRecord` to them, the client's group editor and user list call them | N1 |
| `OVS.Shared.Permissions.ChannelAccess` | `IsJoinable` (no separator), `GroupsAllow` (the group lock, admins pass), `NeedsPassword` (bypass only when joining); whether a channel is too full to enter only the server decides | N1 |
| `OVS.Shared.Permissions.ChannelCapacity` | `IsFull(maxUsers, othersInside)`: the server's join rule and the client's slot counter (`ChannelSlots.IsFull`); moved from `OVS.Server.Rules` in Step 22 | N1, N17 |
| `OVS.Shared.Moderation.BanStatusRule` | `Evaluate(liftedAt, expiresAt, now)`: `BanRecord.IsActive` and the client's ban list and user cards | N1, N18 |
| `ProtocolLimits` and `ProtocolDefaults` constants (were `ProtocolInfo`) | `ProtocolLimits`: the name and text lengths (`MaxNameLength`, `MaxTextLength`, `MaxGroupNameLength`, `MaxNicknameLength`), `MaxUsersMin`, `MaxUsersMax`, `LogDaysMax`; `ProtocolDefaults`: `MaxUsers` (was `MaxUsersDefault`), `LogDays` (was `LogDaysDefault`) | I8, I9, N18 |
| `OVS.Server.Security.StoredPassword` | the stored server and channel password: `Create` (PBKDF2), `FromStored` (any text verbatim), `Matches`, `IsValid`, `IsLegacy`; its JSON converter writes the stored text back byte-identical | E7, N10 |
| `OVS.Server.Rules` | `ChannelSettingsRule` (kind, separator, name and description, limit, group lock, password on the default channel, bitrate), `ChannelLinkRule`, `ModerationRule` (reason, duration), `ServerLimitsRule` (the ranges of `MaxUsers` and `LogDays` for the administration and `ServerConfig`); each returns a `Refusal` (code and English detail) or null | N7 |
| `ServerData.FindProblem` | the invariants a start relies on (lists, names, ids, references, an Admin member); `BackupArchive.Validate` throws its reason. Stored names use a deliberately looser rule than `TextRules` (Package 83) | N7, N11 |
| `ServerState` private helpers | `PasswordTooLong`, `NextChannelOrder`, `UsersAsAdminTuples`, `Refuse` (fails a request with a rule's `Refusal`) | I10, I11, I12 |
| `OVS.Client.Localization.Formats` | `Duration`, `Size`, `ClockTime` (`"HH:mm"`) | H10, I18 |
| `AudioFormat.HalfFrameSamples` | the half frame of 480 samples | I16 |
| `IMotionKey.KeyOf` | the motion key of an item (static interface method) | I17 |
| `FlyGhost.ScrollViewport` | the area the nearest `ScrollViewer` shows, in overlay coordinates | I21 |
| `OVS.Client.Behaviors.DragVisuals` | `Lift`, `Follow`, `Snapshot` | G7 |
| `TestSupport/KeyedRow` | the keyed row record of the collection and motion tests | J5 |
| `TestSupport/OverlayRig` | an overlay alone in a window in the simplified display: `Open`, `FindButtons` (the answer buttons of a dialog's bar), `FindMainButton`, `ReadText`, `Click` | J1 |
| `TestSupport/DialogCase`, `TestSupport/DialogCaseTable` | `DialogCase`: one way to open a dialog (type, opener, buttons, red, what Enter gives); `DialogCaseTable`: `All`, `Find`, `FindDialogTypes`; every dialog type needs a case | J1 |
| `TestSupport/KeyedList` | a list of `KeyedRow` entries with item motion: `Create`, `Show` in an animated window, `Still` (no animation running, no places noted), `Container` (the row of a key) | J1 |
| `TestSupport/Snapshots` | the `UserInfo` builder and the snapshot and `StateMirror` construction of the client tests | J5 |
| `TestSupport/MotionWait` | `Settle(int)` and `WaitFor(Func<bool>, int)`; the transform readers `OffsetX`, `OffsetY` and `Scale` | J5 |
| `TestSupport/AsyncWait` | `Until(Func<bool>)` | J5 |
| `TestSupport/HangGuard` | `Bound(task)`: a test-side guard only, an awaited task that never completes fails the test with a `TimeoutException` after `Limit` (10 s) instead of hanging the run; a passing test never waits for it | J1, N23 |
| `TestSupport/LinkMatrixActions` | `UnlinkAll` on a `LinkMatrixViewModel`: selects every row and marks every link as removed (a pending change, nothing sent); used by the leave and place tests | J5 |
| `TestSupport/RepoPaths` | the repository root, `src/OVS.Client` and `SourceFiles` (a folder's files of one pattern without `bin/`, `obj/` and `*.Designer.cs`; used by `SourceLayout`, `OneClockTests` and the XAML text check) | J5 |
| `TestSupport/StateHarness` | a `ServerState` without sockets: temporary data folder, `ManualTimeProvider`, seeded data, `Admit`, `Handle`, the decoded outbox (`Drain`, `WaitForAsync`) and `ChangeStoredData` under the lock | N7 |
| `TestSupport/MainViewModelSetup` | `Create` (through `MainViewModel.Create`, like the composition root: log and settings from the test folder, on the system clock or a given one, without audio hardware; optional key state, a fake audio hardware, dialogs, server and trusting every certificate), `Compose` (the tests' only direct call of the `MainViewModel` constructor, with parts the caller built) and `ShowServer(vm, server)` (the only place the tests set the shown server) | N13, N14 |
| `TestSupport/BrokenServerData` | `Break(data, variant)`: valid server data broken in one named way, shared by the `ServerData.FindProblem` tests and the backup restore tests | N7 |
| `TestSupport/RequestSamples` | `Factories`: one well-formed instance of every request type (ids and names no server knows), shared by `DispatchTests` and `ProtocolLogTextTests` | N7 |
| `TestSupport/PersistedFixtures` | the persisted files of 3d5c4db and the release deploy-4053cfa in `TestSupport/Fixtures`, and their date | N11 |
| `TestSupport/TempDirectoryTest` | base class for tests whose `Dispose` only deletes their temporary folder | J5 |
| `TestSupport/Rows` | row lookups and the row center of the UI tests | J5 |

Not shared by the first refactoring, because sharing would change behaviour or coupling (A4); since N1 a later commit may share one of them where the principles call for it, with the behaviour change listed: flushing every atomic write (I3), the bound flags `IsInfo`, `IsWelcome`, `IsWarning`, `IsError` of `ChatEntry` and `Notice` (a shared base record would change both records' `ToString` and equality), the native library loader of the audio filters (I16), one type for the failure counters (I14, semantics differ), I15, I19, I20, I22 (it would tie the client to the tools project), I23 (output changes), I24, I25.

## Rule checker

`tests/OVS.Tests/SourceLayoutTests.cs` with its scanner `tests/OVS.Tests/SourceLayout.cs` (K5: a custom xUnit test, no analyzer package and no new dependency) guards these rules on every test run. It checks every `.cs` and `.axaml` under `src/`, `tests/` and `tools/render-icon` (without `bin/`, `obj/` and `*.Designer.cs`):

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

Every review of new or changed code answers each point below for that change, names the file and line for every "no", and either fixes it or records why not. The red flags come from the audits of 5 and 6 October 2026 (weakest points: single responsibility, domain rules inside view models and `ServerState`, behaviour specs not linked to tests, names that say less or more than the type holds).

**Naming (N2, N5)**
- Does every new or renamed type contain only what its name says? Red flag: `Limits` also holds the server's tuning values (hence `ServerTuning`, N8).
- Red flag: a type name that is the plural of another project type name, e.g. `AudioDevices` next to `AudioDevice` (dissolved since, N12), or `KeyActions` next to `KeyAction` (split since into `KeyActionTraits`, `KeyActionLabels` and `KeyBindingMatcher`).
- Red flag: a bag name, e.g. a `ChannelHelper` or a `UserService`; name what the type holds or decides (`ChannelAccess`).
- Red flag: a method that is no verb or repeats its type's name, e.g. `ServerState.ChannelLog(...)`, which writes a line (`WriteChannelLog`), or a `BanRecord.IsBanRecordActive` (`IsActive`).
- Red flag: a `Task`-returning method without `Async`, e.g. `ServerViewModel.DropUser` (`DropUserAsync`).
- Red flag: a German log line or exception message in new or moved code, e.g. `"Channel angelegt von ..."` in a moved handler.

**DRY (don't repeat yourself)**
- Does the change copy logic that exists elsewhere (a helper in §Shared helpers, a rule in `PermissionRules`, the server's `Rules/` or the shared `GroupRules`, `ChannelAccess`, `BanStatusRule`, the settings mapping `AudioEngine.Apply` with `ApplyCaptureSettings(CapturePipeline, ClientSettings)` and `SoundPlayer.GainFor` for the sound settings, guarded by `AudioEngineSettingsTests`)? Use or extend the one place.
- Does a new setting, sound, key action or request need edits in several lists that must agree? Prefer one table or one mapping method over parallel switches.
- Red flag: the same predicate in client and server (e.g. "is this ban active"); put it in `OVS.Shared`.

**DDD (domain-driven design)**
- Is a domain rule (who may do what, when a channel is full or locked, which sound fits which event, reconnect policy) placed in a pure rule class next to its siblings, not inside a view model, a view, `ServerState` or `Session`?
- Does the code use the domain words of the protocol and PLAN.md (Channel, Link, Group, Lock, Separator, Ban, ServerMute, Deafen)?
- Are invariants of persisted records kept by one method on the record or a rule class, not only by a handler?

**TDD (test-driven development)**
- Were the tests written first and seen red (state it in the review notes)?
- Is every new rule tested where it lives, as a fast unit test, with end-to-end tests (TLS, headless UI) only for the acceptance path?
- Does every test use `ManualTimeProvider` or frame counts instead of `Task.Delay`, `Thread.Sleep`, `DateTime.Now` or wall-clock bounds (N23: clock advance plus drain, `AssertStaysFalse`, `AsyncWait`; only the `Thread.Sleep(1)` yields of UI poll loops stay)? Is no new static state shared between test classes?

**BDD (behaviour-driven development)**
- Does each acceptance criterion of the package have a test whose three-part name states the behaviour (`Subject_Condition_Outcome`, N22) and that the package's Tests section in PLAN.md references (N19; the test code carries no criterion comments or traits)?
- Does each test check one behaviour, so its name says what broke when it fails (split per N24)?
- Is each manual check listed as manual in PLAN.md?

**KISS (keep it simple)**
- Is this the simplest code that meets the criteria (no framework or generic layer with one user; no interface unless the tests use a real fake of it or it has several production implementations, N3)?
- Are deliberate shortcuts marked with a `ponytail:` comment naming the limit and the upgrade path?
- Red flags: closure state machines with several captured mutable variables, a new synchronisation scheme, mixed clock sources in one class.

**SOLID**
- **Single responsibility:** does a class gain a responsibility it did not have? Red flags: `ServerState`, `Session`, `MainViewModel`, `AdminViewModel`, `ServerViewModel`, `AudioEngine` growing further; a class over about 400 lines; state or a pure rule added to a `ServerState` part (it gets a class of its own, N7). Extract a collaborator or a child view model instead (the admin page already composes `LogsViewModel` and `LinkMatrixViewModel`).
- **Open/closed:** how many places change to add one more request, dialog, sound, setting or key action? A new `case` in a central switch needs a test that every member is handled (e.g. every `Request` subtype is dispatched).
- **Liskov substitution:** does a subtype keep every promise of its base (no `NotSupportedException` overrides, no stricter preconditions)?
- **Interface segregation:** does a consumer receive more than it uses (the whole `ServerState`, `Session`, `DialogCallbacks` or `AudioEngine`)? Pass the narrow piece (a delegate, a record, or an interface that N3 allows).
- **Dependency inversion:** are time (`TimeProvider`), the UI thread (`post`), the network (`send` or a connection factory), files and devices passed in rather than created inside the class or read from statics (`DateTime.Now`, `Environment.TickCount64`, singletons, static mutable state; the display switch, once the static `Motion.IsAnimated`, is the window's attached `Motion.DisplayProperty`)? Is there one injected `TimeProvider`, created in the composition roots `Program.Main` and `App.OnFrameworkInitializationCompleted`, for every decision clock, with every other use of a clock marked per line with one of the kinds of N14 (`OneClockTests` checks the client)?

## History

- These rules supersede the earlier deliberate decision in PLAN.md for fewer small files ("weniger Kleinstdateien", in its table of deliberate deviations from the plan text), under which several types were merged into one file on purpose (A5). PLAN.md itself is not edited for this.
- Paths named in PLAN.md Packages 1 to 142 describe the layout before the first refactoring. The text of the acceptance criteria is history and is never updated; the test name and path references in the Tests sections may follow renames and moves (N21, which overrides L1 for them).
- `git blame` history of split files is lost (M6); `.git-blame-ignore-revs` lists the pure move commits of the refactoring. Run `git config blame.ignoreRevsFile .git-blame-ignore-revs` once to make a local `git blame` skip them (GitHub reads the file by itself).

## Decisions (6 October 2026)

The IDs N1 to N30 used in this file refer to these decisions of the second refactoring. They rank above the decisions of 5 October 2026 (N1). "User" marks the user's answer; "delegated" marks a choice made under the user's instruction to pick what keeps the principles best, with the principle named.

| # | By | Decision |
|---|---|---|
| N1 | user | The design principles (DRY, DDD, TDD, BDD, KISS, SOLID) and the naming rule (N2) rank above every earlier decision; an earlier decision that contradicts them is wrong. Edited in place for this: rule 11 and L4 (test methods may be renamed), G5 (`SimpleDialogs` is split), L1 (Tests-section references in PLAN.md may follow renames, the text of the acceptance criteria stays history), A4 (behaviour changes are not limited to listed small ones), and the part rule of §ServerState |
| N2 | user | Naming rule (§General rules, rule 13): a type contains only what its name says, and a type that does more is split. No project type name is the plural of another project type name (`X`+`s`, `X`+`es`, `Xy`/`Xies`; framework types do not count). No bag names: names ending in `Helper(s)`, `Util(s)`, `Manager`, `Common`, `Misc`; `Service`, `Info` and `Data` only with a subject (wire records like `ChannelInfo` stay). Methods are verbs and do not repeat the type name. Methods that return a `Task` end in `Async` |
| N3 | user | An interface is allowed when the tests use a real fake of it or when several production implementations exist. An interface with one implementation that nothing fakes is not allowed (KISS); the concrete class or a delegate is used instead |
| N4 | user | Every commit stays reviewable in 15 to 25 minutes: at most about 400 non-mechanical changed lines. Renames are never mixed with logic in one commit |
| N5 | user | Language: identifiers, logs and exceptions are English; the wire carries codes, and the texts the client shows are localized there. New and moved text follows this at once; the rest is converted by the refactoring |
| N6 | user | Naming is enforced by the compiler where it can be: an `.editorconfig` with naming rules (the `I` prefix of interfaces, the `Async` suffix, PascalCase types and members) and `EnforceCodeStyleInBuild` (built-in analyzers, no package, so K5 holds) |
| N7 | delegated (KISS, N3) | Request handlers stay in the `ServerState` parts, also for backups and logs: they need the sessions, persistence, the reentrant lock and the shared `Require`, `Fail`, `Throttle*` and `Page`. What has its own state or is pure leaves `ServerState`. No `*Service` classes. The one lock stays. No role interfaces for `ServerState` (nothing fakes them) |
| N8 | delegated (N2) | New server names: `SaveScheduler` (save, debounce, retry, close; no ban counting), `GuessingThrottle` (server password and admin token per address; the channel password keyed per session and channel as before, cleared when a session is removed; split by the review into `PasswordGuessLimit`, `AdminTokenGuessLimit` and `ChannelPasswordGuessLimit`, one class per limit, as they share no state, N1), `NewIdentityLimit`, `UploadFolder` (upload ids, paths, cleanup), `BackupArchive` (read, manifest, entries), `LogExporter`, `LogJobQueue`, `LogDownload`, `BackupUpload` (one session's upload: id, path, received bytes, append), `SessionOutbox`, `SessionThrottle` (also owns flooding), `SessionVoice`, `ServerTuning` (was `Limits`), `ServerLogWriter` (was `ServerLogs`), `RestartSchedule`, `ServerDataMigration`. `ServerState.AddressGroup` moves to `IpAddresses`; `ServerState.ValidName` goes, its callers use the text rule |
| N9 | user | Protocol: only the C# record names change: `KickSession` (was `Kick`, wire `kick`), `BanSession` (was `Ban`, `ban`), `SetSessionServerMute` (was `SetServerMute`, `setServerMute`), `BanKnownUser` (was `BanUser`, `banUser`), `SetKnownUserServerMute` (was `SetStoredServerMute`, `setStoredServerMute`), `DeleteKnownUser` (was `DeleteUser`, `deleteUser`); their handlers follow (`OnKickSession` and so on); `MoveUser` stays. Every wire discriminator stays as it is, written explicitly on its `[JsonDerivedType]` line; no protocol version bump. A wire freeze test pins every discriminator, every `ReasonCodes` value (was `Codes`), the JSON property names of every `Message` subtype and wire record, and the member names of every enum written as a string (on the wire or in a persisted file). The documented exception: for one concept, the code name and the wire name may differ |
| N10 | user, refined delegated | `PasswordHash` becomes the value object `StoredPassword`: `Create(password)` (null for an empty password, as before), `FromStored(text)` (keeps any text verbatim; an unparseable value loads and never matches), `Matches`, `IsValid`, `IsLegacy` (uppercase hex keeps working). It is registered with `[JsonConverter]` on the type. The persisted properties are named `StoredPassword` with `[JsonPropertyName("passwordHash")]`. Backup validation calls `IsValid` from the `ServerData` checks |
| N11 | delegated (honest proof) | Persisted files are proven load-compatible with equal stored values, not byte-identical. The computed keys `hasLegacyHash` and `limits` vanish in one logic commit. Any getter added to or removed from a persisted record is listed in its commit; new checks on records are methods, never getters. Fixtures are read through `DataStore` and `BackupStore` with a `ManualTimeProvider` pinned to the fixture date, so pruning does not age them. The reverse direction (the code before reads the new file) is proven in a worktree of the commit before. Fixtures exist for 3d5c4db and for the release deploy-4053cfa |
| N12 | user and delegated (N3, N2) | `AudioDevices` is dissolved: `IAudioHardware` (faked by `FakeAudioHardware`, was `FakeAudioDevices`) with `ListActive(direction)`, `FindCurrentDeviceId` (a verb under N2, and it returns a device id, instead of the first listed `CurrentEndpoint`) and its events; `WasapiHardware` (`Open` private, no `Instance` fallback); `AudioDirection` replaces `DataFlow` in signatures; `PlaybackBuffer` is concrete; the saved-device fallback is a private method of `SettingsViewModel` |
| N13 | user and delegated | The big classes are split, each with a table that places every member: `MainViewModel`, `AdminViewModel`, `ServerViewModel`, `AudioEngine`, the `MainWindow` code-behind, `DebugApi`, `SimpleDialogs`, `Session`, `ItemMotion.Tracker`. From `MainViewModel`: `BookmarkList`, `UpdateFlow`, the server icon tracking, `EngineSync` (flags and gains), `VoiceHints`, `SelfTest`, the statistics into the existing `ConnectionStatsViewModel`, and `ConnectionController` last. `AdminViewModel`: its saving places become a delegate record `AdminPlace(HasChanges, SaveAsync, Discard)` (no interface, N3; the enum of that name goes), a shared `AdminTransfer`, answers routed by request id, and back references to `AdminViewModel` become delegates. No `IAdminChannel`. As built in Step 22: `ItemMotion.Tracker` is split into `EntryTracker` (lists with `Enter`) and `PlaceTracker` (lists with `Flip`, with the glides). As built in Step 23: `SimpleDialogs` is split into `DialogFrame`, `DialogOptions<T>`, `DialogControls` and one type per dialog; its `For` is `OverlayDialogs.Create`. As built in Step 24a: `AdminPlace(HasChanges, SaveAsync, Discard)` replaces the enum; `AdminViewModel` makes `GroupsPlace`, `LinksPlace` and `ServerPlace` through `AdminPlace.Create` (the shared save: wait for a pending save, send only a change, wait for the answer) and lists them in `Places` (saving order), each saving tab of `AdminView` carries its place in `Tag`, so `HasChanges(place)`, `SaveAsync(place)`, `Discard(place)` and the view's tab switch are gone. As built in Step 24b: `Services/AdminTransfer` holds the transfers that were `AdminViewModel`'s (the answers by request id, the timeout, the progress as `IsRunning`, `Percent`, `ProgressText`, `DownloadBackupAsync`, `DownloadLogsAsync`, `UploadBackupAsync`, `Apply`, `Cancel`) on delegates (send, notice) and the clock; the page keeps the download of a chosen backup (by its file name) and, of an upload, the Admin-group check and the restore question; `LogsViewModel` takes the transfer instead of its back reference `Owner`. As built in Step 24c1: answers are routed by request id; `ServerViewModel.SendAsync` takes a `route` (registered in `Services/AnswerRoutes`, held as `ServerViewModel.Routes`, before sending, so an answer that comes at once finds it), delivers every answer type and the server's errors (after the notice and the pending marks) to it, keeps `LastUserList` and `LastBanList`, and a route stays until its owner removes it with `Routes.RemoveAll(handler)`; the `AdminMessage` event is gone; the page sends its own requests through its private `SendRoutedAsync` to `AdminViewModel.Apply` (was `OnAdminMessage`, now without the logs and transfer cases; the card actions all routed), the logs tab through its own `SendRoutedAsync` to `LogsViewModel.Apply`, both removed on `Detach`; `AdminTransfer` sends each request with its own route (its `replies`, `ReplyFor` and `Apply` are gone) and passes the backup list after an upload's last chunk to the page (`UploadBackupAsync(path, followUp)`); the server logo's retry routes its request too. The back references to `AdminViewModel` (the user cards) follow in Step 24c2 |
| N14 | user and delegated; amended in Step 17b (delegated: DDD, DRY, BDD) | One injected clock; the composition roots are `Program.Main` and `App.OnFrameworkInitializationCompleted`. Decision clocks (the ping's round trip, the UDP fallback, display times via `GetLocalNow`, retry pauses, the per-second budget of bad voice packets and the log search limit on the server) use the injected `TimeProvider`. Any other use of a clock, timer or timed wait is marked at the end of its line with one of three kinds, and `OneClockTests` fails on an unmarked one outside the client's roots: `// clock: frame timing`, timing used only for drawing (the build-up and track windows of `ItemMotion`, since Step 22 in `EntryTracker` and `PlaceTracker`, count as frame timing); `// clock: real time`, the real-time pace of an audio or poll loop, the bound of a wait on a real socket, thread, process or HTTP request, the ping's keepalive interval (the server's idle timeout runs in real time), a voice frame's arrival (measured on the real network; the jitter logic is tested through explicit arrivals) and the update's progress throttle; `// clock: debug`, the debug API and the audio debug log. A comma and free text may follow the kind. The connect seam is an injected `Func<ConnectChoice, CancellationToken, Task<ClientConnection>>` |
| N15 | delegated (behaviour-neutral) | Two attached properties: the user's display mode (from `Motion.IsAnimated`) and the reduced-motion setting of the OS (from `ReorderDrag.ReducedMotion`); default "animated"; tests set them per window; `Motion.Changed` becomes a property-changed subscription; `Shake` takes a `Visual`. `MotionCollection` stays for the static animation counters. As built in Step 21b: `Motion.DisplayProperty` and `Motion.ReducedProperty` (inherited, set by `Motion.Apply` and `Motion.FollowReduced`), read through `IsAnimated(Visual)`, `IsReduced(Visual)` and `ChooseDuration(Visual, TimeSpan)` (was `Duration`); the OS query is the private `App.IsReducedBySystem` |
| N16 | delegated | `DialogCallbacks` is split per consumer after the `MainViewModel` split, one delegate record per consuming class. `SimpleDialogs` is split into one type per dialog plus `DialogFrame`. As built in Step 23: 16 dialog types in `Dialogs/` with a `ShowAsync` each (the channel dialog was `Views/ChannelDialogView` already), the frame's options in `DialogOptions<T>`, the shared building blocks in `DialogControls`, the overlay's callbacks made by `OverlayDialogs.Create` (the record stays a contract without view types) |
| N17 | delegated (DRY) | `LiveMotion` binds the numbers instead of parsing the displayed "n/max" text. As built in Step 22: `LiveMotion.Slots` is bound to `ChannelViewModel.Slots`, a `ChannelSlots` (the user count and the limit, `IsFull`, and the text as `ToString`, which `ChannelViewModel.SlotText` shows) |
| N18 | delegated (N2) | Client names: `TrustedServerFingerprints` (was `KnownServers`), `IdentityFile` and `ClientPaths` (was `ClientStorage`), `UiCulture` (was `Language`), `AudioContent` (was `Transmission`; the settings property is `ClientSettings.AudioContent` with `[JsonPropertyName("transmission")]`, so the key of settings.json is unchanged), `TransmitRule` (was `TransmitController`, the name of the project's rule holders like `BanStatusRule`; its method is `ChooseTarget` (was `Decide`), and the same verb names the path: `CapturePipeline.ChooseTarget` (was `DecideTarget`) and `AudioEngine.ChooseTarget` (was `Decide`)), `VoiceMixer` (was `Mixer`, not `SoundMixer`: it mixes the remote voices only and `Sound` already means the notification tones of `Audio/Sounds` and `AudioEngine.Sounds`; the `AudioEngine.Mixer` property keeps its name), `SampleConversion` (not a rename: the static `CapturePipeline.ToMono`/`ToStereo`/`ToFloat`/`DominantChannel` were extracted into it as `ReadMono`/`ReadStereo` and the private `ReadInterleaved`/`FindDominantChannel` (verbs under N2), with `Downmix` for the two copies of the stereo averaging), `KeyActionTraits`, `KeyActionLabels`, `KeyBindingMatcher`, `VirtualKeys`, `DefaultPushToTalk`, `ProtocolLogText`, `PopupMotion`, `ShakeMotion`, `AnimationCounter`, `BackgroundTransparency`, `AllowedGroupViewModel`, `GroupAssignmentViewModel`, `PendingActionViewModel`, `OutgoingChatMessageViewModel`, `SpeakerReportThrottle`, `PagedListCollector<T>`, `AvatarConverters`, `NumberConverters`, `LogLineConverters` (every member placed), and `Choice<T>` where an option has only a value and a label. Tool: `SmallIconMetrics`. Shared: `LogFileWriter` (was `LogFiles`), `ReasonCodes` (was `Codes`; chosen over `ProtocolCodes` because every value is the reason of an `Error`, `Rejected` or `Disconnected`, while the protocol also has other codes such as discriminators and packet types), `WireProtocol` (the version), `ProtocolLimits` and `ProtocolDefaults` (was `ProtocolInfo`), `TextRules.NormalizeName`/`NormalizeText`, `RelayPayload.TryReadRanges`/`TryReadArrays`, `PacketDirection`, `ServerIconFormat.ComputeHash`, `CertFingerprint.Compute`/`ComputeHex` (verbs under N2 and N1 instead of the first listed `ValidName`/`ValidText`, `IdOf`, `HashBytes`/`Text`); `BanStatus` moves from the client to Shared. The file names on disk (`settings.json`, `known_servers.json`, `identity.key`) never change |
| N19 | user | Links from acceptance criteria to tests live only in PLAN.md, checked by a test that resolves them by reflection. Forms: `Class > "Method"`, `File.cs > "Method"`, the nested header-plus-bullets form, `[Theory] "Name"`, class-only references (they count as covering a criterion), website references (`file > "text"`: the file exists and contains the text) |
| N20 | delegated (keeps N21) | Acceptance criteria that no test can check and that carry no `(manuell\|manual\|spike ...)` marker right after the ID go into an exemption list in the test file, each with a one-line reason |
| N21 | user | Test methods may be renamed and split; PLAN.md may change in test name and path references only, never in the text of an acceptance criterion |
| N22 | user | Every test name has three parts, `Subject_Condition_Outcome`; without a condition the middle part names the state. A reflection test enforces it once all names follow it |
| N23 | delegated (TDD, KISS) | No src seams only for waiting. Fixed waits become a clock advance plus a drain; the rest goes through `AssertStaysFalse(condition, duration, reason)`; poll loops use `AsyncWait`; the `Thread.Sleep(1)` yields in UI poll loops stay |
| N24 | delegated (KISS) | A test is split only when its name names two outcomes and its setup is cheap (no TLS server, under 100 ms) |
| N25 | user | Working mode as in the first refactoring: an implementing agent per step, an independent review within the time budget, and the orchestrating agent commits on `main` after the review in the repository's commit message format without a trailer (the user authorized these commits for the refactoring; implementers never commit). The agent instructions are kept outside the repository with the working plan (K4), with a checklist per kind of step |
| N26 | user | Nothing is pushed by the refactoring; the user pushes by hand. Every commit message stands alone as a release note line, since every push to `main` is a release |
| N27 | delegated | A step that fails its review twice is reset to the last step commit (local, unpushed) and re-planned or split, never patched a third time |
| N28 | delegated | Local tags `refactor2-<phase>` at each phase end; at each tag the suite runs three times and `docker build .` runs. These are safe stops where `main` is consistent |
| N29 | delegated | No feature package of PLAN.md is started before the refactoring is finished. The branch `safety/before-restoring-94` is stale (its content is on `main`) and is left for the user to delete |
| N30 | delegated (real-time audio) | Every commit that touches `Audio/`, `Voice/` or the clock runs the allocation tests (`IdleCostTests`, `SendPathTests`, `VoiceFilterTests`) alone three times. No new lock, closure or delegate allocation on the capture or playback threads; per frame only `TimeProvider.GetTimestamp`. The user is offered a manual voice check with two clients at the audio tags |

## Decisions (5 October 2026)

The question IDs (A1 to M9) used in this file refer to these decisions. Where a decision of 6 October 2026 overrides one of them (N1), its row says so. Rows that only describe where the first refactoring moved or named a type record history; the 6 October decisions may rename or split those types later.

Answered by the user:

| # | Answer | Decision |
|---|---|---|
| A1 | c | Scope: `src/`, `tests/` and `tools/render-icon` |
| A2 | b | Renames are in scope (needed for the View and ViewModel suffixes) |
| A3 | a | DRY is part of the series, in separate commits after the pure moves |
| A4 | b | Small behaviour changes only when listed and approved one by one. Overridden by N1: behaviour changes where the design principles need it, listed in the commit that makes it |
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
| B10 | Hoist all four: `AdminPlace` (was `AdminViewModel.Place`), `MalformedRequest` (removed later, see E6), `WebsiteScene` (was `WebsiteShots.Scene`), `DeviceRig` (to `TestSupport/`) | They are used outside their container, so B9 does not cover them |
| B11 | Hoist both: `LogExport`, `DownloadChunk` (was `ChunkedDownload.Chunk`) | They are not private, so B9 does not cover them |
| B14 | `EchoCanceller.Native` and `NoiseSuppressor.Native` stay nested | Private and single use (B9) |
| B15 | `LossWindow.Stream` and `ManualTimeProvider.Timer` stay nested and unchanged | Private and single use; the clash would only matter if they were hoisted |
| B13 detail | An `Interop/` folder: `NativeMethods` (all user32/gdi imports of Splash, KeyPoller, ReorderDrag; since Step 21b the reduced-motion query of ReorderDrag is in `App`) and one file per Win32 struct, keeping the Win32 names (`POINT.cs` ...) | B13 b plus B1 |
| C4 detail | `Debug/` becomes `Diagnostics/` (namespace `OVS.Client.Diagnostics`). The `.gitignore` exception for `src/OVS.Client/Debug/` goes away | C4 b |
| D1 | One record per file | B1 |
| D2/D3 | `Protocol/Messages/<Area>/`, with `Message.cs` and `Request.cs` in `Protocol/Messages/`. The areas:<br>• `Connection`: Ping, Pong, Error, ClientHello, Challenge, ClientProof, Welcome, Rejected, Disconnected, ServerSnapshot<br>• `VoiceRelay`: VoiceUp, VoiceDown<br>• `Channels`<br>• `Users`: incl. MoveUser, SetSelfState, ListUsers, UserList<br>• `Groups`: incl. RedeemAdminToken<br>• `ServerInfo`: ServerSettingsInfo, ServerLimits, UpdateServerSettings, ServerSettingsChanged, incl. the server icon<br>• `ServerBackups`, `LogAccess` (the log messages), `Moderation`, `Links`, `Chat` | The feature folders follow the existing section comments. `Backups`, `BackupFiles` (both match `Backup*/`) and `Logs` are git-ignored names (C2). `Voice` would clash with `OVS.Shared.Voice`, `LogFiles` with the class `OVS.Shared.Logging.LogFiles` (now `LogFileWriter`), `ServerSettings` with the server class `ServerSettings` (C3) |
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
| E5 | `RateLimiter` goes to `Throttling/`; `ServerTuning.cs` (was `Limits.cs`) stays in the root | `ServerTuning.cs` holds all limits, not only throttling |
| E6 | `MalformedRequest` becomes a top-level internal type in the server root. Overridden by N1: the stand-in request is gone; `ControlServer` passes a malformed frame to `ServerState.HandleMalformed`, which keeps the session check and the request limits | B10; KISS: no request type that never reaches a handler |
| E7 | PBKDF2 hashing moves to `Security/StoredPassword.cs` (server; introduced as `PasswordHash`, a value object since N10); the stored hash format stays byte-identical | DRY (A3), neutral |
| E8 | `DeleteQuietly`, the atomic write (with a flush parameter) and `ReadChunk` move to Shared `IO/SafeFile.cs` (namespace `OVS.Shared.IO`) | DRY across Client and Server |
| F1 | Yes, everywhere (A1 c) | |
| F2 | `Audio/` gets the subfolders `Capture`, `Processing`, `Transmit`, `Codec`, `Playback`, `Devices` (planned as AudioDevices, IAudioDeviceFactory, WasapiDevices; N12 dissolved and renamed them) and `Sounds`; their types are listed under Client > `Audio/`; `AudioEngine` and `SpeakerReports` stay in `Audio/` | 30 files flat in one folder would be hard to read |
| F3 | `Net/Updates/` for the 6 update types; `Input/` and `Settings/` stay flat | |
| F4 | `PasswordProtector` stays in `Settings/` | It only protects stored settings |
| F5 | `Program`, `ClientOptions`, `App`, `Splash` stay in the root; `ErrorTexts` and `PermissionLabels` go to `Localization/` | |
| F6 | `Assets/`, `Localization/*.resx`, `Styles/` and `runtimes/` stay where they are | Their paths are fixed in the csproj, XAML, tools and tests |
| G1 | Reusable controls go to `Controls/` and keep their names (`BusySpinner`, `LogoMark`, `OverlayHost`); views get the `View` suffix | |
| G2 | `TitleBar` becomes `Views/TitleBarView` | It binds `MainViewModel`, so it is a view |
| G3 | `ChannelDialog` becomes `Views/ChannelDialogView` | |
| G4 | `OverlayHost` goes to `Controls/` | |
| G5 | `SimpleDialogs` goes to `Dialogs/` with its name kept. Overridden by N1 and N16: it is split into `DialogFrame` and one type per dialog (done in Step 23) | The move kept it whole; single responsibility now ranks above avoiding the redesign |
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
| H5 | `Services/`: `LeaveTimer`, `Reconnector`, `ListRefresh`, `ChunkedDownload`, `IconImport`. `Collections/`: `CollectionSync`, `ListPages<T>`, `IMotionKey` | `IMotionKey` is the key `CollectionSync` syncs by. Since Step 24b `Services/` also holds `AdminTransfer` (N13) |
| H6 | `Dialogs` becomes `Dialogs/DialogCallbacks`, with the same shape; N16 later splits it per consumer | A class `Dialogs` would clash with its folder (C3) |
| H7 | Dialog contracts and their enums go to `Dialogs/` | |
| H8 | All enums go to `Models/` (flat); `AdminViewModel.Place` becomes `AdminPlace` (since Step 24a no enum: the delegate record of N13) | H1, B10 |
| H9 | See H5 | |
| H10 | `Duration` and `Size` move to `Localization/Formats.cs`, behaviour-identical | DRY |
| H11 | No further renames for clashes (`Page`, `Notice`, `Choice`, `ListRefresh` and `KeyBinding` stay); renames under the naming rule follow N2 and N18 | No real clash remains after the moves |
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
| L1 | PLAN.md paths stay as history, plus one note (now §History of this file). Overridden in part by N1 and N21: the test name and path references in the Tests sections may follow renames; the text of the acceptance criteria stays history | |
| L2 | Updated in the same commit as the move | |
| L3 | Comments name types instead of paths | |
| L4 | `WebsiteShots` keeps its name. The earlier "no test method changes its name" (rule 11, §Tests) is overridden by N1 and N21 | |
| M1 | Numbered steps of a working plan | First planned as numbered packages in PLAN.md; moved out of PLAN.md on 5 October 2026, as the refactoring is a necessary side refactor, not a set of official packages. The working plan is kept outside the repository |
| M2 | Per project, each step behaviour-neutral and green: safety nets, Shared, Server, Client (non-UI), Client (UI), Tests, DRY, enforcement. Describes the first refactoring; overridden by N1 and N4: renames and moves stay behaviour-neutral, logic commits list their behaviour changes | Each file is split and moved once, never moved twice |
| M3 | One commit per step | |
| M4 | Commit autonomously after each step's independent review | The user's answer |
| M5 | Directly on `main`; the `safety/before-restoring-94` branch is left untouched | |
| M6 | Lost blame history is accepted; a `.git-blame-ignore-revs` lists the refactoring commits | |
| M7 | An independent review per step | |
| M8 | The tool artifacts are cleaned up | |
| M9 | The server image is rebuilt and restarted at the end | |
