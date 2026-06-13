# Idea 2 — Sidecar Metadata Files

**Status:** Phase 3 (largest, most invasive). Reference:
[#1560 comment](https://github.com/dotnet-websharper/core/issues/1560#issuecomment-3910205019).

## Today

WebSharper output goes **inside** the produced `.dll` as Mono.Cecil `EmbeddedResource`s, added
in [../core/src/compiler/WebSharper.Compiler/FrontEnd.fs](../core/src/compiler/WebSharper.Compiler/FrontEnd.fs):

- `WebSharper.meta` (`EMBEDDED_METADATA`) — compile-time metadata used by downstream WS
  projects building on this one.
- `WebSharper.runtime.meta` (`EMBEDDED_RUNTIME_METADATA`) — only for sitelet projects; used by
  the sitelet runtime for remoting/dependencies/page init.
- JS/TS resources and other embedded files.

Reading back is `TryReadFromAssembly` / `TryReadRuntimeFromAssembly` (same file), which scan
`a.Raw.MainModule.Resources`.

## Proposal

Write the WS payload to **sidecar files** next to the dll instead of embedding it:

- `MyProject.dll.websharper` — the compile-time metadata + JS/TS resources.
- `MyProject.dll.websharperruntime` — the runtime metadata (sitelet projects only).

### Why

- Smaller dlls; the .NET loader doesn't carry WS resources it never uses at runtime for plain
  libraries.
- Metadata can be read/written without opening the managed assembly with Cecil — cheaper for
  tooling, and a stepping stone toward caching/partial-reuse of metadata across builds.
- Decouples "produce the dll" from "produce WS metadata", which is friendly to Idea 1's
  parallelism and Idea 4's dll-free client iteration.

### The hard parts (this is why it's big)

1. **Both files must travel with the dll.** Two distribution paths:
   - **MSBuild copy to `bin`.** The sidecars must be copied to the output dir of the building
     project *and* to consumers that reference it via `ProjectReference`/package. This means
     emitting MSBuild items with `CopyToOutputDirectory=PreserveNewest` and ensuring they flow
     across `ProjectReference` (consumers copy referenced projects' sidecars), via the WS
     MSBuild targets in `WebSharper.MSBuild.FSharp`/`WebSharper.MSBuild.CSharp`.
   - **NuGet pack.** `dotnet pack` must place the sidecar beside the dll in `lib/<tfm>/`. WS
     packages are produced via the build script; packing logic must add the sidecars as
     package content next to each assembly.
2. **Back-compat reading.** Existing published packages have embedded resources. The reader
   must: prefer a sidecar if present, else fall back to the embedded resource. So
   `TryReadFromAssembly` gains a sidecar-first path keyed on the assembly's file path. This
   also means readers that only had an `Assembly`/byte stream (no path) still need the embedded
   fallback — audit all call sites for whether a path is available.
3. **Versioning / staleness.** A stale `.websharper` next to a newer dll must not be silently
   used. Embed a hash/timestamp/metadata-version tag and validate against the dll
   (the metadata flag in [../core/src/compiler/WebSharper.Core/Metadata.fs](../core/src/compiler/WebSharper.Core/Metadata.fs)
   already versions the format).
4. **All the consumers of embedded resources.** Beyond the compiler: the DllBrowser tool, the
   metadata explorer, offline sitelet writer, and any unpack/download-resources commands read
   embedded resources. Each needs the sidecar-first/embedded-fallback treatment.

## Design

- Config flag `WebSharperMetadataFormat = Embedded | Sidecar | Both` (MSBuild + wsconfig).
  - `Embedded`: today's behavior (default initially).
  - `Sidecar`: write sidecars, do not embed.
  - `Both`: transitional — write sidecars and embed, so old and new readers both work.
- Writer: in `FrontEnd.fs`, branch the resource-writing so the same serialized bytes either go
  to `MainModule.Resources` (embedded) or to `File.WriteAllBytes(asmPath + ".websharper", ...)`.
  Keep the serialization identical so format is reader-agnostic.
- Reader: `TryReadFromAssembly`/`TryReadRuntimeFromAssembly` try `<dllpath>.websharper(runtime)`
  first (when a path is known and valid), else current embedded scan.
- MSBuild targets: add the sidecars as `None`/`Content` with `CopyToOutputDirectory`, and a
  target that, after build, ensures referenced-project sidecars are copied (model it on how
  the dll itself is copied). Verify with the benchmark scenario's `App.Web` that the sidecars
  for all transitive libraries land in `bin`.
- Pack: extend the WS packaging step to include sidecars in `lib/<tfm>/`.

## Validation

- Build the benchmark solution with `Sidecar`, confirm: dlls shrink, all `.websharper(runtime)`
  files present in every `bin`, app runs identically (same JS, same remoting).
- Pack a library to NuGet with `Sidecar`, consume it from a fresh project, confirm the sidecar
  restores into the package folder and is found by the reader.
- Consume a **`Both`-packed** library from an **old** WS compiler (embedded reader) and a
  library packed `Embedded` from a **new** compiler (sidecar-first, embedded fallback) — both
  must work.
- Measure pack time, dll size, and cold/warm compile in the harness; the perf case for this
  idea is dll size + load + the future caching it enables, not raw compile time, so set
  expectations accordingly.

## Possible follow-on (not in this phase)

Once metadata is a standalone file with a version/hash, the booster could cache and partially
reuse a project's own previous metadata sidecar across edits, which is a bigger warm-recompile
win than the file split itself. Note it; don't build it here.

## Risks

- Distribution gaps are silent and nasty: a missing sidecar at runtime = missing resources.
  The `Both` transition mode and an explicit "sidecar expected but not found, and no embedded
  fallback" diagnostic are the safety net.
- Many readers to update; easy to miss one. Enumerate every `EmbeddedResource` reader before
  starting (grep `EMBEDDED_METADATA`/`EMBEDDED_RUNTIME_METADATA` and `MainModule.Resources`).
