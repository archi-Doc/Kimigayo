# LSP Code Completion: Specification Change and Implementation Plan

Date: 2026-10-07 (rewritten 2026-10-08, revised 2026-10-09). Status: proposal. Not yet integrated into the specification, implemented or verified.

## 1. Purpose and principles

### 1.1. Authority

This proposal has no normative force. [SPEC.md](../../docs/SPEC.md) and its chapters govern until the intake of §8 moves this proposal's rules into the formal specification, which then stands alone and does not refer to this proposal.

### 1.2. Goal

Add code completion to the language server. Completion reads the adopted analysis set that Hover reads and does no request-time compilation or check scheduling (§4.1).

### 1.3. Priorities

1. Request latency and steady-state allocation.
2. Added check cost. Ordinary CLI compilation pays nothing.
3. Candidate precision. It is lowered deliberately; §4.1 states the guarantee and §7.2 lists every trade-off.

### 1.4. Unchanged

Name, access, Type, ownership and effect rules; diagnostics; code actions; check scheduling; Hover's rendering. Hover changes only where §2.2 (a missing Hover part) and §2.4 (the region limit) say.

## 2. Shared editor analysis

Hover's participant, adoption, retention and edit-mapping rules (SPEC §23.4.11.4–5) become one shared mechanism. Hover and completion only read it; neither has its own adoption, retention, expiry or check scheduling.

### 2.1. Adopted analysis set

Each open, synchronized `.kimi` document has at most one adopted analysis set.

| Part | Content |
| --- | --- |
| Participants | Unit keys in canonical order (project identity, unit kind, target), each with its adopted result generation |
| Unit editor projections | One per participant (§2.5) |
| Edit map | Edits since adoption (§2.4) |
| Current flag | True after adoption or revalidation. False after an edit to the document, or after an input event leaves a participant's result held or invalid (SPEC §23.4.5), until revalidation or the next adoption; a set whose participants stay valid stays current while other checks are pending. Hover's previous-analysis notice reads it; completion does not |

The **representative unit** is the first participant.

- Hover compares the requested Hover target in the representative with the same Hover target in every other participant. Agreement is symmetric, so this is today's behavior.
- Completion reads only the representative.

### 2.2. Participants and adoption

- **Participants:** every required unit of every project whose source-selection rules check the document, including units without results. A consumer that reads the document only as a dependency is excluded, and so is a Product unit that does not check a TestSources-only document.
- **Adoption:** the set is adopted when membership and a nonempty participant set are established and every participant has a valid Completed result. Accepted is not required, and generation times may differ. An unfinished, held, Blocked or Faulted participant is never omitted to obtain a set.
- **Missing parts:** a unit editor projection part may be missing. The set is still adopted; only the reader of that part is unavailable.
- **Change from today:** a set whose Hover part is missing is adopted, and Hover answers `null` at once. Today such a set is not adopted, so a previous set can remain while an unrelated check is pending. Rationale: one adoption rule serves every reader (Principle 1), and valid results are a settled outcome for the current inputs.
- **Re-evaluation:** the set is re-evaluated after each worker product (input commit, derivation, unit result, products result, end of a check) and after revalidation, independently of diagnostic publication. A plain input event only updates the edit map and records that re-evaluation is due; it runs before the next request. Today the full re-evaluation runs on every keystroke.

### 2.3. Retention

| Event | Action |
| --- | --- |
| A complete valid set becomes available, including recovery by revalidation alone | Replace atomically; release the old set and its edit map |
| Ordinary source edits leave Test-unit necessity or similar facts undetermined | Keep the set |
| Configuration or project-setting input; membership or placement change; close and reopen; loss of synchronization | Discard |
| Full replacement; edit-map limit | Discard; never recover by spelling or proximity |
| The check for the document's latest input finishes without an adoptable set (Blocked, Faulted) | Discard; later edits do not resurrect it |
| New input arrives during a check and the next check is pending | Keep |

### 2.4. Edit map

Edits since adoption are kept as disjoint **regions** in current coordinates. Each region pairs a current range with an old range.

- An edit that overlaps or touches a region merges into it, and the old range becomes the union. Touching uses closed intervals. Zero-width and identical replacements count as edits.
- A position outside every region maps to the old text by the accumulated length difference. Mapping costs O(log regions); applying an edit costs O(regions).
- The edit-region limit (§2.6) counts regions. Continuous typing stays in one region. This changes Hover's edit-history limit from counting edits to counting regions.
- Hover's rule (an edit that touches a Hover target's range, its interior or a boundary, forbids reusing that target) is equivalent to: some region's old range touches the Hover target's old range. Merging joins only touching edits, so any Hover target that a merged region touches was touched by one of its edits at that edit's step. A randomized differential test against today's per-step algorithm confirms the equivalence.

```csharp
// Sketch. Regions are sorted by CurrentStart and never overlap.
internal readonly record struct EditRegion(int CurrentStart, int CurrentEnd, int OldStart, int OldEnd);

// Maps a current offset to the old text; returns false with the containing region when the offset is new text.
internal bool TryMapToOld(int offset, out int old, out EditRegion region);
```

### 2.5. Unit editor projection

The unit editor projection is an optional check output. Only the language server requests it, through one collection request.

| Part | Produced by | Content |
| --- | --- | --- |
| Hover part | Every unit | Today's Hover document indexes |
| Completion part | The **producer**: in each project, the first unit in canonical order that checks the document | One entry per produced document: scopes, bindings, aliases, the Contract members each scope's constraint environment proves for its type parameters and Contract `Self` (and, once per part, those of each primitive Type), `#if` regions and multi-line token spans |
| Module projections | At least every producer; an implementation may collect them in more units | One reference per module of the unit (§3) |

- The **producer rule** looks only inside one project. The representative, the first unit of the first project, is therefore always the producer for its document, and no cross-project computation is needed. Its part can still be missing after a failure (Isolation).
- **Isolation:** each part has its own failure boundary. A failure is logged and leaves that part missing. It never changes diagnostics, acceptance, TestPresence or another part. Input-invalidation and cancellation control flow is not swallowed.
- **Detachment:** no Koto, BindingSymbol, BoundType or compilation reference is retained.
- **Coverage:** every checked own-project `file:` member, including closed documents, because opening a document does not recompile a valid unit.
- **Lifetime:** unit results and adopted sets hold the parts. The parts are immutable, shared by reference and released by the garbage collector. There is no reader-specific store, reference count or LRU.

### 2.6. Limits and budgets

One `EditorLimits` table serves both readers; it generalizes `HoverLimits`. The shared entries are the edit-region limit (256) and the depth limit (64). Hover keeps its other entries; completion's limits are in §4.6 and module projections' in §3.2.

`EditorBudget`, the generalized `HoverBudget`, throws a limit exception. It guards check-time building of every part and Hover rendering. A completion request instead counts its work in a local integer and never throws; §4.6 gives each limit's outcome.

## 3. Module projections and the module projection cache

### 3.1. Module projection

A **module** is one compiled project node (one Kotonoha) or the Kimi library. Its projection contains:

- **Declaration table.** Entries follow a deterministic walk: each container's `Members` in order, then an implicit constructor after its struct's members, then nested containers. An entry's ordinal is its locator, valid only for the same ModuleInputId (SPEC §18.7.1). Each entry has a kind, a name, its parent ordinal, its own arity, flags, a detail line (§3.4) and term indices: a Property's declared type, and a function's result and parameter types.
- **Type terms.** Primitive, nominal (a declaration reference (module, ordinal) with type arguments), generic parameter (owner and slot), Contract Self, a Semantics or reference layer, function, tuple, fixed array, and Unknown (§3.2). Terms carry no display text.
- **Member tables.** For each container, its own members sorted by name in UTF-16 code-unit order, each with a namespace, a kind, instance or type level, an overload count, a conditional premise and an access descriptor; plus the base terms and the unique index result, if any. Inherited members, from the same or another module, are not stored; a query merges them with base-argument substitution.
- **Module tree.** The module's own references and default aliases, Kimi, and a test-only flag on a Test root's TestDependencies.

It excludes Hover's use-site facts, Hover's Details and Identity (both read compilation-wide state), positions, documentation, per-document `alias` declarations (they belong to the completion part), diagnostics and compilation-wide failure flags.

### 3.2. Purity

A module projection depends only on the module's ModuleInputId (SPEC §18.7.1) and its role (root, dependency or library).

- It reads only its own module and the projections of its dependencies.
- It never reads compilation-wide state: Hover anchors and failure flags, the native-import table, compilation-wide associated-inference evidence or analysis progress.
- Each module has its own budget: 1,048,576 elements and 4,194,304 code units of text. A limit exception on one declaration removes only that entry's detail. Exceeding the module budget turns only that module into a failure record.
- Error types, and references from other modules or from the completion part to a failed module's declarations, are Unknown terms.

### 3.3. Cache

**Key.** The key is derived from the module's ModuleInputId (SPEC §18.7.1). Session-local revisions stand for the raw input identity, the compiler build and verification rules are fixed for the process, and the role is added. Keys compare structurally; hashes may only index (SPEC §18.7.2).

| Component | Value |
| --- | --- |
| Identity | Root: the project file path. Implicit project: its single source path. Dependency: its node's input path. Kimi: `compiler://Kimi` |
| Role | Root, dependency or library; kept because Hover's display text (Owner) depends on the role (§6.6) |
| Mode | Product or Test, for the root only |
| Environment | Target (it also fixes pointer width), debug, and the language |
| Inputs | Every (input key, revision) attributed to the module: its project file, directory listing and sources, and the TestSources of a Test root |
| Dependencies | (reference name, key) for each module it references, including `Kimi` |

**Attribution.** An input belongs to every module whose manifest lists it, because two project files can share a directory and its sources. The lock file and the inputs of merged dependency nodes form a unit-level set that no module key contains.

**Uncacheable modules.** A module is uncacheable (it has no key and is rebuilt in every check) when any of the following holds:

- one of its inputs has no committed revision (a first read keeps revision 0 until its result is adopted);
- it is a file-less root with several sources (tests only);
- one of its documents is not in its manifest source set, or is generated (`Project.AddSource`, Mods);
- a dependency has no key;
- its projection failed.

If a recorded input of the unit cannot be attributed, every user module of the unit is uncacheable; this also covers future input kinds such as Mod assemblies. The Kimi library has no recorded inputs or dependencies, so its key reduces to (Kimi, language, target, debug), and only a failed projection makes it uncacheable. Library directives currently see the root's variables, which does not yet meet SPEC §18.4.3. The library has no directives, so this is unobservable, and a guard test fails if the embedded library sources gain any.

**Hit.** On a key hit, the fresh compilation's declaration walk is compared with the cached entries by (kind, name, parent). The walk also maps the fresh compilation's symbols to the cached ordinals, so rebuilt modules can reference the cached ones. A disagreement is a counted miss and a logged fault note, never stale data.

**Ownership and retention.**

- The session owns an immutable index. Each check receives the snapshot captured when it starts.
- The index is replaced only while the state owner processes the end of a check, the one point of exclusive access.
- An entry lives while a current unit result references it (SPEC §23.4.6). There is no LRU and no count limit; invalidation is per module (SPEC §18.7.3).
- Dispose only drops the index reference, because a check may still be running.

**Transparency.** A cached projection and a fresh projection are structurally equal, so readers cannot observe the cache.

**Value.** Binding still runs over every module in every check. The cache saves projection extraction (headers, type display, member tables) for unchanged modules, and shares memory between the Product and Test units of one target and across generations. The target is in every key, so units of different targets share nothing. Gates G1–G3 measure the benefit before each part of the cache is kept (§6.4).

### 3.4. Declaration display

Completion detail uses Hover's display functions, so a declaration looks the same in both places.

| Declaration | Detail |
| --- | --- |
| Function, type, Contract | The line of the Hover header that contains the introducer; the declaration's own attribute lines are skipped |
| Property | `name: ` followed by Hover's type display (`HoverTypeName`) of its verified type, inferred types included; an unverified Property gets the kind word |
| Local, parameter | `name: ` followed by `HoverTypeName` of its type, as in Hover's variable header (built by the completion part) |
| Group, alias, enum Case, associated type, module, implicit constructor | The kind word |
| Overload group with several declarations | `N overloads` |

A detail is at most 128 UTF-16 code units: a longer one keeps at most 127 units, never splitting a surrogate pair, followed by `…`. Module declarations get their detail once, inside the module projection. Hover's type display may contain a body-local Origin position; such text comes from the sources of the module or its dependencies, so purity holds.

## 4. Code completion

### 4.1. Meaning

- **Documents:** open, synchronized `file:` documents with the `.kimi` extension.
- **Candidates:** names, members and keywords reachable from the mapped position (§4.3) in the representative unit's adopted analysis.
- **Not guaranteed:** reachability in other units; name selection, acceptance, argument fit, Move, Loan or effects in the current text; freshness.
- Responses do not mark whether the analysis is current.
- Requests never compile, parse, bind or read the disk; never schedule, advance or wait for a check; and never change diagnostic publication.

### 4.2. Line context

Request-time lexing covers only the cursor line and follows the lexical rules of SPEC §2.

1. **Line start.** If the line start lies outside every edit region and the representative's completion part records it inside a multi-line comment or literal (interpolations included), return an empty list. A line that starts inside a region starts in code.
2. **Scan.** Scan from the line start to the end of the token that contains the cursor, at most to the line end. States: Code, BlockComment, EscapedText, RawText(N), Interpolation(depth). A number after a member `.` is a tuple index, as in the Tokenizer. If the cursor is in a comment or in literal text, return an empty list.
3. **Range.** The identifier around the cursor is the range [start, end), and [start, cursor) is the prefix. Without an identifier, the range is empty at the cursor.
4. **Context.** The significant token before the identifier selects the context:

| Before the identifier | Context |
| --- | --- |
| Nothing (first token), `=>`, or a leading run of the declaration modifiers `public` `internal` `private` `protected` `open` `virtual` `override` `unsafe` `specialize` | Line start |
| `.` with a receiver on the same line (not part of a number or `..`) | Member |
| `::` | Qualified path |
| `@`, `$`, `#`, or `.` without a receiver (including a line that starts with `.`) | Empty list |
| Anything else | Expression or type |

Type and expression positions are not distinguished, because telling them apart needs grammar (named arguments, labels, declaration colons). Both namespaces are offered, so a type position only gains extra value names.

### 4.3. Position mapping

The request finds an old position p and a scope S.

1. **Origin.** x is the identifier start; in member and path contexts it is the start of the receiver chain.
2. **Existing line** (the line start is outside every region). p is x mapped to the old text, or the region's old start when x lies in a region. S is the innermost scope that contains p, as a closed interval.
3. **New line** (the line start is inside a region). p is the region's old start. Walk up from the current line to the region's first line, skipping blank and comment-only lines; the threshold t is the smallest indentation seen, starting with the current line's. Starting from the innermost scope that contains p, S moves outward while the scope's body baseline exceeds t. A body baseline is the header baseline plus 4; the file scope's baseline is 0.
4. **Visible bindings.** The bindings of S and its ancestors whose visibility start is at most p, inner scopes first. The first name found in each namespace wins.
   - Runtime bindings (locals, parameters, loop and pattern bindings, `self`, `value`, `storage`) of scopes outside the nearest enclosing named function are skipped (SPEC §9.2). Across an anonymous function, an outer runtime binding stays visible only when the function lists it in its Capture List, or has no list and the binding is not contextual (SPEC §7.6.2).
   - A Container whose own same-name declarations in a namespace are all instance members, or a struct without own same-name declarations whose base layers declare an accessible name in that namespace, ends the search for that name in that namespace (SPEC §9.4). The name is not offered, and outer declarations of that name stay hidden.

The completion part records visibility starts from the compiler's rules. A local starts at the end of its declaration statement, after its initializer (SPEC §9.2). A loop binding is visible only in its loop body. A pattern binding starts after its Pattern and is visible only in its own arm (SPEC §14.8.2). Other declarations start at their scope start.

Consequences: outer names stay visible inside new nested blocks, and a dedented line leaves the blocks it closes. Names declared in new lines, bindings of new headers and broken old scope boundaries are not seen (§7.2).

The cursor is shown as `|`; it is not Kimi syntax. Suppose the adopted set came from this text:

```text
func load(path: ref/string) -> i32
    let text = read(path)
    return 0
```

The user then adds the lines marked `+`:

```text
  func load(path: ref/string) -> i32
      let text = read(path)
+     if check(text)
+         te|           ← t = 4: S stays in load's body; path and text are candidates
      return 0
+
+ func save()
+     te|               ← t = 0: S moves to the file scope; text is not a candidate
```

### 4.4. Candidates

#### 4.4.1. Unqualified names

- Names of the Value and Type namespaces. The Type namespace includes groups, modules and aliases, so the qualifiers that start a member path are offered as ordinary names.
- After the scopes, the remaining stages of SPEC §9.4 follow, each only for names not found earlier: the reserved `Kimi` and direct-dependency reference names, then the document's explicit aliases (named aliases and the direct members of each `alias Path`), then the default aliases, including the mandatory `alias Kimi` (SPEC §18.1.3). Equal references within one stage are offered once. At every stage only accessible names count, by the access rule of §4.4.2: an inaccessible name is neither offered nor hides a later stage (SPEC §9.4).
- Instance members and inherited declarations are never offered unqualified, because there is no implicit receiver (SPEC §9.4). Instance members are completed after `self.` or another value receiver, and other inherited declarations after `Self.` or the type name.
- The contextual bindings `self`, `value` and `storage` come only from scopes, under the function-boundary rule of §4.3.
- Same-name functions of one scope form one item.

#### 4.4.2. Members and qualified paths

The receiver chain is read backward on the same line.

| Element | Resolution |
| --- | --- |
| Leading identifier | Resolved from S separately in the Type namespace (Qualifier role) and the Value namespace, each committing at its own first eligible stage (SPEC §9.5). Each path continues on its own; the final members are those reachable on exactly one path, and a name that both paths offer is omitted as ambiguous |
| `Self` | The enclosing type, as a type receiver |
| Leading `::` | The Compilation root (SPEC §9.5): the unit's project-root declarations and its direct-dependency reference names, `Kimi` included. No local scope or alias is searched |
| `.name` | The name in the previous element's members. A field or Property continues with its declared type; a nested type or module continues with itself |
| `(…)` | Continues with the result type when the preceding name is a function group with exactly one declaration whose result type follows from the receiver's type arguments alone |
| `[…]` | Continues with the element type when the type has a unique index result (fixed arrays, or a type with a single index operation) |

- Brackets must be balanced on the line; their contents are not analyzed.
- An element that cannot be resolved ends the chain with an empty list; nothing is guessed.
- **Member sources.** Member lookup first removes Semantics and reference layers.
  - Struct, enum, group-qualified and fixed-array receivers use the module member tables. Base layers are visited nearest first, and an accessible name in a nearer base layer wins; inaccessible names do not stop the walk, as in the compiler's member lookup.
  - Type-parameter and Contract receivers get the members of the Contracts that their use-site constraint environment proves. Primitive receivers get the members of `Utf8Format`, `Equatable` and `Comparable` as the compiler's type classification assigns them. Composite (tuple, function) receivers give an empty list.
  - Associated types are not resolved.
- **Final element.** A value receiver offers instance members. A type or `Self` receiver offers type-level declarations and instance functions, which form unbound references (SPEC §7.3); instance Fields and Properties are not offered after a type.
- **Access.** Each member carries a descriptor of its effective access domain (SPEC §9.3.1). A member is offered when the cursor's scope chain lies inside its domain, evaluated before inherited members are merged. Completion does not check the protected-receiver restriction or test-only access; these are its only deviations from the compiler's accessibility.

#### 4.4.3. Keywords

| Class | Spellings |
| --- | --- |
| Line start (offered together with the expression class) | `let` `var` `func` `for` `while` `return` `exit` `continue` `yield` `require` `defer` `else` `public` `internal` `private` `protected` `init` `drop` `alias` `rootgroup` `group` `struct` `enum` `contract` `computed` `property` `virtual` `override` `open` `specialize` `unsafe` |
| Expression or type | `if` `match` `loop` `do` `try` `not` `true` `false` `null` `Self` `base`; the primitive Types `isize` to `string`; the Semantics `owner` `ref` `uniq` `obj` `rc` `arc` `objref` `objuniq` `raw` |
| Never | Every other spelling of SPEC §2.5.1, the contextual words of SPEC §8.4.10.1 (`effect`, `confined`, `preserves`, `results`), and the contextual bindings `self`, `value`, `storage` |

- The class is an attribute of each spelling, independent of whether the spelling is reserved or contextual. Every SPEC §2.5.1 spelling and every listed contextual word has exactly one class.
- When semantic candidates cannot be determined, the response contains only the keywords of the context's class; member and qualified-path contexts have no keyword class and return an empty list. This happens when there is no adopted analysis set, when the representative has no completion part or module projections, when the position lies in a region excluded by `#if`, or when the new-line walk-up exceeds its limit (§4.6).

### 4.5. Response

**Capability.** The server advertises `completionProvider: { "triggerCharacters": ["."] }` and does not advertise `resolveProvider`. After `::`, completion starts on manual invocation or on the next identifier character.

**Result.** `textDocument/completion` always returns a `CompletionList`. An unsupported document, and every empty-list outcome of §4.2–4.4 and §4.6, returns an empty list with `isIncomplete: false`. Malformed parameters, and requests before `initialize` or after `shutdown`, follow the existing JSON-RPC rules.

**`isIncomplete`.** It is `true` only when a limit of §4.6 made the server filter or truncate. Normally the server does not filter by prefix; the client does. Only when the request-work limit stops enumeration or the response limit is exceeded does the server keep candidates that match the prefix as a case-insensitive subsequence, and then truncate in enumeration order if still needed.

**Edit range.** The insert range is [identifier start, cursor) and the replace range is [identifier start, identifier end).

- If the client lists `editRange` in `textDocument.completion.completionList.itemDefaults`, the range is sent once in `itemDefaults.editRange`: `{insert, replace}` when `completionItem.insertReplaceSupport` is declared, otherwise the insert range.
- Otherwise each item carries a `textEdit` built by the same rule.

**Items.** An item has only `label` (equal to the inserted text), `kind`, `detail` (§3.4) and `sortText`. No `filterText`, `insertText`, snippets, commit characters, additional edits, commands, documentation or preselection.

**`sortText`.** One character:

| Value | Items |
| --- | --- |
| `0` | Locals and parameters of the innermost function, or members of the receiver's own type |
| `1` | Other names and inherited members |
| `2` | Keywords |

The client orders items within a tier.

**`kind`.** Kinds 1–18 belong to the LSP base set that every client supports. Struct (22), EnumMember (20) and TypeParameter (25) use the fallback when the client's `completionItemKind.valueSet` lacks them.

| Declaration | Kind | Fallback |
| --- | --- | --- |
| Local, parameter, loop or pattern binding; the contextual bindings `self`, `value`, `storage` | Variable (6) | — |
| Property (member `let`, `var`, `computed`; Contract `property` requirement) | Property (10) | — |
| Member function of a type, or a Contract function requirement | Method (2) | — |
| Other function | Function (3) | — |
| `init` | Constructor (4) | — |
| struct | Struct (22) | Class (7) |
| enum | Enum (13) | — |
| enum Case | EnumMember (20) | Value (12) |
| Contract | Interface (8) | — |
| group, rootgroup, module | Module (9) | — |
| Type parameter, associated type | TypeParameter (25) | Class (7) |
| alias | Reference (18) | — |
| Keyword, including primitive Types | Keyword (14) | — |

**Processing.**

- Requests run synchronously on the state owner against document events that have already been applied.
- There is no asynchronous completion job, no per-request result cache and no notification window.
- Each request gets exactly one response; cancellation and shutdown follow the existing rules.
- New client-capability fields are read tolerantly, so a malformed optional field cannot fail `initialize`.
- An open list is not refreshed after adoption; the next request sees the new set.

### 4.6. Limits

| Resource | Limit | When exceeded |
| --- | ---: | --- |
| Cursor-line scan | 4,096 UTF-16 code units | Empty list |
| New-line walk-up | 1,024 lines and 65,536 code units | Keywords only |
| Receiver chain | 16 elements | Empty list |
| Type substitution depth | 64 (shared, §2.6) | Empty list |
| Request work (bindings and members visited) | 65,536 | Stop in enumeration order; `isIncomplete: true` |
| Response | 512 items | Prefilter, then truncate; `isIncomplete: true` |
| Completion part per unit | 1,048,576 elements; 4,194,304 code units of text | That unit's completion part is missing for every document it produces |

Enumeration visits scopes from inner to outer, and the names of one scope in UTF-16 code-unit order. Because this order is deterministic, a truncated result depends only on the input, never on time. Measurements may adjust the numbers with recorded evidence; the limits and their outcomes stay.

### 4.7. Examples

```text
cou|                   → count, visible at the mapped position in the representative's set
::Kimi.Con|            → Console; insert and replace ranges are both "Con"
items.len|gth          → length; insert range "len", replace range "length"
items.|length          → members of items' type; insert range empty, replace range "length"
self.items.first().|   → first has one declaration returning Option<T>, so the members of Option<Entry>
values[i].|            → members of the element type, when the index result is unique
make(x).|              → empty list when make has several declarations
public fu|             → func (line start after a modifier)
x@mo|                  → empty list (`@` operations are out of scope)
```

### 4.8. Out of scope

- Completion in `.kimiproj` documents.
- Definition navigation, Signature Help, documentation, `completionItem/resolve`.
- Dedicated completion of Origins, Labels and named arguments.
- `@` operations, and enum Case completion from an expected type.
- Call snippets; automatic aliases or dependency settings.
- Automatic qualification, such as inserting `self.count` for `cou`.
- Literal, associated-type, composite and `base.` receivers, and receiver chains across leading-dot continuation lines.
- Type refinement (SPEC §14.10) in receiver types.

## 5. Implementation design

### 5.1. Data

| Data | Owner | Content |
| --- | --- | --- |
| Completion part | Unit editor projection; one entry per produced document | Per document: scopes sorted by start (range, parent, kind, owner declaration reference for container, function and accessor scopes, body baseline, binding slice, function boundary kind (named, anonymous without a list, anonymous with a list) and listed capture names); bindings (name, namespace, kind, visibility start, declaration reference or type term, detail); per scope, the requirement members its constraint environment proves for each visible type parameter and Contract `Self`; once per part, those of each primitive Type; per-document `alias` declarations; regions excluded by `#if`; spans of multi-line comment and literal tokens. A scope's range is the union of the spans of every compiler scope key mapped to it; a document's top-level scope spans the whole document |
| Module projection | Unit result, shared by reference; the cache index also references keyed projections while a current unit result does (§3.3) | Declaration table, type terms, member tables, module tree (§3.1) |
| Module manifest | Unit result | Per module: identity, role, mode, environment, reference edges, input set, uncacheable reason |
| Module keys and statistics | Unit result | One key per manifest module (or none), and hit, miss and mismatch counts |

Type arguments are substituted at request time by pushing (type term, environment) pairs onto a work area of the shared depth limit (§2.6); no type term is created.

### 5.2. Components and entry points

| Component | Entry points |
| --- | --- |
| Edit map | New `Lsp/EditMap.cs`. It replaces the edit list in `Lsp/HoverState.cs`; the edit hook is `OnDidChange` (`Lsp/LspSession.cs`, the `Edited` call). |
| Shared set | New `EditorAnalysisSet`, extracted from `RefreshHover` (`Lsp/LspSession.Hover.cs`) and from `HoverState`. `OpenDocument.Hover` becomes the shared set. Hover keeps its agreement and render cache. |
| Limits and budget | `Checking/HoverLimits.cs` becomes `EditorLimits` and `EditorBudget`. |
| Editor projection | `CheckOutput.Hover` and `HoverFault` become `EditorProjection` with per-part faults. `collectHover` becomes one collection request (`Checking/CheckContext.cs`, `Checking/CheckService.cs`, `RunCheck` in `Lsp/WorkspaceCheck.cs`); `SolutionAndProject/Project.cs` arms the compilation's collection flags, including the completion Tokenizer hook, before parsing. `WorkspaceCheck.Discover` records each plan's completion scope in a new `UnitPlan` field: the project's Product plan that comes first in canonical order covers the project's product members (targets compare in ordinal string order, as `UnitKey.CompareTo` does, not in the order of the project's `Targets`), the Test plan its TestSources-only members (`SourceDocument.IsTestOnly`), and every other plan none. `RunCheck` passes it in the collection request, and the producers-only collection scope (§6.4) reads the same field. Parts are built in a fixed order, the Hover part, then module projections, then the completion part, because building can intern into Binding caches (M20, if kept, builds module projections first; Appendix A). |
| Module projections | `Compiler/Binding/Binding.ModuleProjection.cs` (builder); `Checking/ModuleProjection.cs`, `TypeTerm.cs`, `ModuleMemberTable.cs`, `ModuleAccess.cs`, `ModuleMemberQuery.cs`, `EditorDisplay.cs` |
| Module cache | `Checking/ModuleManifest.cs` and `ModuleProjectionSource.cs` (an abstract seam reached through `CheckInputSource`, so no Checking type depends on Lsp and the Runner signature is unchanged); `Lsp/ModuleKey.cs`, `CachedModuleProjections.cs`, `ModuleProjectionIndex.cs`, `ModuleInputAttribution.cs` |
| Completion part | New `Compiler/Binding/Binding.CodeCompletion.cs`. It reads the indexer's `scopes` and `symbols` and does not walk the AST again. It uses `Locate`, `Term` and `ValueDetail` of the module projection context while the compilation is alive. Multi-line token spans come from the optional Tokenizer hook (the `Compiler/Core/Compilation.Hover.cs` pattern). |
| Lexical step functions | Static helpers move unchanged out of `Compiler/Lexing/Tokenizer.cs`. New static `MeasureWord` and `MeasurePunctuation`, whose agreement with the Tokenizer the C1 parity test enforces, plus string step functions extracted from `StringLiteralHelper`. The Tokenizer's hot switch is untouched. |
| Text access | New internal line accessors on `Lsp/TextDocument.cs`: line start and end, and a copy across the gap. The request path never uses `LineStarts` or `ToString`. |
| Request | New `Lsp/CodeCompletionContext.cs` (line, context, range, mapping) and `Lsp/CodeCompletionQuery.cs` (enumeration, receiver chain, access, prefilter). |
| Protocol | `LspMethods`, `LspMessageReader.Methods` and `ParamsType`, the `OnMessage` switch, `ServerCapabilities` (`CompletionOptions`), client-capability parsing, `LspJsonContext`, and the raw-JSON response value: the state owner writes the response JSON into a pooled buffer, so the pump thread cannot fail on it. |
| Shared test helpers | `tests/xUnitTest/ProjectionGraph.cs` (structural equality, detachment and immutability oracles) and `tests/Workloads/LspCheckDriver.cs` (a real-check session driver for tests and Benchmark) |

### 5.3. Performance targets

These are review thresholds, not language guarantees or timing tests. When a target is missed, the cause is recorded, fixed and measured again.

| Subject | Target |
| --- | --- |
| Request on the state owner, including JSON writing | p95 ≤ 2 ms for ordinary requests; ≤ 5 ms near the limits |
| Request allocation | Zero in steady state, except the queued response value; the pooled buffer is returned |
| Input event | O(regions) edit-map update; no per-keystroke re-evaluation |
| Check: completion part | ≤ 2% of the median check time of the same workload |
| Check: module projections | ≤ 3% of the median check time (with the cache: once dependencies are unchanged); gates G0 and G1 decide the collection scope, and G1–G3 decide which parts of the cache are kept |
| Disabled collection | Zero added allocation |
| Retention | Element and character counts of completion parts, module projections and the cache index are recorded |

- **Completion measurements** extend the shared `HoverWorkloads` with wide scopes, deep receiver chains, many overloads, `::Kimi.` and all targets, in a new `Benchmark --completion` mode with fixed conditions in HOVER.md.
- **Module projection measurements** use new `ModuleProjectionWorkloads` in a new `Benchmark --module-projections` mode with fixed conditions in `src/Benchmark/ModuleProjections.md` (M5).
- Both modes get rows in the VERIFICATION measurement table. Allocation and retention use `Purpose=Allocation` tests.

## 6. Implementation plan

### 6.1. Order

```text
I0 ─┬─► S1 ─► S2 ─► S3 ─► S4 ─► S5 ──────────────────┐
    ├─► M1 ─► M2 ─► M3 ─► M4 ─► M5 (G0) ─────────────┴─► M6 ─► M7 ─► M8 ─► M9 ─► M10 ─► M11 (API freeze, G1) ─┐
    └─► C1 ─► C2 ─────────────────────────────────────────────────────────────────────────────────────────────┴─► C3 ─► C4 ─► C5 ─► C6

After G1, the first matching branch:
  STOP       ─► G4
  OPTIMIZE   ─► builder-optimization unit ─► G1 again
  KIMI-ONLY  ─► M12 ─► … ─► M17 (G2) ─► G4
  FULL       ─► M12 ─► … ─► M17 (G2) ─► M18 ─► M19 (G3) ─► G4   (M18 only if G2 kept the cache; otherwise G4)
G4 ─► M20, only if it pays off
```

- **I0** is the documentation-only spec intake of §8; the Entry gate (§6.4) waits for it.
- M4 also needs S1 (`EditorLimits`), and M6 needs S5 (one collection request and `EditorProjection`).
- Completion ships on uncached module projections after the API freeze at M11. The cache units never change the API that completion consumes.

### 6.2. Rules for every unit

- **Shape.** A unit is a reproducer, an implementation and focused tests. It is verified, committed with a descriptive message and pushed.
- **Clean worktree.** Other sessions leave uncommitted edits in the shared tree. Commit with explicit paths, check the commit out detached in `temp/vwt` (toolchain junction, `dotnet restore`), verify there, run `verify-commit.ps1` on the evidence, and push only after both pass.
- **Unit verification.** `pwsh -NoProfile -Command "./scripts/verify.ps1 -Class <owning classes> -Name <unit>"`. A selection with zero methods fails, so `-TestPurpose Allocation` is used only for classes that have allocation tests. From M12 on, every unit and gate revert that changes `src/Kimi/Checking`, `src/Kimi/Lsp` or `Binding.ModuleProjection.cs` also passes `ModuleProjectionTest` and the completion classes present at its commit (`CompletionProjectionTest`, `CompletionQueryTest`, `LspCompletionTest`).
- **Session verification.** Once, at the end of the session. A unit that changes `tests/Workloads` or `src/Benchmark`, moves work onto a measured path or decides a gate also runs a Session in `temp/vwt` at its commit before completion and before measuring, and reruns the affected measurement: S1, S2, S3, S4 (`--hover`), S5, M5, M11, M12, M17, M19, M20, C3 (`ParseBenchmark` A/B), C6 and every inserted builder-optimization unit.
- **Measurements** run from the `temp/vwt` build at the unit's commit, with its compiler id recorded; evidence goes to `artifacts/benchmarks/`. Timing is never asserted in tests.
- **Not needed.** No native fixtures or milestones (no code generation change). No Diagnostic Development Workflow. No NativeAOT.
- **Tests** follow the test-writing rules:
  - Add cases to the class that owns the feature, preferring `[InlineData]` rows.
  - Create a class only for a new feature, named after the feature and never after a unit ID.
  - Reuse shared helpers; move a helper to a shared file instead of copying it.
  - Assert each rule at the lowest layer that observes it; add no duplicate cases.
  - Comments state only the rule and its SPEC section; until I0 lands, they cite only existing sections.
  - Zero-allocation checks use `AllocationMeasurement.Measure`.
  - Expected values are written from the rules, never generated from the implementation's output.
- **Documentation.** Each unit updates CODEMAP in the same commit when it adds or moves an entry point. Gate outcomes go to PLAN (after I0) and PLAN_HISTORY; every stop or revert adds a SETTLED entry with its numbers.
- **VS Code.** Completion needs no extension change; `vscode-languageclient` registers the provider from the capability. Protocol tests are the automated evidence. Manual display inspection belongs to the user.

### 6.3. Track S: shared editor analysis

| Unit | Change | Done when |
| --- | --- | --- |
| S1 Limits | Rename `HoverLimits` and `HoverBudget` to `EditorLimits` and `EditorBudget`; mechanical | Hover tests, `HoverWorkloads` and `HoverMeasurements` change only by the rename and pass, allocation tests included. HOVER.md's policy-location sentence and CODEMAP's `HoverLimits` link name `EditorLimits` |
| S2 Edit map | `EditMap` replaces `HoverState`'s edit list; the limit counts regions | `HoverStateTest`: a randomized differential test against the per-step algorithm on Hover targets; the bound test fills 256 disjoint regions; continuous typing keeps one region. Benchmark `--hover`'s history loop, HOVER.md's limit row and its fixed condition "history uses all 256 admitted edits" (now 256 disjoint regions) are updated |
| S3 Shared set | `EditorAnalysisSet` extracted from `RefreshHover` and `HoverState`; `OpenDocument.Hover` becomes the set | `LspHoverAdoptionTest` and `LspHoverTest` pass unchanged. `HoverStateTest`, `HoverImprovementsTest`, `VirtualHoverTest` and `HoverMeasurements` move their `HoverState` construction and `Edited` and `Revalidated` calls to `EditorAnalysisSet`, and `HoverWorkloads` follows any `HoverParticipant` change, with unchanged expected values. CODEMAP's "Hover adoption and reuse" row updated |
| S4 Lazy refresh | Plain input events stop running the full re-evaluation; it runs on worker results and before a request | `LspHoverAdoptionTest`: transitions unchanged, including edits followed by a request. An allocation test: an edit event allocates nothing beyond the edit-map update |
| S5 Editor projection | `EditorProjection` with per-part faults and one collection request; a set whose Hover part is missing is adopted | `HoverCollectionTest`: collection never changes diagnostics, outcome or TestPresence. `LspHoverAdoptionTest`: new rows for a missing Hover part (Hover `null` at once) and a missing completion part |

### 6.4. Track M: module projections and the module projection cache

Track M has four phases. Each later phase starts only if a measured gate says it pays off. Appendix A gives each unit's changes and oracle.

**Phase 1 — uncached projections (M1–M11).** Prove the oracles, build pure per-module projections, publish them in the check output, and freeze the API that completion consumes.

**Phase 2 — Kimi-only cache (M12–M17).** Key only the Kimi library, add the session index and the hit path, prove transparency over random edit sequences, and measure.

**Phase 3 — user-module keys (M18–M19).** Attribute inputs to modules and key the root and dependency modules.

**Phase 4 — optional Hover sharing (M20).** Hover takes the headers of non-root declarations from the module projections.

| Unit | Size | Change | Done when |
| --- | --- | --- | --- |
| M1 Projection oracles | S | Shared `ProjectionGraph`: structural equality, detachment | Both oracles are proven sensitive on today's Hover snapshot |
| M2 Declaration walk | M | `Members`-order walk per module; canonical module identity; walk-only entry | Ordinals, identities, determinism, detachment and immutability hold |
| M3 Purity probes | S | Probe theories only | Dependency and Kimi tables ignore their consumer; an environment change affects exactly its modules |
| M4 Detail lines | M | Hover-display details; per-module budget and failure record | Details match a handwritten table and Hover's header lines; failures stay local |
| M5 Workloads and baseline | M | `ModuleProjectionWorkloads`, `--module-projections`, storage bound | Baseline recorded; gate G0 decided |
| M6 Publication | M | The module part of `EditorProjection`, behind its own isolation | Collection never changes the check or Hover; zero allocation when disabled |
| M7 Type terms | M | Detached terms with (module, ordinal) references; `Locate` and `Term` | Terms agree with Binding's types across modules |
| M8 Module tree | S | Own references, default aliases, Kimi, test-only edges | The tree equals the compilation's references |
| M9 Member tables | M | Own members, sorted; base terms; unique index result | A test-only audit against declaration scopes finds no difference and catches seeded ones |
| M10 Access descriptors | M | Descriptors and `Covers` | Equal to the compiler's accessibility except the stated test-only deviation |
| M11 Member query and API freeze | M | Cross-module inherited query; consumer-shaped test | Matches the compiler's member lookup except the protected-receiver deviation; zero allocation; gate G1 decided |
| M12 Real-check driver | M | `LspCheckDriver` for tests and Benchmark | Existing scheduler rows pass through it; real checks adopt every required unit |
| M13 Manifest and Kimi key | M | Manifest, structural keys (Kimi only), the source seam, the directive guard | Keys compare structurally; the guard is proven sensitive |
| M14 Session index | M | Immutable index replaced only at the end of a check | It holds exactly the keyed projections that current results reference |
| M15 Hit path | M | Reuse on a key hit, with the consistency walk | Hits equal fresh projections; a mismatch is a counted miss |
| M16 Lockstep differential | M | Two sessions, cache on and off, over seeded edit sequences | Equal editor projections and zero mismatches for every seed |
| M17 Cache measurement | M | `--module-projections cache` session scenario | Gate G2 decided |
| M18 Input attribution | M | Set-valued attribution; the uncacheable rule | Every recorded input is attributed; uncacheable reasons are exact |
| M19 User-module keys | M | Keys for root and dependency modules | Keys change exactly with their inputs; gate G3 decided |
| M20 Hover header sharing | M | Hover reads non-root headers from module projections | Hover output unchanged; gate G4 keeps or reverts it |

**Gates.** Track M measures the three-unit set: Product on `x86_64-pc-windows-msvc` and `x86_64-unknown-linux-gnu`, and Test on `x86_64-pc-windows-msvc`. Per check, C is the median check time, B the Bind time, P the projection time (`P_kimi`, `P_dep`, `P_root`, and their sum `P_all`) and W the walk time of non-root modules. D is the retained bytes of module projections duplicated among units of one workspace with equal identity, role, mode and environment; copies on different targets are not duplicates. F is the share of the median Hover snapshot time (`CreateHoverSnapshot`) spent on headers of non-root declarations, and R the retained bytes that storing those headers in module projections would add.

| Gate | After | Decision |
| --- | --- | --- |
| Entry | before M1 | Start once I0 lifts the deferral and PLAN has a track-M row |
| Purity | M4 | A field that varies with the consumer moves to per-unit data in an inserted unit; a missing environment component joins the key definition before M13. M5 starts only when every probe row passes |
| G0 | M5 | If `P_all` ≤ 3% of C on the Hover value program and the diamond workspace, every unit collects module projections. Otherwise insert one builder-optimization unit before M6, measure again, and record that only producers collect |
| API freeze | M11 | `ModuleApiConsumerShape` passes; from here completion may ship on uncached projections. Any later change to the frozen API keeps it and the completion classes green in the same commit. C4 and C5 confirm that completion code references no cache type |
| G1 | M11 | The first matching branch wins: STOP (skip M12–M19, add a SETTLED entry) if `P_all` ≤ 3% of C on every workload and D ≤ 1 MB; OPTIMIZE if `P_root` + W > 3% of C on some workload (insert one builder-optimization unit and repeat G1 once; if it still holds, record the §5.3 miss with its cause and evaluate the remaining branches); KIMI-ONLY (M12–M17) if Kimi accounts for ≥ 80% of `P_all` − `P_root` and of D; otherwise FULL (M12–M19). In every branch, re-apply the G0 scope rule to the full projection and record the scope that C5 switches on |
| Differential | M16, and again at M19 | M17 starts only when every seed passes, and G3 only when they pass again with user-module keys. A failure is fixed in the key rule (from M18, also in the attribution rule) and becomes a deterministic row; the oracle is never relaxed |
| G2 | M17 | Keep M13–M15 only if check time and allocation stay within tolerance (cache-on median ≤ cache-off median + max(1%, half the cache-off interquartile range)), the saved projection time is ≥ 50% of G1's `P_kimi` or retained bytes fall by ≥ 25%, the Kimi hit ratio is ≥ 90%, and the index update p95 is ≤ 0.5 ms. Otherwise revert M13–M17, keeping the directive guard and the driver. M18 starts only if G1 chose FULL and G2 kept the cache |
| G3 | M19 | Rerun the M17 scenario with user-module keys at M19's commit. Keep M18–M19 only if, once dependencies are unchanged, module projection time is ≤ 3% of C or ≥ 50% below G2's Kimi-only result; the hit ratio of unchanged non-root modules from the third check on is ≥ 90%; retained bytes do not exceed the cache-off run; and the G2 tolerance holds. Otherwise revert them |
| G4 | G3; G2 under KIMI-ONLY or after a G2 revert; G1 under STOP (using M5's F and R) | Start M20 only if F ≥ 10% and R ≤ 10% of module projection bytes. Keep it only if the median `CreateHoverSnapshot` time on `HoverWorkloads` and the diamond workspace falls by ≥ 3% in matched runs, allocation does not rise and every Hover suite is unchanged |

### 6.5. Track C: completion

| Unit | Change | Done when |
| --- | --- | --- |
| C1 Lexical functions | Move the Tokenizer's static helpers; add `MeasureWord`, `MeasurePunctuation` and string step functions; add the cursor-line scanner; add `TextDocument` line accessors | Parity: on the existing lexical corpora, the scanner's token boundaries and kinds equal the Tokenizer's (`LexicalBoundaryTest`, `InterpolationScanRecoveryTest`). `LspDocumentTest`: line copy across the gap. The Tokenizer's hot switch is unchanged, so no `ParseBenchmark` run is needed |
| C2 Keyword classes | Class column in `TokenHelper`, and the completion-only list of contextual words lexed as identifiers, kept out of the keyword prefilter | `KeywordClassificationTest`: every SPEC §2.5.1 spelling and listed contextual word has exactly one class; tokenization is unchanged |
| C3 Completion part | Extract from `RequirementMember` a side-effect-free contract-gathering function (no `requirementGroups` write, no `Fail`, every arithmetic witness instead of the name match), called by both. `Binding.CodeCompletion.cs`: scopes with owner references, bindings and details, requirement members of type-parameter, Contract and primitive receivers, per-document aliases, multi-line token spans and `#if` regions. The producer rule as a `UnitPlan` field set in `WorkspaceCheck.Discover`. The Tokenizer hook adds one null check per string and block-comment token, so C3 records a pinned, interleaved `ParseBenchmark` A/B with collection off. As M6 does for modules, the LSP keeps completion-part collection off until C5; `LspCheckDriver` turns it on for the M15 and M16 rows | `CompletionProjectionTest` (new feature class): scopes, visibility starts (a pattern binding is visible in its arm body), shadowing, contextual bindings, function boundaries, `Self.`, private and protected rows resolved through owner references, detachment, and an audit of the gathered contract sets against `RequirementMember` per (type, scope); existing contract-call tests pass unchanged. `CheckSchedulerTest`: the plans' completion scopes for a multi-target project with TestSources whose `Targets` list is not in ordinal order (the producer equals the representative), and for two projects sharing a document. `HoverCollectionTest`: isolation, zero cost when disabled, and a `ProjectionGraph`-equal Hover part with the completion part on and off |
| C4 Query | `CodeCompletionContext` and `CodeCompletionQuery` over C1, C3 and the frozen module API | `CompletionQueryTest` (new feature class), table-driven: current text and edit sequence → expected p, t, scope and candidates, covering §4.2–4.4 and §4.7. Allocation: a steady-state request allocates nothing but the response value |
| C5 Protocol | Capability, method wiring, tolerant client capabilities, response writer, limits and prefilter; completion-part collection is switched on, and module collection with the scope recorded at G0 and G1 | `LspProtocolTest`: the capability count becomes 4. `LspCompletionTest` (new feature class) through `LspTestClient`: empty list, `itemDefaults` present and absent, insert and replace, kind fallback, `isIncomplete`, cancellation, shutdown, no check scheduled by a request, and, for a project with two targets and TestSources, module projections in exactly the units the recorded scope selects. The harness of `LspHoverAdoptionTest` moves to a shared file instead of being copied |
| C6 Measurement | `--completion` mode, workloads, HOVER.md conditions, the VERIFICATION table row | The §5.3 targets are met; each miss is recorded with its cause, fixed and measured again before C6 completes. Once C5's protocol tests and C6's measurements pass, the I0 STATUS entry is replaced by the supported boundary and the root README's VS Code section is updated; the generated extension README is never edited. Final Session |

### 6.6. Open decisions

The plan proceeds with the default unless the user chooses otherwise.

| Decision | Default | Alternative |
| --- | --- | --- |
| When track M starts | After I0 | Start S1 and M1–M5 earlier (tests, internal builders and measurements only), recording outcomes only in PLAN_HISTORY |
| Keys for first reads | Computed on the worker, so a module first read in a check is cached from the next adoption; hits start two checks later | Re-key when the check ends, from the adopted inputs, one check earlier (decide before M14) |
| Kimi library directives | A guard test; PLAN records the SPEC §18.4.3 gap | Close the gap now by giving the library its own built-in variable set, which makes its key sound by construction |
| Role in the key | Kept | Drop it if the M3 role row, rerun after M11 over details, terms and member tables, shows equal content, so one entry serves both roles |
| Gate thresholds | As in §6.4 | Adjust after G0 with recorded evidence |
| Limit failures | Per-entry detail loss; module failure only on the module budget | A whole-module failure on any limit exception |
| Positions and documentation in module projections | Excluded | Add them later for navigation, derived from Koto spans |
| Consistency walk on hits | Kept in production as defense in depth | Test-only |
| Reuse within one check | Deferred; decided from D (G1) and G2's retained bytes | A check-local memo shared by the Product and Test units of one target |
| Test-only access to private Binding members | A nested internal audit entry used only by tests | Internal inspection wrappers (Principle 4) |
| SPEC §23.4.6 | Unchanged | Mention derived projections reused across checks at I0 |

## 7. Evaluation

### 7.1. Kimigayo Principles

| Principle | How this proposal follows it |
| --- | --- |
| 1. One Concept, One Canonical Form | One adopted set, edit map, retention rule, collection request and limit table for every reader. The representative unit is the existing canonical order. One declaration display (Hover's). One module key, derived from ModuleInputId. One uncacheable rule. One range rule and one `isIncomplete` rule. Keyword spellings come from SPEC §2.5.1 only. |
| 2. Local Reasoning | A candidate depends only on the representative unit, the mapped old position and the cursor line. Each step is a closed table with a bound. A module projection depends only on its key. |
| 3. Explicit Semantics | The guarantee (§4.1), the access deviations (§4.4.2) and every imprecision (§7.2) are stated. No implicit receiver, automatic qualification, imports or snippets. |
| 4. Compiler Server Protocol | Projections are immutable and detached. Each unit has a deterministic oracle; cached and fresh projections are compared structurally; each part of the cache is kept only on measured evidence under fixed conditions. |

### 7.2. Accepted imprecision

| Imprecision | Effect | Recovery |
| --- | --- | --- |
| Only the representative unit is read | Names that exist only in other targets or in the Test unit are missing; a name may be valid in one unit only | Next check's diagnostics; the `target` setting |
| A Blocked or Faulted participant removes the whole set | No semantic candidates, as for Hover | Change the shared rule for both readers, if ever |
| Names in new lines and bindings of new headers are unknown | A just-written local appears after the next adoption | Next adoption |
| Broken old scope boundaries are not detected | Names of an old scope appear after its header is deleted | Next adoption |
| New lines start in code | Candidates appear inside a multi-line comment or string opened above | None needed; insertion is the user's choice |
| Type and expression positions are not distinguished | Value names appear in type positions | Client filtering |
| Calls and indexes in receiver chains need a unique declaration or index result, and some receivers are not resolved | Overloaded calls, generic methods, and associated-type, composite and `base.` receivers give an empty list | A later extension |
| Bindings lost by syntax recovery are missing | Fewer candidates in a function with an incomplete statement | Next adoption |
| Type refinement, protected receivers and test-only access are not checked | Members of refined types are missing; inaccessible members can appear | Diagnostics |
| Detail shows the declaration, not the receiver's instantiation | `Option<T>` instead of `Option<Entry>` | Hover |
| An open list is not refreshed | The old list stays until the next request | Next request |

### 7.3. Rejected alternatives

| Alternative | Reason |
| --- | --- |
| Return only candidates on which all participants agree | Cost grows with the number of units; completion only inserts text, and the next check reports disagreement |
| Completion-specific adoption or retention (for example keeping a set after Faulted) | Two state machines for one concept; Hover and completion would disagree (Principle 1) |
| Request-time parsing, binding or check scheduling | Latency, and it bypasses the quiet period; same reason as the settled Hover decision |
| Per-edit lexical convergence tracking | More cost and machinery; the cursor line plus the completion part's multi-line spans suffice |
| Server-side prefix filtering, case-sensitive matching | Conflicts with client fuzzy matching and causes a request per keystroke |
| Automatic qualification such as `self.count` | Contradicts the explicit receiver rule (SPEC §9.4) and adds rules |
| A completion store with LRU or reference counts | Garbage collection and the per-unit limits (§4.6) already bound memory; an evicted part cannot be rebuilt without a new check, so eviction would leave a gap |
| A per-item "cached analysis" marker | Completion runs right after edits, so the marker would always be present |
| A per-request result cache | Requests arrive only at word starts and after `.` |
| Storing every expression's type by position | Large; useless for newly typed expressions |
| Keyword classes as a column of SPEC §2.5.1 | §2.5.1 defines lexical classes, and the extension's grammar test parses its table; a coverage test guards the separate table instead |
| A completion part in every unit | Duplicates per target and per Test unit; the project-local producer rule is enough |
| Positional declaration locators | Synthesized declarations and implicit constructors share spans; `Members`-order ordinals are unique and deterministic |
| Cross-revision declaration correspondence (SPEC §18.7.2 DeclarationKey) | Not needed: the cache reuses whole modules with identical inputs (SPEC §18.7.3) |
| Renumbering Hover after a new shared section | Churn in about seven files; the shared section is added after Hover instead |

## 8. Specification intake

Intake is unit I0 (§6.1): one documentation-only commit that contains all of the following:

- **SPEC §23.4.1:** add `completionProvider`, and also the missing `hoverProvider`, to the capability list; fix the example in §23.4.10 likewise.
- **SPEC §23.1:** the Sender row notes that the state owner serializes completion responses itself (§5.2) and still never waits for output.
- **New `lsp-editor-analysis.md` as §23.4.12 "Editor analysis":** §2, §3.1, §3.2 and §3.4. §3.3 (the module projection cache) is not normative: readers cannot observe it, and gates G1–G3 may skip or revert it.
- **Hover:** §23.4.11.4–6 refer to §23.4.12 for participants, adoption, retention, the edit map, opt-in collection, coverage, detachment, per-part isolation and the edit-region bound, and keep the Hover-specific rules: agreement; the previous-analysis notice; `null` when no set exists, participants disagree on the Hover target or its range cannot be mapped; the per-target touch rule and the check that a mapped range's text equals the previous text; after a switch, no fallback from a missing or different current Hover target; and no request-time lexing or semantic analysis for Hover. The edit-history limit counts regions, and a set with a missing Hover part is adopted and answers `null`. Hover is not renumbered.
- **New `lsp-completion.md` as §23.4.13:** §4 of this proposal.
- **Integrated text:** it refers to no part of §5–§8, unit or gate, and names no code type or test; the §2.4 sketch stays here. Like `lsp-hover.md`, it names each bounded resource and its outcome without numbers. HOVER.md's limits table, the single policy location for `EditorLimits`, receives the values of §2.6, §3.2 and §4.6 in the units that enforce them.
- **Chapter 23:** update the §23.4.11 summary paragraph, add paragraphs for §23.4.12 and §23.4.13, and add "Adopted analysis set, representative unit" and "Module projection" to the §23.2 term table.
- **Appendix D:** the row becomes "Language-server features beyond diagnostics, quick-fix code actions, Hover and completion: navigation, formatting, pull diagnostics, semantic tokens", and its second column also names Hover (§23.4.11) and completion (§23.4.13).
- **Appendix E:** add "code completion", "adopted analysis set", "representative unit" and "module projection", distinguished from the control-flow term Completion (§14.1).
- **Navigation and status:** SPEC.md links to `lsp-editor-analysis.md` and `lsp-completion.md`; PLAN gets one short entry per track; STATUS gets one entry, "Editor analysis and code completion (SPEC §23.4.12–13): specified, not implemented", naming the open gaps (no `completionProvider`; Hover counts edits rather than regions; a set without a Hover part is not adopted). S2 and S5 remove their gaps from that entry.
- **SETTLED.md:** the main rejected alternatives of §7.3; the Hover entry cites §23.4.12 for the shared adoption and refresh rules.
- **`draft/INTEGRATED.md`:** map the integrated sections to their targets and mark the proposal 一部取り込み, freezing only that scope. The open items are §3.3, §5, §6 (including the §6.6 decisions and the gate thresholds) and Appendix A. Close the proposal and move it to `draft/Changes` when the last track-M gate is recorded and every §6.6 decision has a disposition.

Semantic Tokens (2026-10-02, undecided) is independent of this proposal. If it is adopted, whether it can read the adopted analysis set is decided separately.

## Appendix A. Track M units

Every unit follows §6.2. "Owners" are the test classes passed to `-Class`. New feature classes: `ModuleProjectionTest` and `ModuleProjectionCacheTest`.

### Phase 1: uncached projections

**M1 Projection oracles (S).** Owners: `HoverProjectionTest`.

- Change: new `tests/xUnitTest/ProjectionGraph.cs`.
  - `AssertEqual` reflects over all instance fields. It compares arrays element by element and dictionaries as key-ordered pairs (never their internals), memoizes reference pairs, and names the first differing member path.
  - `AssertDetached` walks reachable objects and fails on a forbidden compiler type, reporting its path. The default forbidden set: Koto, BindingSymbol, BindingScope, BoundType, SourceDocument, Compilation, Kotonoha, Binding, Project, DependencyNode, InputState.
  - VERIFICATION's shared-helper list gains `ProjectionGraph`.
- Oracle: a real Hover snapshot reaches no compiler object except SourceDocument; forbidding SourceDocument makes `AssertDetached` fail through `Documents`; two compilations of one program are equal; a one-character edit makes `AssertEqual` fail with a member path.

**M2 Declaration walk and module identity (M).** Owners: `ModuleProjectionTest` (new), `ModuleBindingTest`.

- Change: `Checking/ModuleProjection.cs` (arrays only): `ModuleProjection`, `DeclarationEntry` (kind, name, parent ordinal, own arity, flags), `DeclarationRef(ModuleRef, Ordinal)`, `UnitModules` (root first, dependencies in module order, Kimi last).
  - `ModuleRef` wraps a SourceIdentity and compares through `SourceIdentity.PathComparer`. `Kotonoha.Name`, `Kotonoha.Id` and unit indexes are never stored.
  - The builder in `Binding.ModuleProjection.cs` keeps only Kotos that are their symbol's own declaration, and drops `$` names, generated functions and per-document aliases.
  - It also offers a walk-only entry, used by the hit path and the measurements.
  - `ProjectionGraph.AssertImmutable`: every reachable field is readonly or init-only, with no `List`, `Dictionary`, `HashSet`, `Lazy<T>` or `StringBuilder` fields; arrays are allowed and never written after construction.
  - `ModuleBindingTest.Create` gains optional target, debug, test-build and collection parameters; existing callers are unchanged.
  - CODEMAP gains a "Module projections" row.
- Oracle: handwritten ordinal tables (merged groups, the implicit constructor after its struct's members, overload order, a struct split across documents, enum Cases, absent `$` names and generated functions, a Product build without `#Test` functions); canonical identities (implicit projects sharing a directory stay distinct; case-only path differences follow `PathComparer`); determinism; detachment with a collected `WeakReference<Compilation>`; immutability with negative rows; the walk-only entry yields the same ordinals.

**M3 Purity probes (S).** Owners: `ModuleProjectionTest`.

- Change: probe theories only, with a second target (`x86_64-unknown-linux-gnu`).
- Oracle:
  - A dependency's table is unchanged when only its consumer varies: unused or heavily used, a same-named root setting, a root syntax error, a test build.
  - Kimi's table is unchanged across user programs.
  - Varying debug, target, a dependency's own setting, a root setting or the test build changes exactly the modules that read it.
  - Root and dependency roles project equal content apart from role and identity; this is recorded as evidence, and the role stays in the key.
- If a row fails, M3 stops; the row becomes the reproducer of an inserted fix unit, and the commit message records the fix.

**M4 Detail lines and per-module budget (M).** Owners: `ModuleProjectionTest`, `ModuleBindingTest`. Requires S1.

- Change: `DeclarationEntry.Detail` by §3.4, using `HoverHeader` and `HoverTypeName` (memoized per type within the module).
  - New `Checking/EditorDisplay.cs` provides truncation, the kind word and the value detail; an internal `Binding.ValueDetail` serves in-compilation callers.
  - The module budget comes from `EditorLimits`: module elements, module characters and detail length.
  - The builder never reads Hover failure flags, anchors, Details, Identity or Owner, attribute binding state or the native-import table.
- Oracle:
  - A handwritten detail table: attributed function and struct, a generic function with constraints, a Contract, declared, inferred and unverified Properties, kind words, a 130-unit header cut to 127 units plus `…`, a surrogate pair at the cut, and a header over the output limit (no detail, module not failed).
  - Every root declaration's detail is one line of its Hover header, and every verified Property's type text equals Hover's Details type.
  - A tiny Lib budget gives the same failure record under two consumers and leaves the other modules and Hover unchanged.
  - New consumer-purity rows: a consumer conformance, a conflicting library import, a virtual Lib function, and collection on or off.

**M5 Workloads and baseline, gate G0 (M).** Owners: `ModuleProjectionTest`. Needs a Session.

- Change:
  - New `tests/Workloads/ModuleProjectionWorkloads.cs`, compiled into both projects: an empty program, the Hover value program, a diamond workspace (App→Lib→Leaf, App2→Leaf, a merged node, TestSources, TestDependencies, a lock file) and a shared directory.
  - Measurements use the three-unit set (§6.4).
  - `Benchmark --module-projections` reports C, B, `P_kimi`, `P_dep`, `P_root`, W, allocation per projection, retained bytes and D, plus F and R for G4. Its fixed conditions are in `src/Benchmark/ModuleProjections.md`, with a row in the VERIFICATION measurement table.
- Oracle: the shared workloads are deterministic, detached and consumer-independent with no failed module; an allocation-purpose storage-bound test checks element and character counts against the limits and exactly sized arrays. The benchmark JSON's compiler id matches the build.

**M6 Publication in the check output (M).** Owners: `HoverCollectionTest`, `CheckServiceTest`, `ModuleProjectionTest`, `CheckSchedulerTest`, `LspHoverAdoptionTest`. Requires S5.

- Change:
  - The module part of `EditorProjection` is built after Finalize, for Completed checks only, behind its own isolation boundary. A fault-injection factory serves tests.
  - `ModulesTicks` records projection time without allocation.
  - The LSP keeps module collection off until C5.
  - The session logs module faults as it logs Hover faults.
- Oracle:
  - With modules off, the output has no modules. With modules on, root comes first and Kimi last, and the JSON is unchanged.
  - Rows for a budget failure and an injected exception keep Hover, diagnostics, acceptance and TestPresence unchanged.
  - Cancellation and pending input are not swallowed.
  - The Hover snapshot is equal with modules on and off.
  - Disabled collection allocates 0 bytes.
  - Published modules are detached and immutable.

**M7 Type terms and references (M).** Owners: `ModuleProjectionTest`, `HoverCollectionTest`.

- Change:
  - New `Checking/TypeTerm.cs`: a per-module interned term table (§3.1). The modules are built Kimi first, then in reverse topological order.
  - `Locate(BindingSymbol)` returns a `DeclarationRef`, keyed by (declaration, kind, slot), because several symbols can share one declaration. `Term(BoundType)` serves the completion part.
  - Entry term indices: Property types, and function result and parameter terms.
  - Per generic owner, each slot records its constraint Contract terms.
- Oracle: rows for own, dependency, transitive and Kimi types, generic arguments and slots, Contract Self versus a bound Contract reference, reference layers and a fixed-array length; a Property type and a function result term equal `Term` of the declared type and the result type; a binder differential shows that every declaration-site and member-access type resolves to the declarations `Locate` gives; every reference resolves within the unit, except that with Lib as a failure record the root's Lib-typed Property term is Unknown; purity and storage tests now cover terms.

**M8 Module tree (S).** Owners: `ModuleProjectionTest`.

- Change: the tree from the module's own references and default aliases, plus Kimi and a test-only flag; per-document aliases stay out.
- Oracle: rows for root→Lib, Lib→Child, default aliases, a Test root with a TestDependency, and one Lib under two consumers (equal); each tree equals the compilation's references for that module.

**M9 Own member tables (M).** Owners: `ModuleProjectionTest`, `HoverCollectionTest`.

- Change:
  - New `Checking/ModuleMemberTable.cs`: entries sorted by name in UTF-16 code-unit order and found by binary search. Each table records its base terms in the derived type's parameters and its unique index result.
  - Kimi's receiver tables contain only the fixed-array members group, the one built-in group the compiler's member lookup reads. The integer Position group is an ordinary group.
  - A nested internal `Binding.AuditModuleProjections`, used only by tests, compares tables with the declaration scopes.
- Oracle: the audit finds no difference on real projections and exactly one difference in each seeded copy (a removed member, a changed premise, overload count or base term); rows for overload count and detail, conditional members, enum Cases, bases with arguments, unique index results and fixed arrays; an allocation-purpose lookup test.

**M10 Access descriptors (M).** Owners: `ModuleProjectionTest`.

- Change:
  - New `Checking/ModuleAccess.cs`. A descriptor is the chain of access modifiers and owner ordinals up to the module root, plus test flags.
  - `Covers(descriptor, use site)` evaluates public, lexical containment, root-level private, internal and the protected forms through the projected base graph, without the receiver check.
  - `Covers` implements §4.4.2's access rule.
- Oracle: for every (declaration, container scope) pair, `Covers` equals the compiler's declaration-only accessibility, except pairs whose declaration is test-only, a test marker or the test temp directory; a seeded modifier change is reported.

**M11 Member query and API freeze, gate G1 (M).** Owners: `ModuleProjectionTest`, `HoverCollectionTest`. Needs a Session.

- Change:
  - New `Checking/ModuleMemberQuery.cs`. It strips layers, then visits the own table and the bases nearest first, substituting base arguments through (term, environment) frames on a caller-supplied fixed-depth span. An accessible own member ends the walk, inaccessible ones fall through, and a cycle guard stops cycles.
  - Struct, enum, group-qualified and fixed-array receivers are handled; other receivers return NotHandled.
  - The frozen API: the module part of `EditorProjection` and its fault; `UnitModules.Resolve`; `ModuleProjection` `Declarations`, `Terms`, `Tree`, `MemberTable(ordinal)` and `Failure`; `DeclarationRef`; `ModuleRef`; `ModuleMemberQuery`; `ModuleAccess.Covers`; `EditorDisplay`; and, inside CheckService, `Locate`, `Term` and `ValueDetail`.
  - The consumer-shaped test `ModuleApiConsumerShape` calls every frozen member the way completion will.
  - The M9 audit gains a comparison with `LookupTypeMember`, called only after any Hover snapshot of the same compilation, because it interns member paths.
- Oracle:
  - For every (receiver, name, use scope) triple, the declaring reference and substituted member term equal the compiler's member lookup. The rows cover a base in another module, a generic base, shadowing, a skipped private base member, a three-module chain, a fixed array, protected access through `self`, an integer and a type parameter (both NotHandled; their members come from the completion part, §4.4.2), and a rejected two-base source.
  - The only exceptions are the protected-receiver deviation pairs listed in the rows.
  - A seeded base-term change is reported.
  - A receiver whose type or base lies in a failed module yields NotHandled without an exception.
  - A steady-state query allocates nothing.

### Phase 2: Kimi-only cache

**M12 Real-check driver (M).** Owners: `CheckSchedulerTest`. Needs a Session.

- Change:
  - New `tests/Workloads/LspCheckDriver.cs`. It creates a session with module collection on, runs real `WorkspaceCheck`s on a worker thread, and pumps their products in order on the caller thread. It also has an optional blocking hook around the real runner and helpers for open, change, close and watched files.
  - Internal accessors expose the default runner and the captured check start.
  - `SchedulerHarness` keeps its API but delegates its pump loop to the driver.
  - The driver has no xUnit dependency: it takes a `CancellationToken`, throws on a `CheckDone` failure, builds URIs with `SourceIdentity.ToUri`, and is linked explicitly in `xUnitTest.csproj` and `Benchmark.csproj`.
- Oracle: every existing scheduler row passes; a real check over the diamond workspace with all targets adopts exactly the expected unit keys, all Completed, with root-first and Kimi-last modules; a blocked real check runs the action exactly once and still completes.

**M13 Manifest, structural keys and the Kimi key (M).** Owners: `ModuleProjectionCacheTest` (new), `CoreCatalogTest`, `CheckSchedulerTest`, `HoverCollectionTest`.

- Change:
  - `Checking/ModuleManifest.cs` is built after Finalize, before any projection.
  - `Checking/ModuleProjectionSource.cs` (`Begin`, `TryGet`) is reached through an internal `CheckInputSource.ModuleSource`.
  - `Lsp/ModuleKey.cs` compares structurally; its hash is only an index.
  - `Lsp/CachedModuleProjections.cs` is created per unit by `VisitUnit`, computes keys dependencies first, and counts hits, misses and mismatches onto the unit result. Only Kimi gets a key in this unit.
  - The directive guard in `CoreCatalogTest` tokenizes the embedded library texts and uses a parser predicate shared with the parser.
- Oracle: changing exactly one key component makes keys unequal, and equal content gives equal keys and hashes; `Begin` runs once before the first `TryGet`; after a real check only Kimi is keyed; a fake runner gives no keys; the guard flags `#if`, `#switch` and an orphan `#case` but not a `#Test` attribute.

**M14 Session index (M).** Owners: `ModuleProjectionCacheTest`, `CheckSchedulerTest`, `LspHoverAdoptionTest`.

- Change:
  - New `Lsp/ModuleProjectionIndex.cs`: an immutable map from key to projection. `Next(current results)` keeps the keyed, non-failed projections those results reference, and the first equal key wins.
  - `OnCheckDone` replaces the field; nothing else mutates it. `Tick` captures it into the check start, and Dispose sets it to null.
  - An internal switch can turn the cache off for the differential and the benchmark.
- Oracle: adopt, replace and retire sequences give the expected key sets, and earlier snapshots stay unchanged; after a close, the next check prunes retired entries; while a check is blocked, unit and retire events leave the index reference unchanged and Dispose nulls it without a worker failure; an allocation-purpose test records index retention.

**M15 Hit path (M).** Owners: `ModuleProjectionCacheTest`, `HoverCollectionTest`, `ModuleProjectionTest`.

- Change:
  - On a hit, CheckService runs the walk-only entry, compares (kind, name, parent) per ordinal, and reuses the instance when they are equal. Otherwise it rebuilds, counts a mismatch and adds a fault note.
- Oracle: with a stub source, a root edit reuses Kimi (`Assert.Same`) while root and Lib are rebuilt, a debug change reuses nothing, and a Product→Test switch reuses Kimi; each whole editor projection, including the completion part once C3 has landed, is `ProjectionGraph`-equal to a fresh run, with rebuilt terms resolving into reused modules; a seeded mismatch is counted and rejected; in a real session the second check reuses the first generation's Kimi with no mismatch.

**M16 Lockstep differential (M).** Owners: `ModuleProjectionCacheTest`.

- Change: one theory over (seed, workspace, all targets, debug), with two driver sessions receiving the same messages, one of them with the cache off. Its seeded edit vocabulary:
  - structural edits;
  - comment-only edits;
  - structure-preserving type, access, base, constraint and `#if` edits;
  - project-setting edits;
  - added and removed files;
  - TestSources toggles;
  - close and reopen;
  - edits to a source shared by two project files.
- Oracle: after every check, both sessions' editor projections (Hover part, completion part when present, and module part) and diagnostics JSON are `ProjectionGraph`-equal, and the cached session counts zero mismatches. The index holds exactly the keys current results reference, and the index is detached.

**M17 Cache measurement, gate G2 (M).** Needs a Session.

- Change: `--module-projections cache` runs the diamond workspace with Product on two targets plus Test, over 20 root edits, with the cache on and off in at least 7 matched, interleaved pairs. It reports projection time, check time, allocation, the Kimi hit ratio from the third check on, retained bytes, and the index update time.
- Oracle: the M16 differential passes on the measured commit; the run aborts on a non-Completed check or a mismatch; the compiler id matches the build.

### Phase 3: user-module keys

**M18 Input attribution (M).** Owners: `ModuleProjectionCacheTest`, `DependencyResolutionTest`, `CheckServiceTest`, `LspInputTest`.

- Change:
  - The dependency partition records merged inputs.
  - The manifest gives each module an input set and gives the unit a unit-level set.
  - New `Lsp/ModuleInputAttribution.cs` classifies each recorded input as belonging to a set of modules, to the unit level, or as unattributed.
  - The uncacheable rule (§3.3) is applied transitively.
- Oracle: the diamond records the merged path; for every workspace row (no dependency, a chain, the diamond, the shared directory, a Test unit, an implicit project, a lock file), each recorded input is attributed, none is unattributed, every manifest input was recorded, and the sets equal the expected ones; `Project.AddSource`, a recorded Mod source and an extra dependency document make exactly the expected modules uncacheable.

**M19 User-module keys, gate G3 (M).** Owners: `ModuleProjectionCacheTest`. Needs a Session.

- Change: keys for root and dependency modules from their attributed (input, revision) pairs, role, mode, environment and dependency keys, with the uncacheable rules of §3.3.
  - A revision lookup on the input source treats revision 0 as no key. The worker may read the unit's input list before or after the state owner commits its revisions. Keys are sound either way: a first read has revision 0 and gives no key, and a committed entry carries the revision of exactly the state the worker read. A code comment states this.
- Oracle:
  - The M16 seeds, including the shared-directory edits, pass with user-module keys on the measured commit.
  - Each mutation changes exactly the expected keys, as {modified module → modules whose keys change}:

    | Mutation | Keys that change |
    | --- | --- |
    | A Lib source | Lib, App |
    | Lib project-file formatting | Lib, App |
    | A new file in Lib's directory | Lib, App |
    | An App source | App |
    | A Leaf source | Leaf, Lib, App |
    | A shared-directory source | Lib, App |
    | Product versus Test | App |
    | Debug or target | All, Kimi included |
    | A formatting-only edit of the merged-away project file | None |

  - Equal keys always mean equal projections.
  - A revision 0, a null dependency key or an unattributed input gives no user key.
  - After a root edit, Lib and Kimi are reused.
  - In a real session, Product and Test on one target share Lib and Kimi but not the root, and another target shares none of them.

### Phase 4: optional Hover sharing

**M20 Hover reads shared headers (M).** Owners: the Hover suites. Needs a Session.

- Change: declaration entries gain the full Hover header string (none when a limit was hit). The Hover builder takes the header text of non-root declarations from the unit's module projections, fresh or cached. Owner, Details, Identity, Origins and Documentation stay per unit, and so do limit failures.
  - In units that collect module projections, the §5.2 build order becomes module projections, then the Hover part, then the completion part. A declaration without a projected header falls back to `HoverHeader`: a unit outside the collection scope, a failed module, or a header limit.
  - M6's "Hover snapshot equal with modules on and off" row and C3's Hover-equality row run under the new order.
- Oracle: Hover responses on `HoverWorkloads` (all configurations) and the diamond workspace are equal with and without sharing; a dependency header over the Hover output limit gives the same Hover fault and `null` Hover as without sharing; two participants that differ only by debug under a dependency `#if` still disagree; when the cache is kept, the M16 seeds pass with Hover output compared; `Benchmark --hover` and `--module-projections` are rerun; gate G4 keeps or reverts the unit.
