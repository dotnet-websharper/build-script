# Idea 1 — Parallel F# Compilation (+ #1587, + optional RPC-await)

**Status:** first implementation target (after Phase 0 harness exists).

## The current serialization, and why it exists

In [../core/src/compiler/WebSharper.Compiler.FSharp/Compile.fs](../core/src/compiler/WebSharper.Compiler.FSharp/Compile.fs)
the F# path does, in order:

1. `checker.Compile(... args ...)` — runs the **real F# compiler** (FCS) and writes the
   output `.dll` to `config.AssemblyFile`. Synchronous.
2. In parallel with step 3's setup, reference WS metadata is loaded
   (`wsRefsMeta = Task.Run(...)`).
3. `createAssemblyResolver config true` — note `includeCurrent = true`: it adds the
   **just-written output dll** to the resolver's search paths.
4. `compiler.Compile(...)` — WS translation (project read → name resolve → translate →
   optimize).
5. `loader.LoadFile config.AssemblyFile` + `ModifyAssembly` — re-open the output dll and embed
   WS metadata/JS resources.
6. `assem.Write(...)` — write the final dll.

The reason WS translation can't simply start before FCS finishes is twofold:

- **Reflection on own output (the #1587 issue).** Macros and generators are allowed to
  `Type.GetType`/`Activator.CreateInstance` types **from the current project's own freshly
  built dll** (that's what `includeCurrent = true` enables). If any macro/generator does this,
  the output dll must exist before translation runs. This is the hard dependency that blocks
  overlap today, even though most projects never use it.
- **RPC verification needs the compiled signatures.** The verifier checks remote method
  shapes; today this is reachable from the compiled assembly. This forces "dll exists" before
  the verification step.

So the win is: **if a project does not reflect on its own output, FCS compilation and the WS
translation front-end can overlap**, and the only mandatory join points become (a) writing
resources into the dll and (b) optional RPC verification.

## Sub-task A — #1587: make own-output reflection opt-in

Reference: <https://github.com/dotnet-websharper/core/issues/1587>

- Add a config flag, threaded through `WsConfig`, e.g. `ReflectOwnOutput : bool`, surfaced as:
  - MSBuild property `WebSharperReflectOwnOutput` (both
    [../core/src/compiler/WebSharper.MSBuild.FSharp/WebSharperTask.cs](../core/src/compiler/WebSharper.MSBuild.FSharp/WebSharperTask.cs)
    and the C# task for symmetry), and
  - `wsconfig.json` key `reflectOwnOutput`.
- In `createAssemblyResolver`, only add `config.AssemblyFile` to search paths when the flag is
  set. (`includeCurrent` currently always passed `true` for the main compile — change to pass
  `config.ReflectOwnOutput`.)
- **Default / migration:** ship the flag defaulting to **current behavior (on)** first, so no
  one breaks. Audit the standard stack (StdLib proxies, UI, templates, the in-repo macros/
  generators) for any actual own-output reflection; once confirmed clean, flip the default to
  **off** in a minor release with a release note, since off is what unlocks the perf win and
  is the correct default (a project rarely needs to reflect on the very dll it is producing).
- Emit a clear diagnostic if a macro/generator attempts to load a type from the current
  output assembly while the flag is off (turn today's silent dependency into an explicit,
  actionable error pointing at the flag).

## Sub-task B — RPC verification: optional & configurable await

- Identify the verification step that consumes the output dll (Verifier path reached from the
  compile pipeline / sitelet metadata). Make its dependency on the freshly written dll
  **explicit** as an awaited task rather than an implicit ordering.
- Config `RpcVerification : On | Deferred | Off` (MSBuild `WebSharperRpcVerification`,
  wsconfig `rpcVerification`):
  - `On` (default): verify before finishing the build (today's guarantee).
  - `Deferred`: run verification as a task that is awaited only at the very end, so it overlaps
    translation/packaging instead of gating it.
  - `Off`: skip (fast inner-loop / client-only iteration; documented as reduced safety).
- This pairs naturally with Idea 4's client-only watch, where RPC shapes haven't changed.

## Sub-task C — overlap FCS compile with WS front-end

With A (reflection off) and B (verification awaited), restructure `Compile`:

- Start `checker.Compile(...)` as a task `fscTask` instead of `Async.RunSynchronously`.
- Concurrently run the parts of the WS front-end that **don't** need the output dll:
  reference-metadata union (already parallel), assembly resolver setup, and the project read /
  name resolution that operate off FCS check results.
  - Important nuance: WS translation reads FCS's **typed AST** (`keepAssemblyContents = true`),
    which is produced by the *check*, not the *emit*. Investigate splitting
    `checker.Compile` (check + emit) so WS translation can begin from the check results while
    the emit (writing the dll) proceeds in parallel. If FCS doesn't expose a clean
    check-then-emit split for the project-args entry point used here, fall back to: keep one
    `Compile`, but overlap the **next** mandatory dll-dependent stages behind tasks.
- Join points:
  - Before `ModifyAssembly` / `loader.LoadFile config.AssemblyFile` — must await `fscTask`
    (the dll must exist to embed resources).
  - Before finishing — await RPC verification task if `Deferred`.
- Preserve `CompileOnWorker`'s large-stack worker-thread behavior; the parallelism is in
  addition to it, not a replacement.

### Booster interaction

The win matters most in the **booster** (`wsfscservice`) where `FSharpChecker` is warm. The
restructure must hold the booster's invariants: cached ref metadata (`MemoryCache`), project
arg/timestamp cache (`argsDict`, `ProjectNotCached`). Parallelism is per-compilation-request;
ensure no shared mutable state in the new task graph leaks across concurrent requests.

## Correctness gates (must all pass before default flip)

- Emitted JS and embedded metadata are **byte-identical** to baseline for the whole snippet
  suite and the benchmark scenarios, with `ReflectOwnOutput = off`.
- Full `CI-Release` (compiler tests + QUnit) green.
- A dedicated regression project that *does* reflect on its own output: with the flag on it
  builds; with the flag off it produces the new explicit diagnostic (not a crash).

## Expected measurement (what Phase 0 should show)

- `cold-full` and `cold-booster` on the multi-project scenario should drop, proportional to the
  overlap between FCS check/emit and WS front-end. The harness's per-stage timing
  (`Parsing with FCS` vs `WebSharper translation` vs `Writing resources into assembly`) tells
  us the realistic ceiling **before** we build it — if FCS dominates and WS front-end is tiny,
  the overlap win is small and we re-prioritize. Measure first.

## Risks

- FCS may not cleanly separate check/emit for this entry point → smaller win than hoped.
- Subtle ordering bugs from concurrency (shared resolver/loader state). Mitigated by the
  byte-identical-output gate.
- Flipping the #1587 default could surprise downstream extension authors who relied on
  own-output reflection. Mitigated by audit + explicit diagnostic + release note + keeping the
  opt-in.
