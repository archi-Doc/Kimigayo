# LN design: one owner of a source's logical name (R5 U9a, with G289 (3))

Base `abc7e28c7` (dev). Design only; implementation waits for the planning session's GO. Major (a new representation). Probe: the
copies' expressions copied verbatim from the base and run on this host (.NET, Windows), plus a case-folding probe (scratchpad
`lnprobe/probe.cs`, `fold.cs`); rows marked (Linux) are read from the code, not run.

| Revision | Change |
| --- | --- |
| 0 (ae8737e12) | Copies, probe, owner Kotonoha, stored `SourceDocument.LogicalName`; split U9a-1..3. |
| 1 (c65cc1340) | Review REVISE: every document creator listed with its name (B1); the name is Kotonoha's query over the current base, not a value stored at parse (B2); HoverBuilder's cache goes (N1); SPEC 6.2.1's order an open issue (N2); G346's check half ends in U9a-1, no absolute name for a NUL inside the project (N3); one base for hover placement (N4); pinned Unicode data (N5); split re-sized, U9b with U9a-1 (N6); the merge dropped (N7); contract records (N8). |
| 2 | Review 2 REVISE (B3; planning ruling: B2 and B3 share a cause): the name is a pure function of the document's path and its module's stored base, each with explicit change points; the current directory is captured once when the root module is created; the query is memoized, a warm read allocates nothing; measurement plan; N9-N12; U9a-1 split into 1a (the base) and 1b (the query) so each unit's tests fail first. |

## Required meaning

- IMPL 20.1: a source's **logical name** is its project-relative path with `/` separators; names must be NFC, and two names equal under
  simple case folding are an error. Product sources lie directly in the project directory (name = file name); TestSources are
  project-relative paths inside it. An implicit project's directory is its file's (IMPL 20.3.1), so its source's name is the file name.
- Its project is the source's own: a dependency's sources are named relative to the dependency's project (SPEC 18.5: a source package
  holds its sources with their logical names; `DependencySource.LogicalPath` is already the file name).
- Readers: declaration order across documents, UTF-8 byte order of the names (SPEC 6.2.1); runtime locations, displayed with control
  and non-ASCII characters and `\` escaped (IMPL 21.4.2, SPEC 22.5.1); test identities, "independent of absolute paths" (IMPL 20.6.2);
  documentation order, hover footers and links (`DocumentationSource.LogicalName`, `Binding.Documentation`, HoverRenderer, HoverPlacement).
- Not defined by IMPL: an in-memory source (`Project.AddSource(url, text)`) outside its base, on another drive or with NUL, and a project
  without a directory (an API construct; R5-M20 took the current directory). Generated Mod documents need none (SPEC 6.2.1 orders them
  after the others); the bundled library's `compiler://` documents are implementation-defined.

## The copies today (probe; root `C:\p\App`, dependency `C:\p\Lib`)

| Input path | `Kotonoha.ParseSource` :276 | `SourceLocationTable.Text` :50 | `DocumentationSource.SetLocationCore` :197 | `.PhysicalLogicalName` :201 (hover) | `TestCatalog.LogicalPath` :232 |
| --- | --- | --- | --- | --- | --- |
| `C:\p\App\tests\T.kimi` | `tests\T.kimi` | `tests/T.kimi` | `tests/T.kimi` | `tests/T.kimi` | `tests/T.kimi` |
| dependency `C:\p\Lib\Lib.kimi` | `..\Lib\Lib.kimi` | `../Lib/Lib.kimi` | `Lib.kimi` | `Lib.kimi` | `../Lib/Lib.kimi` (catalog inputs) |
| other drive `D:\x\Main.kimi` | `D:\x\Main.kimi` | `D:/x/Main.kimi` | `D:/x/Main.kimi` | null | `D:/x/Main.kimi` |
| no directory, `<cwd>\A.kimi` | absolute | `A.kimi` | absolute, `/` | null | `A.kimi` |
| no directory, `C:\elsewhere\A.kimi` | absolute | `../../../../../../elsewhere/A.kimi` | `C:/elsewhere/A.kimi` | null | as SLT |
| relative `Main.kimi` with a directory | `..\..\<cwd…>\Main.kimi` | `Main.kimi` | `Main.kimi` | `Main.kimi` | `../../<cwd…>/Main.kimi` |
| `compiler://Kimi/0.1/Console.kimi` | `..\..\<cwd…>\compiler:\Kimi\…` | the path | the path | null | (not a source module) |
| `C:\p\App\a<NUL>b.kimi` | ArgumentException | ArgumentException | ArgumentException | ArgumentException | ArgumentException |
| (Linux) file name `a\b.kimi` | `a\b.kimi` | `a\\b.kimi` | `a/b.kimi` | `a/b.kimi` | `a/b.kimi` |

Differences: the base (root vs the source's own project: dependency rows); separators (`ParseSource` keeps `\`; Doc and TestCatalog
turn a non-Windows `\` into a separator); the no-directory base (Doc keeps the absolute path, hover gives none; SLT's memo is keyed by
the project directory, not the current directory it actually reads); relative and `compiler://` paths resolved against the process's
current directory (`ParseSource`, TestCatalog). `ParseSource`'s result is the diagnostic collection name and the path of text parsed
through that collection (`CodeContext.Parse`). A sixth reader caches: `HoverBuilder.logicalNames` from `Kotonoha.HoverSourceName`. No copy
checks NFC or case folding. G289 (3): TestCatalog lines 232 (162 characters) and 273 (235).

## Every sequence that creates a document, and its name

| Kind | Creator (parse sequence) | Kotonoha's base | Name by the rule | Today's readers keep |
| --- | --- | --- | --- | --- |
| Root module source (disk, TestSources, in-memory, deserialized) | `Kotonoha.ParseSource` (`AddSource`, `OnDeserialized`) | the project directory, else the current directory captured when the root module is created | relative to the base | — |
| Dependency module source | `Kotonoha.ParseSource` | its project's directory (`Url`'s) | relative to its own base | — |
| Text parsed into a module (tests, edits; path = the collection name) | `CodeContext.Parse(…, text)` | the module's | the path as given (relative) | — |
| Mod-generated document | `CodeContext.Parse(…, document, modId, order)` | the module's | by the rule (its path) | documentation and hover: none, by `ModId` and `generatedSourceLocations` (their rules today) |
| Library source, `$abort` | `KimiLibrarySources.ParseSource`, `KimiLibrary.CreateAbort` | none (`compiler://` Url) | the path (`compiler://Kimi/…`), as documentation has it today | hover: none for a built-in source (`SourceIdentity.IsBuiltIn`) |

(`Parser`'s nested interpolation reader reuses its document's context and creates none.)

## Owner, representation, lifetime (revision 2: B2 and B3 share one cause)

Cause one level higher: revision 1's name read ambient state, the process's current directory, at query time, so its lifetime had no
change points (B2) and a warm read allocated (B3). Rule: **the logical name is a pure function of two published facts, the document's
path and its module's base, and each fact has explicit change points.**

- **The base: a value Kotonoha owns and publishes** (`Kotonoha.Base`, replacing `SourceDirectory`), stored and recomputed only at a change point:
  - root module: `Project.Directory`, normalized once per value (`GetFullPath`); a project without a directory takes the current
    directory **captured once when the compilation creates its root module** (`Compilation`'s constructor; a parse may precede `Prepare`,
    `CreateForTest` then `Parse`). Change point: the `Project.Directory` setter, which Kotonoha detects as SLT detects it today (R5-M20):
    an ordinal reference comparison of the project's directory with the one its base came from, no allocation; the base is recomputed
    only then.
  - dependency module: its project's directory, set when dependency resolution creates the module (`PrepareModules`, a new `Kotonoha` per
    resolution); no other change point.
  - library, `$abort` and test modules (`Url` not a full path): none.
  - A directory that is not a fully qualified, NUL-free path (an API setter value with NUL) is not normalized and is used as given: no
    `Path` call throws; such a base matches only paths with its exact text (N11). `Project.TryCreate` never produces one.
- **The name: `Kotonoha.LogicalName(SourceDocument)`**, the rule over the document's path and the stored base, memoized per document
  in the module (`Dictionary<SourceDocument, string>` by reference). The memo is emptied when the base is recomputed and when the source
  analysis is revoked (`InvalidateSourceAnalysis`, where SLT's is), so it holds only current documents (R5-M20). A warm read is a lookup:
  no allocation. `HoverBuilder.Placement`'s root reads the same `Base` (N4).
- **Readers.** SLT keeps its (document, offset) memo, invalidated as R5-M20 states (its directory key becomes the root module's `Base`,
  compared by reference), and asks the name only on a miss. TestCatalog asks at discovery. Hover asks live (`HoverSourceName`: null for a
  built-in or generated document, else the name). Documentation stores the name at its parse (`SetLocation(name)`), as `SetLocationCore`
  does today: a documentation source is rebuilt by every parse, and a transferred one keeps its parse's name; the contract says so (N9).
- **Rule (total, no throwing `Path` call)**: a fully qualified path under the base is its remainder with the host's separators as `/`;
  any other path (outside the base, another drive, relative, `compiler://`) is its own text with the host's separators as `/`. "Under" is
  a prefix test, without allocation, against the normalized base: the host's separators compare equal (Windows: `\` and `/`), letters
  compare as `SourceIdentity.PathComparer` does (case-insensitive on Windows), and the prefix must end at a separator (`C:\p\App` does not
  contain `C:\p\AppX\A.kimi`; a root base such as `C:\` ends in one). Sources the project enumerates are already full normalized paths, so
  the result equals today's `GetRelativePath` for them. Changes, in-memory API inputs only (N11): a `/`-separated Windows path (`C:/p/App/
  Main.kimi`) is under its base as today; a path with `.` or `..` segments keeps them. A NUL inside the project keeps its relative name,
  never an absolute one (N3, R5-M20), which SLT escapes as `\u{0}` (IMPL 21.4.2).
- Hover's built-in rule is `SourceIdentity.IsBuiltIn` (`compiler://`): another `://` path, which today's `PhysicalLogicalName` nulls, gets
  its own text as its name; no source creates one today (N12).
- The parse-entry merge is dropped (N7): the name has no writer.

**Measurement plan.** Warm 0 B stays: `NoInitTest.WarmPhasesAllocateNothing` (162-169) and
`AbortEmissionTest.RebindReloadAndWarmPassesPreserveTheBuiltin`, both `CreateForTest` without a directory (the captured base, an SLT memo
hit, one reference comparison). Hover's repeated-lookup tests (`HoverStateTest.RepeatedCurrentAndHistoricalLookupAllocatesNothing`,
`HoverProjectionTest.IndexedLookupAllocatesNothingAndHonorsHalfOpenRanges`, `VirtualHoverTest.CachedVirtualFactsReleaseTheCompilerAndAllocateNothingOnRepeatedLookup`,
`HoverImprovementsTest.MeasuredWorkloadsReuseTheCompleteResponseWithinBounds`) stay: names are asked at a hover build, memoized. Warm
diagnostics do not read the name (the collection name is computed at parse). Cold cost (one memo per module, one string per named
document, one directory string per compilation) is measured in the unit's commit (time, allocation, retained memory; AGENTS).

## The NFC and case-fold check

No new code: a project's invalid source set is already `ProjectPreparationFailed_Kd` (Input, at the project file, cause as a note, as for
duplicate TestSources); a dependency's, DependencyResolver's existing failure. The data are pinned like `UnicodeIdentifierData` and
`MarkdownUnicodeData` (N5): Unicode 15.0.0, each source file's SHA-256, a generator script (as `GenerateMarkdownUnicode.ps1`), the
measured size and one check site (the module's names, at project preparation). NFC uses the compiler's own normalization
(`UnicodeIdentifierHelper`'s decomposition and composition), not the host's ICU; if its tables cover identifier scalars only, U9a-3 extends
them. Simple case folding needs a CaseFolding (C+S) table: `OrdinalIgnoreCase` misses `ẞ`/`ß` and `ſ`/`s` (probe); accepting it would
narrow the rule (AGENTS).

## Options

- **(A) Kotonoha owns the base (stored, with change points) and the rule (one memoized query); every reader switched. Recommended.**
- **(A′) Revision 0's `SourceDocument.LogicalName` stored at parse**: rejected (stale after the setter; four creators). **(A″) Revision
  1's base read at query time**: rejected (ambient state; a warm read allocates, B3).
- **(B) A shared helper** taking a base: rejected, every reader would still choose the base. **(C) Naming at the input layer**: rejected,
  in-memory, deserialized, generated and library documents need other paths.

## Behavior changes (fail-first, lowest public layer)

| Unit | Change | Test |
| --- | --- | --- |
| U9a-1a | A no-directory root's base is the captured current directory: documentation names `A.kimi` (absolute today), hover names it (null today), hover links resolve from it | `DocumentationSource.LogicalName`; `HoverSourceName`; HoverLinkTest |
| U9a-1b | A dependency's location and catalog input are `Lib.kimi`, not `../Lib/Lib.kimi` | `Locations.Text`; TestCatalog inputs |
| U9a-1b | A relative in-memory path is not resolved against the process directory (TestCatalog, the collection name) | catalog; snapshot |
| U9a-1b | A NUL path inside the project checks (no `CheckFaulted_Kd`; documentation's `SetLocationCore` throws too and switches here) and its location is relative, `\u{0}` escaped (G346's check half) | check result; `Locations.Text` |
| U9a-1b | A path under `C:\p\AppX` is not under the base `C:\p\App`; `C:/p/App/Main.kimi` is | the query's rows |
| U9a-2 | Hover names an other-drive source (null today); library and generated stay null | `HoverSourceName` |
| — | A non-Windows `\` stays a character; a current-directory change after creation does not change a compilation's names | by review (no Linux host; changing a process's directory in parallel tests is unsafe) |

## Split (each ≤ 1 h; N10: each unit's tests fail before it)

1. **U9a-1a** (≈ 45 min): `Kotonoha.Base` replaces `SourceDirectory` (stored; change points; the captured current directory; the NUL-safe
   normalization) with Kotonoha's contract header (N8); its readers today (`SetLocationCore`, `HoverSourceName`, `Placement`) follow, so
   the no-directory hover and documentation tests belong here. Net about +20 code lines, +30 test lines.
2. **U9a-1b** (≈ 1 h): the memoized query; SLT (path code gone, memo keyed by `Base`; header contract), TestCatalog (G289 (3) with its
   copy), `ParseSource`'s collection name and documentation's `SetLocation(name)` switched. Net about −40 code lines, +45 test lines.
3. **U9b** (G346, same merge as U9a-1b): SLT no longer throws, so G330b's row (`EmissionArtifactsTest.AnExceptionEndsTheCommandsResultAsFaulted`)
   loses its trigger. U9b finds another real exception inside `Publish`'s `generating` exclusion, or, if none exists, removes that
   exclusion with its row (R5-M169 amended). Design it before U9a-1b lands (N6). About 30 min.
4. **U9a-2** (≈ 45 min): hover's rule: `PhysicalLogicalName` gone, `HoverSourceName` the query with the built-in and generated rules,
   `HoverBuilder`'s cache removed (N1; no hold). Net about −25 lines.
5. **U9a-3** (≈ 1 h): the NFC and case-fold check with its pinned data; STATUS's Source row.

## Records

DECISIONS: a new entry (the base and its change points, the memoized query, the rule, the creator table, the measurement; rejected A′, A″,
B, C), amending R5-M20 (the directory key becomes the root module's `Base`; the rule moves to Kotonoha; the current directory is captured
at creation). ARCH: a canonical row "Logical source name | `Kotonoha.LogicalName` over `Kotonoha.Base` | `Core/Kotonoha.cs`" and the
Runtime-locations row reading it (N8). SLT's, Kotonoha's and `DocumentationSource`'s contract text (N8, N9). CODEMAP: "Hover source
links" loses `PhysicalLogicalName` (U9a-2). STATUS: at U9a-3. PLAN LN, G346 and G289 (3) close at the merges. Open issue for PLAN (N2):
SPEC 6.2.1's source order. `Project` sorts full paths in UTF-16 ordinal order and appends TestSources after the product sources
(`Project.cs:287-289, 483-505`), where SPEC orders documents by their names' UTF-8 bytes.

## Questions for the planning session

1. U9b lands with U9a-1b, and may remove R5-M169's `generating` exclusion if no other trigger exists: agreed?
2. U9a-3's fold table (generated, pinned data): proceed, or record the check as a STATUS known limit for now?
