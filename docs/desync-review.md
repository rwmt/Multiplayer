# Desync review: changes on `fix/desync-review`

This document lists every change on the `fix/desync-review` branch, grouped by area. It is written for reviewing the
pull request and can be removed before merging.

## Scope

The branch has 182 commits on top of `dev` at `e760d30` (192 files, +8957/−1468). The work was done in three stages:

1. A full review of the code that decides whether clients stay in sync: the command log and join points, the server
   and networking, RNG and simulation contexts, the sync framework, sessions, multifaction faction contexts, async
   time and the tick loop, the storyteller and quests, and the Odyssey gravship.
2. A review of all 28 open pull requests and about 60 open issues against the branch. Useful parts of open PRs were
   ported or reimplemented; the entries below credit them.
3. Fixes for the open issues that were still not addressed after stage 2.

Most of the desyncs fixed here fall into a few groups:

- Code that runs on only some clients (the interface, the client that issued a command, the camera or view, local
  settings, the game language) changed shared state or used the synced RNG.
- Global state was set in a Harmony prefix and restored in a postfix, which does not run when the original method
  throws.
- Joiners and reloading clients reached a state by a different path than live clients (command order, `ProgramState`,
  state that is not saved).
- In multifaction, simulation code ran with the local player's faction data instead of a faction chosen by the
  simulation.
- The interface and the simulation shared a cache, so a value cached by the interface on one client was later read by
  the simulation.

Conventions: hashes are commits on `fix/desync-review`; `#N` are issues and pull requests in rwmt/Multiplayer.
"Fixes #N" means the listed commits fully fix the issue; "part of #N" means they address part of it.

Terms used below:

- **Sync reference**: the client whose state counts as correct: the arbiter, else the host, else (on a standalone
  server) the longest-connected player that is caught up and in sync.
- **Join point**: a saved world plus the command log after it, which joiners load and replay.
- **Sim long event**: a long event queued during a tick or command, which runs later in the frame, outside of it.
- **Forced faction push**: `map.PushFaction(f, force: true)`, which (after `c3889fa`) applies a faction's data to the
  world and to every map.
- **`LocalOnlyScope`**: runs code with a pushed RNG state and local unique ids, with desync traces ignored
  (`5a92b1d`).

## 1. Joining, command log and join points

### Command log

- **One globally ordered command log for joiners.** Live clients run all commands from one queue in arrival order
  (#734), but the server stored commands per map and joiners replayed them "world first, then each map", so any tick
  with commands for two tickables ran in a different order on joiners (and `AsyncTimeComp.FinalizeInit` could drop or
  duplicate map commands received during the load). The server now keeps one ordered list (`Saved/cmds.dat` on
  standalone), clients build the queue from it in `Loader.PostLoad`, `RunCmds` drops a command for a past tick with an
  error instead of blocking forever, and the `consistentCommandOrder` tweak value is removed; protocol bumped to 57.
  `7d09265`, test `0bd4e06`
- **Command size is checked before the command is logged.** The server's rebroadcast adds a 17-byte header, so a
  client command near the 65535-byte limit made `Send` throw after the command was already in the log, and joiners
  executed a command that live clients never received. Command data is now capped at what fits the rebroadcast and
  rejected before logging. `ec1900b` (the cap was later raised to 1 MiB by fragmented commands, `f8dd311`)
- **Saved standalone command logs from other protocols are discarded.** Nothing recorded which protocol wrote the
  command log in `Saved/`, so a log from protocol 56 could be replayed with misread sync data. The protocol is now
  written to `Saved/cmds_protocol.txt` and a log without a matching protocol is discarded with an error, while the
  saved world, maps and session still load; legacy per-map logs are no longer merged. `2947cb7`

### Join points

- **Join points are atomic and have one designated uploader.** On standalone servers a join point reloaded only the
  issuing client (#874), but a reload is not a no-op (LoadPatch's extra tick, the seed long event, statics reset by
  `MultiplayerGame`, state that isn't saved), so that client drifted from the others and joiners loading its save
  drifted from live clients. Now every client reloads, as in hosted mode; the server names the uploader (the sync
  reference) in `CreateJoinPoint`, accepts only its upload for that tick and swaps world, maps and command log
  together. A join point is aborted when the uploader leaves, desyncs or rejoins, or after 60 s without progress;
  forced join points are rate-limited to one per 10 s; minute-based standalone autosaves are issued by the server at a
  tick; world-travel join point requests are ignored. `bdf1995`
- **Join points don't time out while the uploader is saving.** Only upload fragments counted as progress, so a big
  game whose save took over a minute always had its join point aborted. Missing keep-alive replies while the
  uploader's main thread is blocked now count as progress; a client that hangs for good is still dropped by the
  10-minute keep-alive timeout. `940a691`
- **One join point per saved tick.** With day-based autosaves every client requests a join point after its save, and a
  slower client started a second one, reloading everyone again. Save requests now carry the saved state's game timer
  and the server ignores requests already covered by the current or last completed join point. `edd7d3c`
- **The arbiter runs in-game synchronous long events.** Such an event only starts after its loading window has been
  drawn, and in batch mode windows are never drawn, so the arbiter never ran join point creation: it stopped
  simulating and the server paused everyone waiting for it. On `dev` an arbiter game froze for everyone as soon as
  someone joined. `c877df8`
- **World data for joiners stays within the fragmented packet limit.** `Server_WorldData` carries the save, the log
  and up to 10 stored sync opinions, which with traces could exceed 32 MiB; `SendFragmented` then threw and every
  later joiner got a state exception disconnect. Stored opinions are capped at 8 MiB (the newest are kept) and only
  those that fit are sent; if the data is still too big the joiner gets a message with the sizes. `37302bd`
- **The standalone join point test can fail.** Auto join points were off and the existing player wasn't a valid
  uploader, so the test passed either way. `324df63`

### Joining handshake and compatibility check

- **Init data requests no longer hang every later join.** A joiner disconnecting during the handshake left the server
  in `InitDataState.Requested`, so every later joiner waited forever. `RequestInitData` now always completes,
  handshake waits time out (2 min for automatic replies, 15 min for replies behind user input), the `rawMods` limit is
  restored (4 MiB compressed, also for the forwarded copy), and the flow stops right after a protocol or username
  disconnect. `7c88a31`
- **Players who left while waiting for init data stop their join flow** instead of waiting for join data on a dead
  connection (`c189a0c`). The init data upload, which carries the mod list and synced configs, gets 5 minutes instead
  of 2 (`1b011ec`).
- **Reloads run under `ProgramState.MapInitializing`.** Join point and resync reloads kept `ProgramState.Playing`,
  unlike a normal load or a joiner, so load-time code behaved differently on reloading clients (e.g.
  `GenSpawn.Spawn`'s per-cell item limit moved or destroyed stacked items only there). Fixes #854. `d67d559`
- **Different Multiplayer builds and missing def types are caught.** Mod file hashes covered only `Assemblies/`, but
  Multiplayer's own DLLs are in `AssembliesCustom/`, and the def check only looked at def types the client sent. Those
  DLLs are hashed now (shown as a file mismatch, "Connect anyway" still works), and the server also reports def types
  missing on the client (`ServerJoinDataPacket.missingDefs`). `8205395`
- **Line endings are ignored when comparing mods.** Synced mod configs and Defs/Patches XML hashes were compared byte
  by byte, so the same mod on Windows (CRLF) and Linux (LF) showed a mismatch; assemblies are still hashed raw. Config
  comparison from #961. Fixes #386. `0198180`, `03f3ae5`
- **A different game language shows in the mismatch window.** Translated text is generated and saved during the
  simulation and name generation retries depend on it, so different languages can desync. The init data provider's
  language is sent in the join data and compared like the other entries. From #961; part of #264. `92af9a9`

## 2. Server and networking

### Threads and lifecycle

- **The server join flow stays on the server thread.** `TypedPacket<T>()` used `ContinueWith` without
  `ExecuteSynchronously` and the server thread has no `SynchronizationContext`, so everything after the first awaited
  typed packet in `ServerJoiningState` (pausing, join point creation, `OnJoin`, disconnects) ran on a thread-pool
  thread at the same time as the server loop, racing `CommandHandler.Send` and the player list. Typed waits now resume
  inline on the packet handler's thread, and `MultiplayerServer.CheckServerThread()` logs off-thread calls. `d2cf042`
- **The server stops once, on its own thread.** `TryStop` ran twice and cleared the static instance unconditionally,
  so the rest of a server iteration could run without an instance (the `FreezeManager.Tick` NRE after which the game
  timer never advanced). It is now latched, clears the instance only if it is still the same server, and from other
  threads waits up to 5 s for the server thread. Reimplements #993 (@MhaWay). Likely fixes the NRE of #991 (its "players too far behind" symptom needs a retest). `cd6d1f5`

### Client input validation

- **Server-only commands and foreign faction changes are rejected.** The server forwarded any command type for any map
  id, so a modified client could send `InitPlayerData` (granting dev mode) or spam `PauseAll`/`CreateJoinPoint`, and
  anyone could move anyone to any faction. Unknown types and map ids outside the type's scope now disconnect the
  sender; players may change only their own faction and moving others is host-only. `dd969f9`
- **Remote lengths are validated before allocating.** A length prefix of `int.MaxValue` made the host try to allocate
  2 GB, and `PacketReader.BindBytes` ignored `maxLength` (so the mod list limit from #878 never applied). Reads and
  binds now check lengths against the remaining data, and `Gzipped` caps the decompressed size at 64 MiB; no wire
  format change. `dba4ff2`
- **String limits count UTF-8 bytes.** The writer checked string limits by char count and the reader by UTF-8 bytes,
  so a Steam name of 17+ Cyrillic letters passed the host's check and made every receiver of the player list drop the
  connection. Steam names are bounded by Steam's own byte limit (from #961) and array binds reject negative lengths.
  `41c8fab`
- **Unresponsive players time out; client timing reports are clamped.** `keepAliveAt` was never read, so a hung game
  stayed connected and its `ticksBehind` paused everyone indefinitely; players in game that don't answer keep-alives
  for 10 minutes are now dropped (the in-memory host connection is exempt). `ticksBehind` and `frameTime` are clamped
  and NaN-safe: a NaN frame time used to stop the server's game timer for good. `b2fb3f1`

### Packets

- **Partially received fragmented packets are dropped on a state change.** The fragment buffer survived connection
  state changes, so after a change in the middle of a transfer the next fragmented packet failed with "High number of
  fragmented packets at once!" and the rejoin broke. Partial packets are discarded and their remaining fragments
  skipped by fragment id. `e3edd35`
- **Big commands are sent fragmented.** Commands were single packets of at most 64 KiB, so big actions (designating or
  pasting a large area, reforming an ideoligion) failed with a send exception halfway. Commands that don't fit are
  sent fragmented on the same reliable ordered channel, capped at 1 MiB, and the client shows a message instead of
  throwing. Fixes #415. `f8dd311`
- **Selections over 200 things are split.** The selection packet carries at most 200 ids per list, so bigger changes
  (possible with mods) threw in the writer every 0.2 s; they are now split into several packets. Fixes #377. `9b94384`

### Standalone servers

- **Desyncs are detected on standalone servers.** Only the host or arbiter sent sync opinions, so a standalone server
  never compared anything. The server now forwards the opinions of the sync reference, re-evaluates it when a player
  becomes ready, desyncs, rejoins or leaves, and on standalone tells the old and new reference with a new
  `Server_SyncReference` packet (appended, existing packet ids unchanged), so only that client uploads opinions.
  `6c48789`

### Steam and LiteNetLib

- **Steam reconnects work and closed P2P sessions are freed.** A join packet from a remote that still had a connection
  was dropped, so a player who rejoined before the old connection timed out waited forever; P2P sessions were never
  closed, and a rejected prompt made the host ignore all later requests from that player. The stale connection is
  replaced, sessions are closed on disconnect (3 s after a goodbye so it gets delivered), a rejected player's requests
  are ignored for 30 s (`49cd635`), and due closes run after the tick's packets are read so a waiting reconnect
  cancels them (`799efd4`). Steam part of #958 (@romangr); its same-IP replacement for direct connections was left out
  because a player behind the same NAT could kick another. Fixes #843. `8ce4386`, `49cd635`, `799efd4`
- **Sends to a disconnecting LiteNetLib peer aren't reported as errors.** LiteNetLib changes the peer state on its own
  thread, so the rest of the server tick still sent to the peer and logged an error (and failed `ServerTest` at
  times). These are logged at verbose level now. Reimplements #977 (@M-r-A). `6b109e5`

## 3. Determinism

### RNG isolation

- **Sound and music don't use the synced RNG.** `Fire.TickInterval` reaches a `SoundSizeAggregator` constructor
  (`Rand.Value`) only when no aggregated sustainer exists locally, which depends on the local sound manager, so the
  map RNG advanced on some clients only. The RNG is pushed and popped around the sound entry points reachable from
  ticks and around the music manager triggers. Part of #618. `675ec35`
- **`Rand` push/pop is exception safe.** Several seeding patches (`SeedGameLoad`, `SeedMapLoad`,
  `SeedMapFinalizeLoading`, `SeedCaravanEnter`, `SeedGrammar`, `SeedPawnGraphics`, `TryGetNearbyUnseenCell`,
  `DeterministicUndercaveRockCollapse`) popped in a postfix, so an exception left an orphaned entry that the enclosing
  tick saved as its own state, and the extra stack depth turned desync traces off for the session. They pop in
  finalizers now, and ticks and commands unwind leftover pushes; `TryGetNearbyUnseenCell` is also seeded with the pawn
  id. `f5a41e8`
- **Quest name generation retries are seeded.** The `GrammarRequest` overload of `NameGenerator.GenerateName` and
  `QuestNode_ResolveQuestName` retry depending on the generated text, so on the game language. Port of #980
  (@romangr); part of #264. `b815de5`

### Simulation contexts

- **Sim long events run in a deterministic context.** Only the action of such an event was seeded: the faction context
  was lost (under multifaction each client generated maps for its own faction), actions deferred with
  `ExecuteWhenFinished` counted as interface code (unseeded RNG, local unique ids, sync methods sending commands), and
  events could run on a worker thread sharing `Rand` with the UI. A `SimLongEventContext` (RNG seed and, in
  multifaction, the faction) is now captured when the event is queued and applied around the whole update with
  `InInterface` false, and such events run synchronously. `389678c` takes the seed without advancing the RNG (some
  events are queued on the issuer only), skips autosave events and restores the context in `finally`. Part of #518 and
  #621. `619bc19`, `389678c`
- **The ship countdown ends in the world context.** `ConstantTicker` ended it outside any tickable's context, so
  `ShipCountdown.CountdownEnded` used the unsynced global RNG; it now runs inside the world's context, with the ship's
  faction pushed (`0030d36`). `9427b7d`

### Hashing and ordering

- **Deterministic `HashCode.Combine` patches are applied early and always.** They switched on `Multiplayer.Client !=
  null`, so the hash function changed under containers alive across connecting and disconnecting, and containers
  filled during def loading kept a per-process order. `6e8249c`
- **Inverted zone/plan cell shuffle check.** The transpiler emitted a bitwise `not`, so the condition was always true
  and cells were never shuffled, even in singleplayer. Singleplayer gets vanilla's shuffle back; MP keeps the existing
  no-shuffle behavior, since the shuffled flag isn't saved. `83d0cc8`
- **Job-loop counter for pawns without a map.** `Pawn_JobTracker.StartJob`'s frame count was replaced by the map's
  event counter, but unspawned pawns (carried, in containers or caravans) fell back to the frame count, which differs
  between clients. It now uses `MapHeld` with the tick timer as fallback. `fa7f328`

### Threads

- **Path jobs complete right after `PathFinderTick`.** The only guard against path jobs racing main-thread changes
  (#900) ran after `MapPreTick`; jobs are now also completed in a `PathFinderTick` postfix so they can't overlap the
  rest of `MapPreTick`. `9a7c278`
- **`FastTileFinder.Closest` is deterministic.** It kept a closest tile per worker thread, so equally close tiles went
  to whichever thread found them first, and a cached result or cache regeneration triggered by one client's interface
  could be reused by the simulation. `Closest` runs in a single batch like `Query`, and the simulation always
  recomputes and regenerates a cache that the interface regenerated (`f145f9a`). Likely fixes #648 (the reported desync came from this path; not reproduced). `52cd03d`, `f145f9a`

### Transpilers

- **Fragile determinism transpilers fail loudly.** `TryGetRandomUnusedSolidName` loads its list the way the `Shuffle`
  call does instead of hardcoding `ldloc.1`, the `TargetInfo`/`GlobalTargetInfo` hash code and
  `ProcessPostTickVisuals` patches log when nothing was patched, and the plan color gizmo lambda is found with
  `MpMethodUtil.GetLambda` instead of a hardcoded display class name. These errors are shown before hosting or joining
  (`d4349f2`). `2e921f5`

### Caches shared by the interface and the simulation

- **Item accessibility cache per simulation step.** `PlayerItemAccessibilityUtility` reused its `Rand`-consuming scan
  within a frame, so quest generation rescanned a client-dependent number of times and could read a scan made by the
  interface. In MP it is reused only within one tick or command, and sim long events count as steps (`a3ccabd`). From
  #961. `9b5661a`, `a3ccabd`
- **The goodwill situation cache isn't filled from the interface.** On a cache miss `GetSituations` filled the shared
  cache and ran `CheckHostilityChanged`, which from the factions tab happened on one client only. The interface now
  gets uncached values. From #961. `d3c7bb1`
- **Stat cache staleness is judged with the thing's own time.** With async time the interface checked
  `StatWorker.GetValue`'s shared cache against the viewed map's or the world's time and could replace an entry that
  the simulation would still have used. In the interface the time of the thing's map is used. `5923645`
- **The UI tile temperature cache is ticked.** It was only ticked when the real comp ticked in the interface, which
  never happens, so its entries were never refreshed or evicted; it is now ticked at most once per frame when read,
  and guarded against exceptions. `873f837`, `977e1bc`
- The grav engine cache is covered under the Odyssey section (`64e3aef`).

## 4. Sync framework

### Field watches and markers

- **Watched fields are restored even if the watched code throws.** The watch postfix didn't run when the UI method
  threw, leaving the user's change (or the buffered value) in that client's simulation and the watch stack unbalanced.
  Watches now use finalizers and `try/finally`, always pop the whole scope, flush leaked scopes once per frame (also
  at the end of `UIRootOnGUI`/`UIRootUpdate`, logged once), skip nested watches of an already watched field (as
  `Bill_Production.paused` is), and clear the stack when MP stops. `a59e1c8`, `109b5f2`, `c436e65`
- **Sync state flags are reset in finalizers.** A new `MpFinalizer` attribute replaces postfixes where a skipped
  postfix broke state for the session: `StorageSettingsClipboard.Copy` left `Multiplayer.dontSync` set (every later
  sync handler ran locally), `ThingFilterMarkers` left a stale filter context, and the growth moment letter's RNG push
  and the manual priorities marker could leak. `2aec6ac`, `417c0ac`

### Serializers and sync workers

- **Sync serializer reader and writer stay aligned.** Writers of implementation-indexed types wrote `0xFFFF` for an
  unknown type followed by the object's data, which the reader didn't consume, and a failing implicit sync worker
  truncated the writer to before the parent type's data while the reader re-read it. The writer now throws so the
  command isn't sent, and both sides roll back to the same position. `5238392`
- **World pawns and genes sync reliably.** Pawns without a supported holder (world pawns, pawns kept by quests or
  letters) were read as null, and genes were resolved by def alone, which picks the wrong one when a pawn has the same
  def as endogene and xenogene. Unheld world pawns are looked up by id, genes are matched by exact `loadID`, and
  `ChoiceLetter_ChoosePawn`'s option is cancelled if a captured field reads as null; protocol 57. `8bc60df`, `c436e65`
- **Lords are registered in the shared cross refs.** Synced data that references a lord (e.g. ceremony jobs) resolved
  it to null; lords are registered at runtime and from the save, the same way on every client. From #961. `af91582`
- **The `WorldSelected` sync context includes the selected tile.** Only `WorldSelector.selected` was synced, so the
  dev "Generate settlement" gizmo ran on every client's own selected tile. `selectedTile`/`selectedLayer` are synced,
  the context is read inside `HandleCmd`'s `try/finally`, and `SyncUtil.prevSelected` holds the real previous
  selection. `318bac4`

### Sync methods and delegates

- **The sync method transpiler emits valid default values.** For synced methods returning a struct (e.g.
  `*Database.TryDelete` returning `AcceptanceReport`) the early return ran `ret` with an empty stack, and `out`
  parameters of small value types were overwritten with 4 bytes. `1d5e411`
- **Colonist bar reordering is synced with pawns.** It was synced as the `ColonistBar.Entry` closure, whose indices
  were resolved against each client's own cached bar, which differs in multifaction or with hidden maps. The sender
  now resolves the pawns and the synced method applies vanilla's reorder rule to them. `dce32db`, `c436e65`
- **Thing filter edits stay out of the sender-only simulation.** Range setters run `settingsChangedCallback` (storage
  rechecks the map's haulables), and the buffered watch and the slider called them every frame on the sender only. The
  interface now writes only the backing field, edits without a map context are blocked, and "Clear all" leaves hidden
  special filters alone as in vanilla. `85066f3`
- **`TransporterUtility.InitiateLoading` from float menu options is synced.** Carry to shuttle, place in transporter
  and enter shuttle set the transporter group id on the clicking client only. From #961. `035d208`
- **A drafted stop order ends only the goto it was aimed at.** The synced `EndCurrentJob` runs ticks after the click,
  so it could end a job (or another goto) the pawn had started meanwhile; it now checks the job id. From #961.
  `017ed3c`, `aa636c5`
- **`PriorityWork.Clear` is allowed during ticks.** The guard meant to stop the interface from clearing prioritized
  work also dropped vanilla's tick-time calls, so pawns stayed prioritized until the timeout. `393ac90`
- **The product style of bills is synced.** The bill dialog's style menu (Ideology, classic mode) set `Bill.style`,
  `globalStyle` and `graphicIndexOverride` only locally. Reimplements #402 (@SokyranTheDragon) for 1.6. `5005f8f`
- **The health tab's dev "remove hediff" button is synced.** It removed the hediff locally; its `RemoveHediff` call is
  redirected to a debug-only sync method. Fixes #358. `1fa6df1`
- **Debug actions replay under the acting player's view.** Vanilla debug actions (e.g. incident targets) read
  `WorldRendererUtility.WorldSelected`, which is local view state. `ab816c3` (port of #976 by @M-r-A) overrides it
  during the command, and `a3e2414` derives it from the command (world commands under the planet view, map commands
  under their map) and reads all command data before installing overrides. `ab816c3`, `a3e2414`

## 5. Sessions, issuer-only and interface-only code

### Code that runs on a subset of clients inside the simulation

- **`LocalOnlyScope` for issuer-only code in commands.** Commands run with the shared RNG and unique id counters, and
  code that runs only on the issuing client (session windows, `FactionCreator`'s local setup) or uses local UI state
  (`MainTabsRoot.Notify_SelectedObjectDespawned`) advanced them on one client only, the bug class #968 fixed at one
  call site. Those call sites now run in `LocalOnlyScope`: ritual, psychic ritual, growth moment, transporter loading,
  caravan forming, caravan splitting, map portal and trade windows. `5a92b1d`, `83f8853`, `ac8feeb`
- **Motes aren't null only on non-issuing clients.** During commands `MakeStaticMote` returned null on clients that
  didn't issue the command but a mote on the issuer, so callers using the result threw or branched differently;
  non-issuers now get an unspawned mote. `bcf9384`

### Session lifecycle

- **Saving doesn't change live sessions.** `SessionManager.WriteSessionData` removed invalid sessions from the live
  lists, and saving can happen on one client only (e.g. the host's autosave), so session lists and map pausing could
  differ. Invalid sessions are now only left out of the written data; inverted "log once" checks, the unsaveable
  `CaravanFormingSession.onClosed` and an inverted early return in `HandleMissingDialogsGameStart` are fixed too.
  `22f958b`
- **Growth moment sessions survive the child leaving the map (Biotech).** The session's map was `letter.pawn.Map`, so
  once the child left, `Tick()` threw every tick and the session could never be removed, and the window's `PostClose`
  ticked the session from UI code. The session stores its map, a dead or absent child invalidates it (checked by the
  tick, not right after loading, when pawns aren't spawned yet), children held on the map (growth vat, carried,
  shuttle) count as present, options are generated on every client, and one failing session no longer skips the
  others. `de1999a`, `5452502`, `a1e0059`
- **The growth moment option is offered only to the child's faction.** A player of another faction with a pawn on the
  map could choose the child's traits from the colonist bar. #961 makes the same check. `f3b6ca5`
- **Portal and transporter sessions are matched by their target.** A map can have one portal session per portal, but
  the proxy took the first one, and transporter loading reused any session on the map; sessions despawning with their
  portal threw. Sessions are looked up by portal or transporters, store their map, end with their portal, and caravan
  forming sessions created in ticks use a player faction with pawns on the map instead of the spectator. `ee6e9af`,
  `a1e0059`
- **Caravan splitting doesn't depend on the local dialog.** The synced cancel and accept used the session's dialog,
  which exists only on clients that opened it, so cancelling threw elsewhere and accepting split the caravan only
  there. Accept now splits through a proxy built from the session's transferables, and only the caravan's players get
  the window. `e0161aa`

### Trading

- **Trade deals are recached by the simulation.** `MpTradeDeal.Recache()` changed the tradeables but was called only
  from the trading window, i.e. on clients that had it open, so `TryExecute` traded different items elsewhere. The
  session's world tick and the synced transferable lookup now recache with the negotiator's faction forced, and the
  dirty state is saved and ignored while loading (`4649662`). `3f41f92`, `4649662`
- **Faction pushes are balanced in the trading UI and map faction ticks.** The trading window popped only when the
  push returned non-null, leaking a stack entry every frame of an own-faction trade, and `MultiplayerMapComp.DoTick`
  popped without `try/finally`. `b6f1126`

## 6. Multifaction

### Faction contexts

- **Forced pushes apply the faction to every map.** Outside simulation contexts each map holds the local player's
  faction data (areas, zones, designations, listers), and ticks and commands applied their faction only to their own
  map, so code reaching into another map (portals, pocket maps, gravship landing) read client-specific data. Forced
  pushes now apply the faction to the world and every map, and their pops restore exactly what each map held before.
  Part of #621. `c3889fa`, `2da45bb`
- **Leaked faction pushes are unwound at tick and command boundaries.** A push left by an exception kept the wrong
  `Faction.OfPlayer` and faction data after the context; the stack is popped back to the context's depth (reported
  once), and leaked map pushes restore their map. `d412a1c`, `c5d0566`

### Map generation

- **The map generation faction is found from any player caravan.** `MapGenFactionPatch` used
  `PlayerControlledCaravanAt`, which compares with `Faction.OfPlayer`, so only the caravan's owner found it and maps
  were set up for different factions on different clients. The dead `TileFactionContext` fallback is removed. Fixes
  #518 (with `619bc19`). `3b62baa`
- **Map generation uses the faction's world data.** The patch swapped only `Faction.OfPlayer`, so another faction's
  map used the context faction's research, play settings, history and storyteller; it now does a forced `PushFaction`.
  Part of #621. `b33e913`, `0030d36`

### Creating a faction

- **Factions are created with the same data on every client.** `FactionCreator`'s long event ran with each client's
  own faction applied, so the fallback ideoligion, starting research, play settings and copied storyteller were the
  local player's. It runs under a forced spectator push, `Faction.OfPlayer` is restored after `FactionManager.Add`,
  new storytellers get their own difficulty copy, and the event keeps its faction data in its sim context (`d6a13aa`).
  `578c0a6`, `d6a13aa`
- **Faction creation pages don't change shared state.** Opening the settle pages replaced `Current.Game.Scenario` for
  good and registered the temporary pawn-generation faction (and possibly its ideoligion) in the live game on the
  creating client only. The chosen scenario is applied only while the pages' own code runs, and the temporary faction
  and its ideoligion stay unregistered. `8c2731e`, `8a091e5`
- **A new faction's ideoligion is generated for its own faction def**, not `Faction.OfPlayer`'s or the game scenario's
  (wrong for e.g. tribal starts). `513af76`
- **Local starting pawn requests are cleared before faction map generation.** `PrepForMapGen` added custom xenotypes
  from client-local static requests to the shared custom xenotype database. From #961. `e2a589f`
- **Synced starting pawns load like vanilla loads pawns.** The pawn was read in `Playing`, so the needs tracker hit an
  NRE on hediffs without a pawn, and its ideoligion (from the unregistered page faction) didn't resolve, so a random
  fallback was assigned and kept as a previous ideoligion. The pawn is sent without an ideoligion and read in
  `MapInitializing` with the fallback disabled and the per-client `GameInitData` hidden; same wire format. Fixes #536
  and #537. `783d500`, `9992f50`

### Letters

- **The letter stack is the same on every client.** Each client removed other factions' letters from its stack, but
  the simulation reads the stack (expiring letters and their default choices, growth moment sessions, archive
  culling), and letters received in spectator contexts were dropped for everyone. Letters now stay in every stack with
  their faction recorded and saved, only the UI filters them, and other factions' expiring letters open without their
  windows or tab switches. `a95832b`, `ad4bc2f`, `b7a389f`

### Seasons

- **Season notifications run per player faction.** `DateNotifierTick` ran in the synced world tick with the local
  player's faction, so clients saved different `lastSeason` values and the first summer warning letter came on some
  clients only. It now runs once per player faction under a forced push with a saved `lastSeason` per faction, skips
  factions without a home, uses the home map's whole clock and only the faction's own homes, and shows the message
  only to that faction's players. Reimplements #904 (@Sakura-TA) per faction. `43a6493`, `0abd01f`, `ce0136a`,
  `2da2603`, `81ba184`

### World tick under the spectator faction

- **Caravan battle wins and ideoligion ticks run for their player faction.** The world tick runs as the spectator, so
  `CheckWonBattle` compared threats with the spectator and `Ideo.IdeoTick` never added date ritual obligations
  (festivals). The battlefield's player faction and the first player faction following the ideoligion are force-pushed
  around them, as in #961. `f409a3d`, `546b68d`, `bec7f23`

### Other factions' data on shared maps

- **Zone cells under new buildings are cleared for every faction, and other factions' designations on a destroyed
  thing are removed.** Only the installed (context faction's) managers were updated, and the leftovers disappeared
  only on a client that reloaded. From #961. `dceac58`, `6dc1e5b`
- **Other players are kept out of trade requests and ritual goodwill.** `Settlement.Visitable` excluded only
  `Faction.OfPlayer`'s settlements, so trade request quests could target another player's colony, and the
  `NearbyFactionGoodwill` ritual reward could raise goodwill with the nearest player. From #984 (@still222), with a
  narrow transpiler instead of a global goodwill patch. `7399c62`

### Converting to singleplayer

- **The spectator faction is removed cleanly.** Removing it only from the faction list left references in
  reservations, pawns, listers, letters, the play log, quests, ideoligions, tales and map events of the converted
  save; it now goes through vanilla's faction removal. From #961. `c5aac4f`, `a6a4ab7`

## 7. Async time, VTR and the tick loop

### Tick loop

- **The world and maps tick in lockstep without async time.** Each tickable ticked through its own accumulator (the
  world N times, then each map N times), so new maps gained a tick, a rate change during a map tick came after the
  world's ticks, and removing a map mid-tick made another one tick twice; world and map clocks drifted apart by a few
  ticks. Every game tick now ticks the world and then a snapshot of the maps once each; new maps start at the world's
  tick, also when generated later in the world tick (`925534c`), and at the game's speed (`34e19f8`). Async time is
  unchanged. Fixes #339. `291c3c4`, `925534c`, `34e19f8`
- **Ticks and commands see the simulated paused state.** `TickManager.Paused` returned the paused state of the map the
  local player was looking at, also inside ticks and commands, so for example `Pawn_JobTracker.StartJob`'s
  `jobsGivenThisTick` differed between clients. Ticks now always see "not paused", and commands, the constant ticker
  and sim long events see whether their own tickable is paused. `6fcaecb`, `2336353`
- **One subsystem's exception doesn't skip the rest of the tick.** `AsyncTimeComp.Tick` ran everything in one
  `try/finally`, so an exception in `MapPreTick` skipped `mapTicks++` and everything after it, and the world could
  skip `worldTicks++`. Each subsystem is caught separately, as in vanilla's `DoSingleTick`. `38fbe9d`

### Time contexts

- **Map time contexts nest.** A map's `PreContext` kept the previous time in one field, and quest time getters enter
  their map's context while that map ticks the quest, so the outer `PostContext` left the map's clock installed and
  the world clock jumped to the map's. Each context keeps its own frame, UI time restores run in finalizers, and maps
  that are still being generated (no comps yet) are skipped instead of throwing. Fixes #789 (with `f8d9f49`).
  `822b44c`

### VTR (variable tick rate)

- **VTR player counts are saved.** The per-map and world viewer counts affect the simulation but weren't saved, so a
  reload reset them to 0 (on standalone, only on the reloading client); the world count is clamped at 0. `7b666d7`
- **The host's view is counted and old counts are reset.** The host's game starts through a save and reload, which
  doesn't report a view, so the host's map ran at the no-viewer rate (projectiles moving in 15-tick steps) until the
  host switched maps. The host now announces its view, the arbiter no longer counts as a viewer, and counts saved by
  an earlier session are zeroed when hosting or starting a standalone server. `6ad2ef2`
- **The viewed map is followed every frame.** Counts changed only through the `CurrentMap` setter and world map
  toggles, so a multifaction joiner starting on the world map was never counted and its first switch took a viewer
  away from the host's map. The client now compares what it looks at with what it last reported once per frame.
  Supersedes #956 (@MhaWay), whose out-of-band count packets would bring back #862. `65bfd9d`

### Async time

- **An accepted async world quest ticks once per tick.** Generation and acceptance both cached the quest, so its parts
  ticked twice per tick until the next reload, and removal skipped the world list because of an operator precedence
  bug. Supersedes the async quest part of #541 (@Tick-git); #539 itself is already fixed on `dev`. `1095720`
- **Animal hunger alerts use the animal's own time.** `Need_Food.TicksStarving` compares the current time with a
  timestamp from the pawn's clock, but alerts run at the viewed map's time, so animals on other maps showed as
  starving (or didn't) depending on the view. Reimplements #983 (@still222). `123ee05`
- **TimestampFixer handles destroyed pawns and more fields.** It ran a debug save also on pawns killed on a map and
  passed to the world, logging a warning on each death even without async time; it now skips pawns whose clock doesn't
  change and shifts destroyed pawns' fields directly, including all their abilities (`c181b79`). It also shifts the
  starvation start, ability and royal permit cooldowns, the daily skill XP reset and the decree interval. Reimplements
  #979 (@M-r-A) without its save pass; part of #964 and #422. `89dfd7a`, `c181b79`

## 8. Storyteller and quests

- **Story watcher adaptation grows once per game tick.** The world tick ticks the story watcher and every map tick
  ticked it again, so with N maps adaptation (the raid points factor) grew 1+N times as fast as in singleplayer. Map
  ticks now only resolve the colonists downed during them. Part of #659 and #884. `5fb7c98`
- **Quest threats fire in singleplayer.** The storyteller prefix set its flag in singleplayer too, so the postfix
  dropped every `QuestPart_ThreatsGenerator` and quest threats never fired with the mod loaded. `17c7354`
- **Multifaction quest threats fire from one storyteller per map.** Every player faction's storyteller walks the
  shared quest list on each map, and threat counts are seeded by world, target and salt, so all of them fired the same
  quest raids at once; other quest incident makers fired 1+N times. Quest threats now run for one faction per map (the
  owner, else the lowest-id player faction with pawns there), and other incident makers run in the world tick only; a
  map owned by a faction without world data falls back to the pawn-based choice. Part of #659 and #884. `46c06a7`,
  `99d1da2`
- **Multifaction storytellers stay off other players' maps.** Faction A's incidents (e.g. `Map_Misc` while its
  colonists visit) could fire on faction B's home; each storyteller now targets its own maps (including pocket maps)
  and unowned maps where it has pawns. From #961; part of #659 and #884. `0b4fde8`, `524e05e`
- **Quest map picks stay on the generating faction's maps.** Quests generated for a player faction could choose
  another player's map (a gravship landing site counts as a home of every faction), so refugees arrived at a friend's
  colony. Port of #961's owner filter for `QuestNode_GetMap`, `QuestGen_Get.GetMap`, work sites and reliquary
  pilgrims. Part of #659. `46638cc`
- **Nested incidents keep the right map context.** A nested `IncidentWorker.TryExecute` during the world tick reset
  `tickingMap` and popped the outer incident's context, and a nested incident for another map ran in the outer map's
  context (time, RNG, faction). The context is now left and re-entered for another map and kept for the same map. From
  #961. `fb00a6e`, `5373330`
- Quest name seeding is under RNG isolation (`b815de5`), the async world quest double tick under async time
  (`1095720`).

## 9. Odyssey: gravship

- **Takeoff and landing ends run in a synced command.** Vanilla calls `TakeoffEnded`/`LandingEnded` from
  `WorldComponentUpdate`, i.e. per frame in the interface at a different moment on each client, so synced methods
  inside were sent as commands by every client, unique ids were local and `Faction.OfPlayer` was the local faction.
  Each client now sends a command when its cutscene ends, the first one executed runs vanilla's method everywhere, and
  the gravship faction is force-pushed (`8b2268a`). Fixes #640 and #638 (with #893 on `dev`); part of #789 and #687.
  `f8d9f49`, `8b2268a`
- **Cutscene ends survive reloads.** The pending flag lived in `MultiplayerGame`, which is recreated on every load
  (`d900efe`), and a join point during a cutscene lost the gravship, after which the ends threw and the game stayed in
  the cutscene for good (`2bca475`). The flag is saved with the controller, cutscenes without a gravship end cleanly,
  late aborts are ignored, and stale freeze state is cleared (`273b61b`). Part of the #643 fix. `d900efe`, `2bca475`,
  `273b61b`
- **The landing pause is restored after loading.** The `GravshipTravelSession` pausing the landing map isn't saved, so
  after a join point, or for a joiner, the map ran while players chose where to land; it is recreated from the saved
  landing marker. Fixes #643 (with `2bca475`, `273b61b`). `3d8a29a`
- **The landing marker and rotation are the same on every client.** The move designator's sync worker read a field
  that only the landing UI creates, so the command threw on other clients; the confirming client cleared its landing
  marker before its command executed; and other players' designator previews could rotate the marker locally. The lazy
  accessor is used, the marker stays until landing, and the confirming player's rotation is sent with the landing.
  Fixes #791. `3682aea`, `e47be47`, `218c7e5`
- **The landing and departing gravship are placed in their map's context.** `PlaceGravship` and the takeoff's removal
  run in long events queued from cutscene callbacks, outside any tick or command, so they used the interface's RNG and
  the world's clock. Both now run in their map's context (time, RNG, faction), which also replaces the glow grid
  workaround from #635. Fixes #646. `bdb2515` (landing), `2b952b9` (takeoff)
- **Every tickable gets the speed set after landing without async time.** Landing set Normal speed only on the landing
  map, while without async time all tickables share one speed. `74a851a`
- **Camera shakes are timed with the MP timer.** Under async time the extended shake request came from a map's clock
  and was checked against the world's, so the camera shook until the world caught up. UI only. Fixes #696. `44a946a`
- **The grav engine cache is out of the interface's reach.** `GravshipUtility` caches each map's grav engine for the
  current tick, and the interface (at the viewed map's time) could fill it before a command changed the map; in
  multifaction any player's engine made a map a home of every player faction. Ticks, commands and sim long events
  start with an empty cache, and other players' engines are skipped. Different approach than #906 (@Sakura-TA), which
  doesn't build on 1.6.4850. `64e3aef`

## 10. Anomaly, Ideology and Biotech

### Anomaly

- **Unnatural corpse teleports don't depend on the camera.** `UnnaturalCorpse.IsOutsideView` and the container escape
  check read `Find.CurrentMap` and the local camera rect, so corpses teleported or escaped on some clients only. In MP
  the corpse counts as never seen by a camera, keeping the reservation check for every player faction. From #961.
  Fixes #959. `6605f20`, `25558d4`
- **Psychic ritual offerings (Horax) work.** Vanilla moved the offering while enumerating the invoker's inventory, so
  moving a whole stack threw and the ritual never completed; the inventory is snapshotted first, in MP only and up to
  the required count. From #961. Fixes #855 (the rest of it was fixed on `dev` by #739). `c6c9fa6`, `64c25c4`

### Ideology

- **The ritual role group cache is refilled before a synced `TryAssignAnyRole`.** It iterates a static cache that is
  filled only when the ritual dialog is drawn, so clients without the dialog assigned roles differently. Core of #995
  (@jangoergens), without its Roslyn test fixtures. `e05a679`
- **Rituals start with the starting player's role assignments.** Without Prepatcher the role selection widgets aren't
  synced, so each client could hold its own assignments; the start command now carries the widget-changed assignments
  of the starting player (no-op with Prepatcher). Part of #687. `6472678`, `425c18d`
- **Only the clicked ritual gizmo is processed.** Grouped `Command_Ritual` gizmos also processed another gizmo's
  click, and in MP each opens a session, so the dialog opened for the wrong ideoligion. Fixes #506. `0d73a38`
- **A warning when Prepatcher didn't patch the ritual role widget.** Nothing told players that ritual role selection
  isn't synced without Prepatcher; the patched method is checked, and a warning shows in the log, the host window, the
  server browser and the ritual dialogs. Fixes #501. `ab76273`
- **`FixIdeoAfterCopy` handles non-fluid ideoligions and runs in ticks.** `Ideo.CopyTo` leaves `development` null for
  a non-fluid target, so the postfix threw; precept id fixing also applies to copies made by simulation code. From
  #961. `244f8ef`
- Bill product style: see sync methods (`5005f8f`).

### Biotech

- Growth moment sessions: see section 5 (`de1999a`, `5452502`, `a1e0059`, `f3b6ca5`). Gene sync by `loadID`:
  `8bc60df`. Custom xenotypes on faction creation: `e2a589f`.

## 11. Diagnostics

### Desync tracer

- **The deferred stack tracer fails safely.** It runs as a postfix of `Rand.Value`/`Rand.Int`, so any exception in it
  aborted game code after the RNG had advanced, on that client only ("Unknown function header", a trace buffer
  overflow from uncounted inlined frames, the LMF walk NRE from #944). The walk now ends on unknown frames, checks and
  masks LMF pointers, has a step cap and never throws; a failed walk marks the window so its trace hashes aren't
  compared. Covers the tracer overflow part of #988 (@Manaether). `93d6f86`, `9fb2dcd`
- **The trace hash depth doesn't depend on the tick loop call site.** Commands ran either from `TickPatch.Prefix` or
  one frame deeper from `DoUpdate`, depending on frame timing, so identical clients could report "Trace hashes don't
  match". Both paths now have the same depth, and the dev tick-once hotkey is limited to replays. Fixes #994.
  `059510e`
- **Trace collection stops after the stack walk throws.** On some setups (macOS through Rosetta) the walk threw on
  every `Rand` call and the caught exceptions stalled the game; tracing is turned off for the process after the first
  one, and the affected window is marked unreliable first, so this can't cause a false trace desync. Likely fixes #856 (needs confirmation on macOS).
  `0d00fcc`, `6c5bf26`
- `9fb2dcd` also keeps an exception from the main tabs update from skipping the rest of `ExecuteCmd`'s `finally` block
  (RNG and faction restore).

### Desync reports and sync opinions

- **Pending sync opinions are kept across reloads.** Every reload creates a new `SyncCoordinator`, dropping opinions
  still waiting for their counterpart, so a desync right before a join point went unnoticed; they are now carried
  over, and skipped opinions are logged. `3f575f8`
- **Desync traces are sent fragmented.** On standalone the trace reply comes from a remote client and was sent as one
  packet limited to 64 KiB; above that `Send` threw in the reference's handler and the server's forward disconnected
  the sender. `66ee8bc`
- **Desync reports keep the end of the log and the join data diff.** Reports kept the first 10,000 log lines, losing
  the lines around the desync in long sessions; they now keep 2,000 from the start and the newest 8,000, record what
  didn't match when the player connected anyway, and say which part of a replay's desync data couldn't be read. From
  #961. `77fb479`
- **Skipped simulation errors are counted and reported.** The catch blocks that keep one subsystem's exception from
  skipping a tick only logged; the count and the first error are now added to desync reports and the player is told
  once, and the count resets when world data arrives after a rejoin (`60b457b`). From #961. `a21fb66`, `60b457b`

### Surfacing failures

- **Loading errors are confirmed before hosting or joining.** Failed patches and sync registrations only showed a
  marker on the main menu button; they are now collected through `Multiplayer.LoadingError` and listed in a
  confirmation dialog before opening the server browser, hosting or accepting a Steam invite. From #961. `d4349f2`
- **Disconnects caused by a server state exception are explained.** The client only showed "Disconnected", with no
  hint that the error is in the host's log. Likely fixes #797 together with the bigger init/join data limits of `7c88a31` (no host log in the report). `db58b65`

## 12. Usability and robustness

- **The game can be saved after a disconnect.** The disconnected window only allowed quitting, losing everything since
  the host's last save. The last join point snapshot (which already gets every received command appended) is kept
  while disconnecting, and a Save button writes it as a save that simulates to the tick this client reached, without
  changing game state. Fixes #333 and #937. `201829a`
- **Selecting drafted pawns doesn't lag.** The interface melee verb patch built `VerbEntry` lists (O(n²) in the number
  of verbs) several times per frame for every selected drafted pawn; it now reuses the current melee verb when usable,
  otherwise picks in O(n) as vanilla would, and caches the answer per frame, still without `Rand`. Fixes #308.
  `f59d6b7`, `03d89bd`
- **The launch destination targeter stops when the launchable is gone.** While one player picked a destination,
  another could launch or cancel the group, after which the targeter threw every frame and couldn't be closed. UI
  only. Fixes #365. `b9ad395`
- **Caravan meeting and demand dialogs pause the game.** MP disables `WindowsForcePause` because windows differ
  between clients, so time kept running while these dialogs waited for an answer; the incidents now pause from the
  simulation like a pausing letter, unless the host set pause on letter to never. Fixes #512. `016815f`
- **The connecting window reads the download speed once.** The speed was recomputed on every read, so the value
  checked against 0 wasn't the one divided by (`DivideByZeroException`). From #961. `c3c3323`
- **Null guards in three patches**: the world time speed query, the forbiddables despawn prefix and the teaching work
  giver prefix threw on null maps or jobs. From #974 (@Spagles). `1c20d17`

## Compatibility and behavior changes

### Protocol, replays and saved data

- `MpVersion.Protocol` goes from 56 to 57. Within 57 the branch changes `Server_WorldData` (one command list),
  `ServerJoinDataPacket` (missing defs, game language), `ClientInitDataPacket` (language), the `CreateJoinPoint`
  payload (uploader), join point uploads and save requests (tick), adds `Server_SyncReference`, sends large commands
  fragmented and changes the sync data of unheld pawns, genes and the `WorldSelected` context. Other open PRs also
  bump the protocol with different formats (#956 to 57, #961 to 58), so the number needs coordinating.
- Replays recorded before this branch are not compatible (sync handler ids, command formats and tick order changed).
  The old per-map command layout of replays is still read.
- Standalone servers keep their command log in `Saved/cmds.dat` with its protocol in `Saved/cmds_protocol.txt`. A log
  written by an older server is discarded on start with an error; the saved world, maps and session still load, and
  only the progress since that save is lost.
- The join compatibility check now also compares Multiplayer's own assemblies, def types missing on the client and the
  game language, and ignores line endings in synced configs and XML files.

### Server and join behavior

- On standalone servers every client reloads at a join point (as in hosted mode). Only the sync reference uploads;
  join points are aborted after 60 s without progress; forced join points are limited to one per 10 s; minute-based
  autosaves are issued by the server; join point requests from world travel are ignored.
- Standalone servers now compare sync opinions (from the sync reference) and report desyncs.
- Clients can't send server-only commands; only the host can move other players between factions.
- Handshake timeouts: 2 minutes for automatic replies, 5 minutes for the init data upload, 15 minutes for replies that
  wait on user input. Players in game who don't answer keep-alives for 10 minutes are dropped.
- Commands larger than a single packet are sent fragmented, up to 1 MiB; selections over 200 things are split.
- A joiner whose world data would exceed 32 MiB is disconnected with a message giving the sizes.
- Steam: P2P sessions are closed on disconnect; a rejected player's join requests are ignored for 30 s.

### Gameplay-visible behavior (deterministic on all clients)

- Storyteller adaptation grows once per game tick (it grew 1+N times with N maps).
- Multifaction storytellers target only their own maps and unowned maps where they have pawns; quest threats fire once
  per map; other quest incident makers fire once.
- Quest threats fire again in singleplayer with the mod loaded.
- Without async time the world and maps tick in lockstep (no 1–3 tick drift). At speeds above 1x the order is W M M W
  M M instead of W W M M M M.
- Code in ticks sees `TickManager.Paused` as false; commands see their own tickable's paused state.
- The arbiter doesn't count as a VTR viewer; the host's map is counted.
- Caravan meeting and demand dialogs pause the game (unless pause on letter is set to never).
- Rituals start with the starting player's role assignments (matters without Prepatcher).
- Zone and plan cells are shuffled again in singleplayer; MP keeps not shuffling them.
- Multifaction: every client keeps the full letter stack and the UI filters it; season messages are shown only to the
  faction's players and are not archived.

### UI and translations

- New UI: a confirmation dialog before hosting or joining with loading errors, a Prepatcher-missing warning (host
  window, server browser, ritual dialogs), a Save button in the disconnected window, a language row in the join data
  window, an explanation for server state exception disconnects, and messages when an action is too big to send or a
  simulation error was skipped.
- New translation keys, with English fallbacks in code, to add to Multiplayer-Locale: `MpMismatchLanguage`,
  `MpPrepatcherMissing`, `MpPrepatcherMissingDesc`, `MpSaveGameAfterDisconnect`, `MpServerStateException`,
  `MpServerStateExceptionDesc`.
- Some new texts are English only, without keys: the loading errors confirmation, "this action is too big to send to
  the server", the skipped simulation error notice and the "world data too big" disconnect reason.

### Code-level changes

- Added: `MpFinalizer`, `LocalOnlyScope`, `SimLongEventContext`, `Multiplayer.LoadingError`,
  `MultiplayerServer.CheckServerThread()`.
- Removed: the `consistentCommandOrder` tweak value, the `PlaceGravship` sync method registration (it never synced
  inside the long event), `PushFactionOutsideContext`/`PopToLocalFaction`, the `TileFactionContext` fallback. Clients
  no longer send standalone snapshot packets.

## Known limitations / not addressed

### Limitations of the fixes

- Without Prepatcher the ritual role widgets themselves are still not synced; only the assignments at ritual start are
  (#687 is only partly fixed).
- In multifaction only the first player faction that follows an ideoligion gets its festival and role letters
  (`bec7f23`).
- Map ids in client commands are checked by scope only, not against existing maps, because the server doesn't know
  maps created since the last join point.
- Gravship takeoff dialogs, the destination picker and landing cutscenes can't be restored after a reload; their
  sessions stay unsaved.
- On setups where the stack walk throws (e.g. macOS through Rosetta) desync traces are off for the rest of the
  process, so reports from that client carry no traces.
- Custom xenotypes of a new multifaction faction's starting pawns aren't added to the custom xenotype database
  (`e2a589f`).
- A different game language is only reported (#264); translated text still differs between such clients.
- #618 is addressed only for the fire/sound path; #621, #659, #884, #964 and #422 are partly addressed.

### Parts of open PRs deliberately not taken

- #958: the same-IP replacement for direct connections (a player behind the same NAT could kick another).
- #961: its async clock model and VTR view table (they conflict with the sim long event context and the VTR fix here),
  and its `DefeatAllEnemiesQuestComp`/`TimedForcedExit` parts (nothing starts that quest and no def uses the obsolete
  comp in 1.6).
- #956: out-of-band player count packets (would bring back #862).
- #995: the Roslyn test fixtures.

### Open issues not addressed

- #546, #581: quests in multifaction need the quest owner's faction context (a large change).
- #669 (scenarios in multifaction), #639 (one global gravship controller in multifaction), #799 (permanent pawn
  opinions), #898 (pens with same-tile settlements), #627 (ShuttleCrash_Rescue bodyguards leaving).
- #992 (Combat Extended bullet collision desync).
- #989, #990, #457 (MonoMod allocator in Prepatcher's Harmony).
- #754 (ARM float differences).

## Testing

- The solution builds and 284 unit tests pass. New test files cover the command log, join points, the joining state,
  fragmented packets, large commands, reader bounds, reconnects, server stop, the sync reference, standalone
  persistence, world data size, selection packets, line ending normalization and log trimming.
- Each stage was reviewed separately and the findings were fixed before it was merged into the branch.
- In-game tests used several real RimWorld 1.6.4850 instances with all DLCs on Linux (headless, Xvfb, software GL). A
  harness mod drives the game through normal sync paths (draft/undraft, mining, stockpiles, work priorities, forbid,
  plant cutting, raids, fires, incidents) and logs the sync state every 5 s.
- Scenarios: hosted with an arbiter and a joiner joining mid-game; hosted without an arbiter; multifaction with the
  joiner creating its own faction and map; async time; standalone server (two players, server-driven autosave join
  point, restart from the persisted state, start from an older save).
- Results on `dev`: a desync in 1 of 4 valid runs ("Random state from commands doesn't match" during the joiner's
  catch-up), and the arbiter game froze for everyone as soon as someone joined.
- Results on the branch: no desyncs in about 30 sessions, the last ones a full run of every scenario on the complete branch, identical VTR viewer counts on all instances, and identical
  world and map ticks without async time.
- Not covered in game, only by code review: Steam, Windows and macOS clients, other mods, gravship flights, rituals,
  trading and caravans, large mod lists.
