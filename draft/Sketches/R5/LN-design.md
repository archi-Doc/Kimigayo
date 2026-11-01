# LN design: one owner of a source's logical name (R5 U9a, with G289 (3))

Base `abc7e28c7` (dev). Design only; implementation waits for the planning session's GO. Major (a new representation). Probe: the
copies' expressions copied verbatim from the base and run on this host (.NET, Windows), plus a case-folding probe (scratchpad
`lnprobe/probe.cs`, `fold.cs`); rows marked (Linux) are read from the code, not run.

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
  without a directory (an API construct; R5-M20 took the current directory). Generated Mod documents have none (SPEC 6.2.1 orders them
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

## Owner, representation, lifetime

- **Owner: Kotonoha** (owner-confirmed 2026-10-19). Documents enter through two parse entries that repeat one tokenize-parse-document
  sequence: `Kotonoha.ParseSource` (`AddSource`, `OnDeserialized`'s reparse) and `CodeContext.Parse(…, SourceDocument, modId, order)`
  (Mod-generated documents, text parses, the library), each calling `SetLocation(SourceDirectory)`. They become one Kotonoha entry (its
  diagnostic collection an input), the name's one writer. It computes the name once per parse from its **base**: the root module's
  project directory, or the current directory captured once when the compilation prepares its modules if the project has none; a
  dependency's project directory (`Url`'s, as `SourceDirectory` does today); none for `compiler://` and generated documents.
- **Representation: `SourceDocument.LogicalName`** (`string?`, `[IgnoreMember]`, written by Kotonoha only), read through the document
  every reader already holds (SLT via `node.CodeContext.SourceDocument`, TestCatalog, `DocumentationSource.Source`, hover). A document
  object is shared only among one Project's compilations (`additionalSource`; disk and LSP inputs are created per compilation), which
  share the base, so each parse writes the same value (contract invariant).
- **Rule (total)**: a fully qualified, NUL-free path under the base is its relative path with the host's separators as `/`; any other
  path (outside the base, another drive, relative, `compiler://`, NUL) is its own text with the host's separators as `/`. Only the first
  case calls a `Path` API. Display escaping stays SLT's (IMPL 21.4.2).
- **Invalidation**: a reparse rewrites it; SLT's memo drops its directory key (its texts follow the documents, already cleared by
  `InvalidateSourceAnalysis`), so the current-directory gap closes with the capture.

## The NFC and case-fold check

No new code: a project's invalid source set is already `ProjectPreparationFailed_Kd` (Input, at the project file, cause as a note: e.g.
duplicate TestSources); a dependency's, DependencyResolver's existing failure (as its duplicate TestSources). NFC: `IsNormalized()`
(TestCatalog already normalizes the project identity with FormC; `UnicodeIdentifierHelper` covers identifier scalars only). Simple case folding needs data: .NET's `OrdinalIgnoreCase` is uppercase mapping and misses folds
(probe: `ẞ`/`ß`, `ſ`/`s` compare unequal; Kelvin and Ohm signs fail NFC first), so the check needs a generated CaseFolding (C+S) table
beside `UnicodeIdentifierData`. Accepting `OrdinalIgnoreCase` would narrow the rule (AGENTS): not proposed.

## Options

- **(A) Inside Kotonoha, published on the document, every reader switched. Recommended.** One base decision, one rule, one value.
- **(B) A shared helper** `LogicalName.Of(path, base)`. A pure function with one contract is not a utility bag, but every reader would
  still choose its base, which is where the dependency and no-directory bugs live ("a consumer deciding … what its owning component
  should publish"), and it computes per read. Rejected.
- **(C) The input layer names documents** (Project, DependencyResolver): rejected, in-memory, deserialized and generated documents need a
  second path. **(D) A Kotonoha-side map**: rejected, document-only readers would need the module too.

## Behavior changes (fail-first, lowest public layer), deletions, size

| Change | Test |
| --- | --- |
| A dependency's location and catalog input name `Lib.kimi`, not `../Lib/Lib.kimi` | `Compilation.Locations.Text` (AbortEmissionTest); TestCatalog inputs |
| Doc and hover name a no-directory and an other-drive source like SLT (`A.kimi`; `D:/x/Main.kimi`) | DocumentationSource.LogicalName; `HoverSourceName` |
| A relative in-memory path keeps its name in the collection and TestCatalog | diagnostic location path; catalog |
| A NUL path checks and its location escapes `\u{0}` (G346) | Locations.Text; check result not Faulted |
| Linux `\` stays a character (unverified on this host; Windows CI) | by review |

Deletions: SLT's path code and directory memo key, `PhysicalLogicalName`, `SetLocationCore`'s computation, TestCatalog's
`LogicalPath` (G289 (3) with it), ParseSource's `GetRelativePath`, one of the two parse sequences. Expected net about −30 code
lines, +50 test lines.

## Split (each ≤ 1 h; readers cannot switch before the rule changes: every reader differs on some input)

1. **U9a-1**: commit 1, behavior-preserving: the two parse entries become one Kotonoha entry (diagnostic snapshot 0, `.ll` identical).
   Commit 2: the owner, the property, SLT and TestCatalog switched, G289 (3), G346's NUL row and regression. It removes SLT's
   `GetRelativePath`, the trigger of G330b's row (`EmissionArtifactsTest.AnExceptionEndsTheCommandsResultAsFaulted`), so **U9b** (G346)
   reduces to that row's new trigger and must merge with U9a-1; design it first.
2. **U9a-2**: DocumentationSource's two copies, the collection name, `HoverSourceName` reading the property. `HoverBuilder.cs` is a hold
   file (R5-M7): its cache stays until a hold; staged removal recorded.
3. **U9a-3**: the NFC and case-fold check (fold data generation), then STATUS's Source row.

## Records

DECISIONS: a new entry (the representation, base, rule, readers; rejected B-D), amending R5-M20 (no-directory base, memo key). ARCH: a
canonical row "Logical source name | `SourceDocument.LogicalName`, written by `Kotonoha.ParseSource`" and the Runtime locations row
reading it. CODEMAP: "Hover source links" loses `PhysicalLogicalName`. STATUS: at U9a-3. PLAN LN, G346 and G289 (3) close at the merges.

## Questions for the planning session

1. U9b merges with U9a-1 (G330b's row needs its new trigger in the same merge): agreed?
2. U9a-3's fold table (generated data, size to measure): proceed, or record the check as a STATUS known limit for now?
