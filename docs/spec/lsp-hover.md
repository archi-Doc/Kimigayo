# 23.4.11. Hover

This section is part of [Chapter 23](23-compiler-services.md#234-language-server-protocol). Hover describes compiler-established declarations, contracts and documentation. It does not change language validity, access, ownership, Copy proofs, call selection or documentation association. Explanatory text never adds a language guarantee.

## 23.4.11.1. Targets and positions

Hover serves open, synchronized `file:` documents with the `.kimi` extension. Document synchronization and analysis freshness are separate: a synchronized document can display previous analysis under §23.4.11.5.

| Target at its declaration or resolved reference | Contents |
| --- | --- |
| Type: struct, enum, built-in, Type parameter, associated Type or instantiated Type | Declaration or Type spelling, relevant Constraints, Copy judgment and documentation |
| Contract | Declaration, required contracts, effect bounds and documentation; no Copy field for the Contract itself |
| Property: member `let`, `var`, `computed`, or a Contract Property requirement | Declaration, Type, accessor contracts and documentation |
| Named function, member function, Contract requirement, `init`, `drop`, explicit specialization | Signature, Constraints, effect information and documentation |

Resolved references into dependencies and Kimi use the same rules. Generated declarations expose only established information; no physical file, declaration position or documentation is invented. Dedicated hover for local bindings, ordinary parameters, enum Cases, groups, Origin names and Semantics names is outside this profile, as are arbitrary expression Types, completion, Signature Help, definition navigation and Document Link requests on comments. Being a documentation target alone does not make a declaration a Hover target. Callable call contracts and effects remain included.

Positions use UTF-16 and ranges are half-open. A character position past a line's end is clamped to its end; a nonexistent line or position without a target returns `null`. Malformed requests follow the protocol error rules.

Each semantic target owns a representative token of its own syntax:

| Syntax | Representative token |
| --- | --- |
| Named declaration or reference, including `init` and `drop` | Its name token |
| Instantiated Type `Container<T>` | `Container` for the outer Type; `T` retains its inner target |
| Unnamed Type | `(` for Unit and Tuple, `->` for a Function Type, `[` for a fixed array, `?` for Option |
| Semantics-applied Type `X/T` | `X`, whether a keyword or Semantics parameter; the target is the applied Type |
| Call contract | That invocation's `(`; a direct name or member reference, including a Type-argument application, also carries the call contract on its name |

A child owns its tokens. Grouping parentheses alone add no target. Origin annotations belong to the complete target Type; the Copy field identifies that Type. In `A.f<T>(x)` the call name is `f`, and in `Box<T>.init(x)` it is `init`; qualifiers and Type arguments keep their own targets. The outer calls in `factory()(value)` and `items[index](value)` use their own `(`, never the inner name or index.

Selection uses the unique Binding result. A unique candidate may consist of a common contract and several requirement declarations ([§8.4.6](08-generics-constraints-and-contracts.md#846-calls-and-shared-requirements)); keep both the selected contract and its declaration set. This is distinct from an ambiguous candidate set. Ambiguity, unresolved selection, recovery guesses and conflicting targets left on one token return `null`.

Combine information for the same target and settle child precedence when building the index. Both lookup and response `range` use one representative token, including effect Hover; an entire invocation is not its range. Selection and exclusion belong to the displayed analysis snapshot. Other punctuation, keywords, whitespace and comment bodies in that snapshot have no target. A reference returns its use range, not the declaration's range; previous ranges follow §23.4.11.5.

## 23.4.11.2. Declaration and contract display

Display, in order: the previous-analysis notice when applicable, owner and target kind, declaration, established use-site information, Copy/effects, then documentation. Distinguish the logical declaration, its source fragments and the complete use-site Type. Omit empty fields, but identify deferred or interrupted information.

Generated labels are English; documentation retains its language. Preserve the effect labels `Call:`, `Requirement:`, `Callable:`, `Available bound:`, `Declared by:` and `Premise:`. Other decoration is not a fixed machine interface; structured compiler services use semantic facts rather than parsing the rendering.

- Render a normalized declaration header preserving its contract: modifiers, Type/length arguments, parameter names, receiver, task slot, acquisition Semantics, result mode and Type, Origins, base Types, leading Constraints, conformances including conditional Copy, and associated-Type specifications where applicable.
- Do not expand all Type members, function/accessor bodies or Property initializers. Preserve whether a parameter has a default; its expression can be explicitly omitted under §23.4.11.3.
- A Property shows declared operations and access conditions. `var` alone does not establish writability at a use; any use-permission supplement must be checked.
- Keep the generic declaration unchanged and supplement it with established static arguments or the instantiated call signature. Do not expose unrelated internal names or unestablished arguments. Distinguish inferred result information from written syntax.
- A split struct is one logical declaration including all fragments' bases and Constraints; choose no primary fragment. A unique candidate composed of requirement declarations displays the common contract once and preserves each declaration's provenance.
- An associated-Type reference describes its contract declaration even when `T.Element` normalizes to `i32`; the established Type supplements the display and supplies the Copy query. At a specification's own declaration, describe that specification.
- A Type without source declaration uses its canonical Type spelling and `Built-in type`, without fabricated source information.

Copy uses the existing judgment on the complete Type in the displayed position's Constraint context: the declaration context at a declaration and the use context at a reference.

| Judgment | Field |
| --- | --- |
| Proven | `Copy: Yes` |
| Refuted | `Copy: No` |
| Unknown | `Copy: Unknown`: neither proved nor refuted in this context |
| Error | `Copy: Error`: invalid evidence prevents a judgment |

Unknown and Error are established answers, not missing facts. An inapplicable target has no Copy field; an unresolved Type is not queried. Invalid Constraints, recovery nodes and unchecked calls issue no guarantees. Missing proof never means Non-Copy. Keep a declared `Self is Copy when P` in the header; a substituted condition remains distinct from the contextual judgment, with no separate Hover inference or simplifier.

Effect information follows [§8.4.10.6–7](08-generics-constraints-and-contracts.md#84106-diagnostics-and-recovery). A Contract displays the bounds it declares; a requirement call displays available bounds, every declaring Contract and every contributing premise; a Callable call preserves its contract and all contributing premises. Integrate this information into the target's declaration display. Implementation bodies and specializations never strengthen the public call contract.

## 23.4.11.3. Documentation and links

### Association and provenance

Collection, association and selection follow [§2.3](02-source-and-lexical-structure.md#23-whitespace-and-comments).

| Position | Documentation |
| --- | --- |
| Declaration | Its own selected documentation, regardless of access level |
| Resolved reference | The legitimately accessible declaration's documentation; no blanket public-only filter |
| Ordinary or specialized call | The original declaration that defines the call contract |
| Specialization declaration | Its own `Implementation note`; any original contract description is separate and attributed |
| Type parameter | Its uniquely matching classified parameter item from the parent declaration, with provenance; otherwise omitted |

Do not implicitly copy documentation from an inherited/implementing declaration or another overload. Preserve all fragments of a split declaration and all declarations of a unique merged requirement candidate, parsing and rendering each independently. Order first by stable project identity, then by the existing logical declaration order, never by arrival. Remove duplicate paths to one declaration, not identical text from distinct declarations.

Distinguish absent, explicitly empty and deferred documentation. A SourceDocument with syntax errors defers association for the whole document and shows `Documentation deferred: syntax errors`; do not infer absence or borrow excluded, adjacent or older text to fill a missing current field. Previous analysis uses one coherent previous snapshot (§23.4.11.5).

### Rendering and limits

Render the complete selected documentation by default, not only its summary. Generate Markdown or plaintext from the [Documentation Markdown profile](documentation-markdown.md)'s parsed structure; neither forward raw comments nor repurpose HTML output. Escape unadmitted syntax so the client cannot reinterpret it as images, HTML or reference links. Preserve code as code and encode fences, destinations and labels for their output contexts.

The declaration heading is level 2; a comment heading of level `n` becomes `n + 2`. Above level 6, use a bold paragraph that retains the numeric depth, such as `Heading level 7: …`, rather than flattening levels. Plaintext retains hierarchy with labels, indentation and line breaks, and shows both label and URL for an enabled link. Render independent fragments separately; extract parameter items from parsed structure.

Truncate comments only at completed block boundaries, naming the omitted part and reason, and giving counts only when known. Never cut a link or code block. A parsing depth/work limit publishes no partial parse: retain established declaration information and state why documentation stopped. Mandatory information is indivisible: the previous-analysis notice, declaration contract, Copy result, and effects with all provenance/premises. If it cannot fit, return `null`. A default expression may be explicitly omitted, but the default's existence remains visible.

Log resource-limit and internal-failure reasons for `null`. A documentation-only failure preserves other established information with an explanatory notice. These are not language diagnostics or new documentation lint.

### Link placement

Reuse the profile's URL validation and logical source-path resolution. Editor placement is separate from HTML output placement.

| Destination | Treatment |
| --- | --- |
| Valid `http:`, `https:`, `mailto:` | Enabled link |
| Source-relative path, optionally with query/fragment | Resolve against the declaration's project and logical source name; produce an absolute `file:` URI only where physical placement is known |
| Empty destination, query-only, fragment-only or output-root-relative path | No display-page/output-root mapping in this profile; leave the label |
| Relative path with no source base or known placement, including embedded `compiler://Kimi/...` sources | Leave the label |
| Network-path reference `//host/path`, disallowed scheme or malformed destination | No link; preserve an ordinary link's decorated label, or an autolink's original spelling including angle brackets |

Placement is an immutable partial mapping `(project identity, logical path) → physical URI or unsupported`. Use established layout rules where known and exact mappings for externally placed files. Do not infer neighboring placement from one mapped file or concatenate every logical path with the project root.

Separate path, query and fragment. UTF-8-decode path components exactly once; reject project-root escape, decoded separators/controls and malformed encodings. Encode `#`, `%` and other path data independently from query/fragment, then validate the resulting URI. `file:` is allowed only as the server's mapped source-relative result, not as an arbitrary literal comment destination; `command:` and trusted Markdown execution are not enabled.

Resolution reads no target content, tests no target existence and accesses no network. Preserve the fragment, but an enabled link guarantees neither file existence nor client navigation to a heading. Record actual client behavior in verification. Automatic code-name links, a new `kimi:` scheme and a documentation viewer are outside this profile.

## 23.4.11.4. Shared scheduling and current results

Hover uses the same workspace check as diagnostics, with no separate timer, interval or setting. Scheduling is [§23.4.6](23-compiler-services.md#2346-scheduling-and-consistency); `initializationOptions.checkQuietPeriodMs`, including defaults, validation and session immutability, is [§23.4.9](23-compiler-services.md#2349-watching-and-session-settings).

After input, previous information can be served while the shared quiet period and any running check are awaited. Replace it when valid participating results are ready. The quiet period is a start condition, not a completion deadline. Hover never starts or advances a check.

Reconsider Hover information on result adoption, input revalidation and participant determination/change, independently of diagnostic notification delivery. An unchanged or empty diagnostic payload may suppress publication without suppressing Hover updates. Revalidation that restores valid results needs no recompilation. Keep diagnostic publication conditions unchanged: do not wait for Hover participants or documentation rendering. Diagnostics and Hover need not become visible simultaneously; updated Hover is returned on the next request, without a guarantee to refresh an already displayed popup.

**Participants.** For a document, include every required unit of every project of which it is a member whose source-selection rules check that document, including units without results. Diagnostic `contributors` are insufficient. Exclude consumers that only read it as a dependency and, for example, Product units that do not check a TestSources-only document.

**Adoption.** For each open document, adopt a result set only when membership and the nonempty participant set are established and every participant has valid Completed output with usable Hover information. Never omit an unfinished, held, Blocked or Faulted unit to obtain agreement. Previous information while waiting follows §23.4.11.5. Completed output need not be Accepted: errors elsewhere do not erase this target's established facts. If selection itself is unestablished, return `null`; documentation-only uncertainty is reported as above.

Use the existing input/dependency validity rules. Currently valid results may have different generation times; do not require recompilation of unaffected units. Never fill a current set with invalid historical fields. Version numbers alone do not establish freshness; dependency changes and the existing disk-detection limits also apply.

**Agreement.** Compare the requested target across every participant. Missing targets or differences return `null`. Compare declaration sets and their identities, provenance/content, representative ranges, headers and use-site instantiation; Copy and complete effect evidence; documentation text/ranges, association status, classification facts, fragment order and link placement. Compilation-local object references or sequence numbers do not identify declarations across compilations, and equal names/ranges/rendered text alone are insufficient.

## 23.4.11.5. Previous analysis while updating

When input changes require revalidation and the latest check is pending/running, retain at most the last adopted set for each open document. Freeze its participant set and unit information. All declaration, proof and documentation fields come from that set; it can answer positions never previously rendered. An unaffected valid set remains current.

Prepend `Previous analysis: update pending` to every previous response. It applies to the entire response and explicitly withholds a guarantee about current code. Return `null` if no previous set exists, its participants disagree on the target, or its range cannot be mapped.

Map positions through accepted incremental edits. If any edit touches the representative range's interior or either boundary at that step, do not reuse that target. Return a current-document range and verify its text equals the previous range's text. Mapping establishes character continuity only: selection, lexical classification, Binding and documentation remain historical. Earlier edits may have turned the surviving text into a comment or changed its binding; the previous notice still applies. Do not add request-time lexing or semantic analysis to guess equivalence.

| Event/state | Action |
| --- | --- |
| A complete valid set becomes available, including revalidation-only recovery | Switch atomically, release obsolete historical data and edit mapping; missing/different current targets return `null`, never fall back |
| Ordinary source edits temporarily leave Test-unit necessity or related facts undetermined | Continue the same previous set while waiting |
| Configuration/project-setting input, membership or placement changes; close/reopen; loss of document synchronization | Discard previous data and mapping |
| Full replacement or edit-history limit | Discard, without matching by spelling or proximity |
| The check definitively finishes for this document's latest input without an adoptable set, including missing Hover data, Blocked or Faulted output | Discard; subsequent edits do not resurrect it |
| New input arrives during a check and its next check remains pending | That intermediate completion alone does not discard the retained set |

## 23.4.11.6. Responses, reuse and isolation

Advertise `hoverProvider: true`; answer `textDocument/hover` with `Hover | null`, using `MarkupContent`. Select the first supported client `textDocument.hover.contentFormat`; use plaintext for absent, empty or unsupported preferences. Markdown declaration fences use language ID `kimi`. Integrate general and effect Hover in one provider; the editor extension does not reconstruct semantics. Each request receives one response; cancellation/shutdown follows existing message processing, and a late cancellation never causes a second response.

Process requests synchronously on the state owner using events already applied: map/search positions, compare, parse/render needed documentation, and enqueue the response in one operation. Do not wait for a check, re-Bind, or add a Hover worker, pending job, asynchronous completion event or concurrent cancellation mechanism.

- Strictly compare immutable inputs on first use; cache agreement, disagreement, absence and completed rendering. A hash alone cannot establish equality.
- Bind reuse to target, participants, each adopted result's generation, rendering rules/format/limits and placement. Update eligibility on invalidation/adoption. Repeated comparison/rendering must not rescan comment text or effect evidence.
- Attach current ranges and the historical notice separately from the body; position shifts alone do not rerender it. Transient failures are not successful cache entries; reproducible limit results retain their reason. Elapsed time alone never determines a partial answer.
- Bound comparison work, parse input/depth, output, edit history and cache storage. An incomplete agreement check returns `null`; documentation/output limits follow §23.4.11.3. Record limits and responsiveness criteria and measure maximal inputs and continuous requests. Synchronous work delays later server events, not the editor's text-entry mechanism; finite limits alone establish no latency claim.

Detach immutable Hover facts before releasing each compilation. Do not retain mutable syntax, Binding or declaration nodes in LSP state. Share declaration headers, documentation inputs and existing Copy proofs for the same complete Type/Constraint context. Index all checked own-project `file:` members, even closed ones; dependency references retain needed declaration metadata rather than indexing every dependency for every consumer. Use indexed position lookup, not a per-request AST/declaration scan. Parse comments only when requested; distinguish unavoidable serialization cost from lookup/reuse allocations.

Documentation collection and Hover generation are opt-in for checks that need them; ordinary compilation does not pay their per-compilation costs. Embedded Kimi token reuse must neither prevent later documentation collection nor force collection into a disabled compilation. Share immutable source/token/comment-candidate data; associate and classify per compilation. Invocation order and concurrency do not change results or diagnostic collection.

Optional Hover processing has an isolation boundary covering collection, indexes, Copy projection, header formatting, detachment, comparison and rendering. A documentation-only failure retains the other fields with a notice. Unit information-generation failure makes that unit's Hover unavailable while preserving finalized diagnostics, acceptance and TestPresence. Request comparison/rendering failure returns `null` without changing adopted immutable facts or diagnostics. Core-analysis failures remain Faulted. Log optional failures; do not swallow input-invalidation/cancellation control flow as internal errors. Enabling collection or asking for Hover never changes check outcomes.

Release unneeded information, history and cache entries on replacement, retirement, close and session end. Historical retention is limited to open documents' required immutable data and sources for one prior set. Allocation/reuse checks and explicit latency/throughput measurements must cover initial/repeated requests, edits, dependency changes, multiple configurations, long comments, effect evidence, history and lifetime. Any later asynchronous rendering design is a separate change justified by measured responsiveness needs.
