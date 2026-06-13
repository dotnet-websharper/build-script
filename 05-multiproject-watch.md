# Idea 4 — Multi-project tracking in `dotnet ws watch`

**Status:** Phase 2. Largely isolated to the `dotnet-ws` tool.

## Today

`dotnet ws watch` ([../dotnet-ws/Program.fs](../dotnet-ws/Program.fs)):

- `checkCurrentFolderWithPattern` enumerates `*.fsproj` under the current dir.
- For each project, `getFilesToWatch` parses the `.fsproj` for `<Compile>/<None>/<Content>`
  includes and resolves them to absolute paths.
- A single recursive `FileSystemWatcher` on the current dir fires `WatchHandler.Handler`.
- On a change, it finds which project owns the changed file and runs a **full**
  `dotnet ws build` for *that one project* (via the `build` path), serialized through a
  `MailboxProcessor` that cancels/kills any in-flight build.

Limitations for the target use case:

- **No dependency awareness.** Editing a leaf library does not propagate to the SPA/Web project
  that consumes it; you'd have to rebuild those yourself.
- **Everything is a dll rebuild.** Even a pure client-side change triggers full project builds,
  which is exactly the cost Idea 4 wants to avoid.

## Target scenario

A client-side-only edit (a `[<JavaScript>]` function body, a UI template) should:

1. Re-run **WebSharper translation** for the edited project,
2. **Propagate** the translation through dependent projects up to the final app,
3. **Write out the JS**, and — for bundled apps (WS8+) — run **esbuild**,

all **without recompiling any `.dll`**, because no .NET-visible signature or server code
changed.

When a change *does* affect server code, public signatures, references, or `.fsproj`
structure, fall back to the existing full `dotnet ws build` propagation.

## Design

### 1. Build a project dependency graph

- Discover the watched projects (already done) and read each one's `ProjectReference`s to build
  a DAG. Reuse the `.fsproj` parsing approach already in `getFilesToWatch`; add
  `<ProjectReference Include=.../>` extraction.
- Topologically order projects so propagation visits a project only after its dependencies.

### 2. Classify the change

On a file event, decide the cheapest sufficient action:

- **`.fsproj` changed** → reload graph + full build of that project and dependents (today's
  behavior, extended to dependents).
- **A source file changed**:
  - **client-only-safe** → JS-only propagation path (below).
  - **otherwise** → full `dotnet ws build` of the project, then propagate to dependents.

Classifying "client-only-safe" precisely is the crux. Conservative, staged options:

- v1 (safe, simple): treat a change as client-only **only** if the user opts in (e.g.
  `dotnet ws watch --client-only`) or the project is marked client-only (SPA/`BundleOnly`/
  proxy). Anything ambiguous → full build. Ship this first; it already delivers the win for
  the common "I'm iterating on UI" loop.
- v2 (smarter): compare the new compile's produced **metadata/signature surface** to the
  previous one; if only client bodies changed (no change to the public .NET surface or RPC
  shapes), keep the existing dll and only refresh JS. This leans on the metadata work and the
  RPC-verification config from Idea 1 (`RpcVerification = Off/Deferred` during watch).

### 3. JS-only propagation path

This is the new capability. For the edited project and, in topo order, its dependents up to the
final app:

- Run WS translation using the **existing** referenced dlls/metadata (unchanged) plus the
  edited project's new translation — i.e. invoke the compiler's translate+package stages
  without the F# `checker.Compile`/dll-write. The compiler already separates these phases
  (`Compile.fs`: translation, then `ModifyAssembly`/`Bundle`); we need an entry point that
  stops after producing JS and skips the dll write.
- Write JS to the consuming project's output (the `JSOutputPath`/unpack location already used).
- For the final bundled app, run the bundling step and then **esbuild** (the WS8+ bundling
  already shells esbuild during normal builds; reuse that invocation).
- Keep the booster warm and reuse cached reference metadata (`MemoryCache`/`argsDict`), so this
  path is fast.

### 4. Propagation control

- Reuse the `MailboxProcessor` serialization, but make cancellation propagate to the whole
  chain (currently it kills a single project build).
- Debounce bursts of file events (editors save in multiple events); the current `Async.Sleep 100`
  is a start — coalesce events per project within a short window.

## Interactions

- **Idea 1** gives `RpcVerification = Deferred/Off`, which the watch's client-only path wants
  (no need to re-verify RPC when shapes are unchanged).
- **Idea 2/3** help: structured timing shows whether the JS-only path is actually dll-free and
  where its time goes; sidecar metadata makes "is the dll still valid?" cheap to check.

## Validation

- In the benchmark solution, `dotnet ws watch` running, edit a `Client.Components` function
  body: assert (a) no `.dll` mtimes change, (b) the app's JS/bundle updates, (c) the change is
  visible in the browser. Compare wall-clock to today's full-build watch.
- Edit a server RPC signature: assert it falls back to a full build and stays correct.
- Time both paths via the harness's warm phases (`warm-client-edit` should drop substantially
  with the JS-only path).

## Risks

- Misclassifying a server/signature change as client-only would produce a stale dll with fresh
  JS — correctness hazard. The conservative v1 (opt-in / client-only project kinds) avoids this;
  v2 must be gated on a sound surface-diff.
- Needs a compiler entry point that does translate+package without dll emit; if that's awkward
  to expose cleanly, v1 can still rebuild dlls but skip them when up-to-date, capturing part of
  the win.
