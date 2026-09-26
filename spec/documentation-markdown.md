# Documentation Markdown profile

[Specification index](../SPEC.md) · [Documentation Comments](02-source-and-lexical-structure.md#231-documentation-text)

## 1. Scope and authority

This is the normative Markdown profile for [§2.3.4–6](02-source-and-lexical-structure.md#234-markdown-and-links). Use [CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/) for adopted syntax unless this profile explicitly changes a rule; full CommonMark compatibility is not required.

[§2.3.1–3](02-source-and-lexical-structure.md#231-documentation-text) owns `///` recognition, text extraction, declaration association, configuration selection and publication scope. Documentation does not change name resolution, Type checking or execution. Unless marked `kimi`, the examples below show extracted Markdown without `///`; the tables that explain this specification add no table syntax to the profile.

## 2. Syntax

### 2.1. Common parsing rules

- Count indentation columns from each extracted line's start, even after consuming outer list or quote prefixes. Use CommonMark space/tab rules; U+0020 indentation is recommended. This does not change the ban on tabs in executable source.
- Keep CommonMark's block precedence, paragraph-interruption conditions and at-most-three-column start indentation after outer containers. Omitted block rules neither start blocks, terminate paragraphs nor suppress other syntax.
- Parse inline content after its paragraph or heading boundary and content are settled. Eager, incremental and lazy processing must produce the same completed result; publication rules are in [§5.2](#52-completion-concurrency-and-reuse).
- Do not reparse decoded escapes or character references as Markdown. Item extraction is a separate operation on parsed content.
- Interpret U+0000 as U+FFFD before applying Markdown rules, including link recognition and delimiter classification. Preserve the extracted input and its UTF-16 coordinates; HTML must not emit NUL characters.
- Unrecognized notation is processed by the remaining rules, not protected as one literal unit. Use code notation to force literal display. Unclosed inline delimiters remain text under CommonMark rules; unclosed fences follow §2.2.2. Incomplete Markdown is not a documentation syntax error or a reason to fail ordinary compilation. Existing source encoding, lexical and language syntax checks still apply.
- Use Unicode **15.0.0** for Markdown whitespace, punctuation and symbol classification, with CommonMark's classification conditions. Runtime, OS and locale must not change results. A Unicode version change is a parsing-rule revision.

### 2.2. Block syntax

#### 2.2.1. Paragraphs, breaks and headings

Blank lines separate paragraphs. A normal newline inside a paragraph is a soft break, serialized as a newline in HTML, not `<br />`. Two or more trailing spaces or a trailing backslash produce a hard break where CommonMark permits it, serialized as `<br />`. A backslash is recommended because it is visible and survives trailing-space cleanup. Extracted trailing spaces remain significant under §2.3.1.

```markdown
One paragraph continues
on this line, then uses a hard break.\
This line starts a new displayed line.

This is a second paragraph.
```

Use CommonMark ATX headings (`#` through `######`). Display them below the enclosing declaration heading while preserving relative levels: below a level-2 declaration, `#` becomes level 3. The renderer defines the representation beyond HTML level 6; `div role="heading" aria-level="7"` is one permitted representation.

#### 2.2.2. Fenced code and block-start indentation

Use CommonMark backtick or tilde fences. Code content does not interpret headings, documentation items, emphasis, links, escapes or character references. Escape it when rendering HTML. The first word of the info string may be passed to the renderer as a language hint; `kimi` does not request compilation or execution. Language hints must not enable diagrams or other extensions.

````markdown
```kimi
let total = add(2, 3)
# This is code, not a heading.
```
````

An unclosed fence continues to the end of its containing list item or quote, or to EOF at the document root.

Indentation alone never creates a code block. At a block-start position, four or more remaining columns cannot start a heading, fence, list or quote; they are paragraph content. Existing fences and outer-container continuation retain their own rules. Paragraph rules determine displayed indentation; original syntax ranges remain available.

#### 2.2.3. Lists and explicit continuation

Bullet markers, ordered markers/start numbers, empty items and interruption rules follow CommonMark. Compute each item's required continuation indentation from its content start, not a fixed two or four columns. If a marker of width `W` is followed by at least five columns of whitespace, consume `W + 1` columns as item indentation and process the rest as content. Thus `-     a` contains paragraph `a`, with two-column continuation indentation.

Every nonempty continuation line must meet the item's indentation. Close each item whose continuation condition fails, then process the line in the remaining outer structure; do not use lazy paragraph continuation. Blank lines within items need no indentation. Items may contain paragraphs, child lists, headings, quotes and fenced code.

````markdown
10. An ordered item.
    This line needs four columns, not three.

- value: The input.

  A second paragraph in the same item.

  - A nested condition.

  ```kimi
  let result = add(2, 3)
  ```

- return: The result.
This line is outside the list.
````

Keep CommonMark's tight/loose list classification from blank lines and child-block placement. Tight item paragraphs omit HTML `<p>` wrappers, and loose ones keep them. Classification is per list, and child lists are independent. Simple paragraph items without separating blank lines are tight; separating blank lines make them loose.

#### 2.2.4. Quotes

Every quoted line, including an internal blank line, requires `>`. Nested quotes require the marker at every continuing level; both `>>` and `> >` are valid. A line without the required marker is outside that quote. Lists and quotes consume their required indentation/markers from outside to inside. All adopted block forms may occur inside quotes.

```markdown
- note: An item containing a quote.

  > First paragraph.
  >
  > > Nested quote.
```

### 2.3. Inline syntax

Use CommonMark code spans, `*`/`_` emphasis and strong emphasis, backslash escapes, inline links with optional titles, and angle-bracket URI/email autolinks. Code spans do not decode escapes or character references. Link labels may contain code and emphasis. Bare URLs are not autolinks. Recognizing link syntax does not authorize its destination; [§4](#4-html-and-links) controls output.

```markdown
Use `value`, *emphasis*, **strong emphasis**, or \*literal stars\*.
[Guide](guide.md "Usage") and <https://example.com/api>
<name@example.com>
```

Numeric references `&#...;`, `&#x...;` and `&#X...;` use CommonMark digit limits, Unicode value handling and invalid-value replacement. Only these five named references are recognized, case-sensitively and with a required semicolon:

| Reference | Decoded character |
| --- | --- |
| `&amp;` | `&` |
| `&lt;` | `<` |
| `&gt;` | `>` |
| `&quot;` | `"` |
| `&apos;` | `'` |

References decode only in CommonMark-permitted positions, never inside code. `&#42;` produces a literal `*`, not an emphasis opener. `&copy;` and `&AMP;` remain literal; escape their ampersands in HTML so the browser does not interpret them later.

### 2.4. Omitted syntax and boundary examples

Do not recognize Setext headings, indented code, thematic breaks, reference links/definitions, images or raw HTML. Do not create a reference-definition dictionary. Also omit extensions such as tables, footnotes, strikethrough, task lists, mathematics, Mermaid and custom tags.

Each row is an independent input. `⏎` denotes LF and `␠` denotes U+0020.

| Input | Result under this profile |
| --- | --- |
| `example⏎---` | One paragraph, not a Setext heading. |
| `␠␠␠␠# example` or `␠␠␠␠- a` | Paragraph text, not indented code, a heading or a list. |
| `- a⏎b` or `> a⏎b` | `b` is outside the list or quote. |
| `10. a⏎␠␠␠b` | `b` is outside the item; continuation needs four columns here. |
| `[x][r]⏎⏎[r]: guide.md` | Two ordinary paragraphs; no link or hidden definition. |
| `![image](guide.md)` | Literal `!` followed by an inline link labeled `image`. |
| `---` | A paragraph, not a thematic break. |
| `- - -` | Three nested list items, ending in an empty item. |
| `&copy;` or `&AMP;` | Literal spelling, not a named character reference. |
| `<T>⏎*description*` | Literal `<T>` and emphasis within a paragraph; no HTML block suppresses Markdown. |
| `<Key:Value>` with default URL settings | Parsed as an autolink; rendered literally with its angle brackets because its scheme is not allowed. |

Use ATX headings, fenced code, explicit continuation, inline links and the adopted character references instead of omitted forms. Optional writing diagnostics are owned by [§2.3.6](02-source-and-lexical-structure.md#236-tooling-and-diagnostics).

## 3. Summary and documentation items

### 3.1. Summary and standard names

The first block is the summary only when it is a paragraph. Do not infer a summary from a heading or list. Standard item names are case-sensitive; old spellings such as `Returns` are not aliases. They are documentation conventions, not new language keywords, and descriptions may use any natural language.

| Name | Meaning |
| --- | --- |
| `return` | Result meaning and conditions |
| `abort` | Function-specific Abort conditions |
| `safety` | Existing obligations on an unsafe caller |
| `note` | Additional explanation |
| `warning` | Constraints needing particular care |
| `example` | Usage examples |

Use short list items or longer standard sections. See [§2.3.5](02-source-and-lexical-structure.md#235-writing-and-extracting-items) for writing guidance and a complete declaration example.

### 3.2. Extraction and description ranges

A name is unformatted text, including decoded escapes/adopted character references, or one code span. Do not mix plain text and code or accept emphasis/link wrappers as a name. Use the decoded text or CommonMark-normalized code-span content without further trimming, case folding or Unicode normalization. Do not recheck reserved words, character classes or NFC as language identifiers; declaration validity belongs to [§2.5](02-source-and-lexical-structure.md#25-names). Extraction does not change Markdown structure or display.

Inspect only root-level lists and headings, not quotes, code or nested lists:

| Node | Recognition | Description range |
| --- | --- | --- |
| Root list item | Its first block is a paragraph starting with a nonempty name and ASCII `:`. | Immediately after the colon's complete source spelling to the item end, including child blocks. |
| Root heading | Its whole content is one name exactly matching a standard name. No colon. | After the heading to the next root heading of equal or higher level, or EOF. |

For plain names use the first colon; for code names require a colon immediately after the code span. Decide the delimiter and following whitespace from decoded plain text. The colon must be followed by U+0020, tab, newline or paragraph end. List items become candidates; [§3.3](#33-declaration-dependent-classification) classifies them. Headings extract standard items only and never match parameters.

Keep original node identity, source order, duplicates and containment. Unknown candidates and free headings stay in the body; do not reconstruct the document or overwrite same-name items. [§5.1](#51-read-only-syntax-and-source-positions) defines coordinates and source mapping.

Assume `value` names exactly one parameter and `return` names none:

| Input | Extraction |
| --- | --- |
| `- value: Input.` or ``- `value`: Input.`` | Parameter item `value`. |
| `- return: Result.` or ``- `return`: Result.`` | Standard item `return`. |
| `- return\: Result.` or `- return&#58; Result.` | Standard item; description starts after `\:` or `&#58;`. |
| `- r&#101;turn: Result.` | Match decoded name `return`. |
| `- return:Result.` or `- **return**: Result.` | No candidate: missing delimiter whitespace or a decorated name. |
| `# return` or ``# `return` `` | Standard section. |
| `# Return` or `# Returns` | Ordinary heading. |
| ``- ` value `: Input.`` | Code-span whitespace rules yield parameter name `value`. |
| `- value : Input.` or ``- `value name`: Input.`` | Unknown candidate; preserve the body. |

### 3.3. Declaration-dependent classification

Match ordinary arguments by external name and Type, Semantics, constant and Origin parameters by declared name. Exclude only parameters whose declaration role is receiver. Match exact names in this order:

| Matches | Classification |
| --- | --- |
| Exactly one parameter | That parameter's description. |
| Multiple parameters | Ambiguous candidate; no fallback to a standard item. |
| No parameter, but a standard name | Standard item. |
| Neither | Unknown candidate, displayed as ordinary content. |

Do not finalize list classification before declaration facts are available. Backticks do not change precedence. A heading distinguishes a standard section from a same-name parameter:

```kimi
/// Returns the supplied text.
/// - note: The input parameter.
/// - return: The supplied text.
///
/// # note
/// This section describes the whole function.
func echo(note: string) -> string => note

group Samples
    /// Returns its input.
    /// - self: An ordinary parameter, not a receiver.
    func identity(self: i32) -> i32 => self
```

Receiver roles follow [§7.3](07-functions-and-callable-values.md#73-explicit-receivers), not the spelling `self`. Unknown/ambiguous candidates may receive optional documentation diagnostics under §2.3.6. Missing `abort` prose does not rule out other Aborts; `safety` neither changes unsafe designation nor grants calling permission.

## 4. HTML and links

### 4.1. Escaping and display

Raw HTML is neither block nor inline syntax. Escape body text, code and attributes appropriately without changing the tree. HTML-looking text must not suppress subsequent Markdown (see the `<T>` example in §2.4). Autolink angle brackets remain distinct from HTML syntax.

Unresolved, disallowed or rewrite-disabled ordinary links retain their label and formatting. Disabled autolinks retain their escaped original spelling including angle brackets, such as `<Foo::bar>` under the default policy. Do not automatically link code names to declarations or introduce `kimi:` links. Renderers define stable anchors/output organization; recognizing `#anchor` does not prove that the anchor exists.

### 4.2. Reference kinds and bases

Classify the Markdown-decoded destination in this order, subject to [§4.4](#44-url-validation-and-serialization):

| Kind | Example | Base and generated-source behavior |
| --- | --- | --- |
| Absolute URL | `https://example.com` | Renderer scheme policy; also usable from generated source. |
| Network-relative | `//host/path` | Disallowed in HTML, including from generated source. |
| Relative with empty path | Empty destination, `?q`, `#f`, `?q#f` | Display-page URL; also usable from generated source. |
| Root-relative | `/guide.md` | Output URL root, not the source-project root; also usable from generated source. |
| Source-relative | `guide.md#sec` | Resolve the nonempty path within its source project; unresolved for generated source. |

For an empty path, an explicitly supplied query replaces the page query; an absent query inherits it. Use an explicitly supplied fragment, but never inherit an absent fragment. Keep empty query/fragment distinct from absence, following [RFC 3986 §5.2.2](https://www.rfc-editor.org/rfc/rfc3986.html#section-5.2.2). A query may identify another resource. If HTML `base` differs, explicitly use the display-page URL to preserve this reference base.

For display page `https://docs.example/api.html?old#previous`, an empty destination resolves to `https://docs.example/api.html?old`, `#part` to that URL plus `#part`, and `?#` to `https://docs.example/api.html?#`.

### 4.3. Logical source targets and output mapping

Use the ordinary source's [§20.7.4 logical name](20-compilation-configuration.md#2074-generated-sources-and-declaration-order) without requiring a file-existence check. Keep project identity, logical path, query and fragment as separate values; do not join a logical target into a URL and parse it again.

1. Split the URI reference into path, query and fragment. Encoded `?` and `#` are data, not separators.
2. Split the path on `/` and decode each segment as UTF-8 exactly once. Reject invalid encoding and decoded `/`, backslash or controls. Do not decode the existing logical source name.
3. Resolve `.` and `..` against the source name's parent, rejecting traversal outside the project. Do not include query/fragment in path normalization.
4. Let the renderer map the logical target to an output location and assemble the URL. If no mapping is available, leave the link unresolved. Encode path data such as `#` and `%` separately from query/fragment.

This follows [RFC 3986 §2.4](https://www.rfc-editor.org/rfc/rfc3986.html#section-2.4): delimit first, decode second. `%252e%252e` becomes logical name `%2e%2e`, never `..` through a second decode.

| Source | Destination | Logical target |
| --- | --- | --- |
| `src/api.kimi` | `guide.md#sec` | Path `src/guide.md`, fragment `sec`. |
| `src/api.kimi` | `api%23v2.kimi?q` | Path `src/api#v2.kimi`, query `q`. |

With unchanged path placement, the latter URL path/query is `src/api%23v2.kimi?q`. Actual `href` still depends on the output page and base URL; never assume a source logical path is already relative to the output page. No particular output layout is required.

### 4.4. URL validation and serialization

The renderer configures permitted schemes; defaults are `http`, `https`, `mailto`. Compare schemes case-insensitively in ASCII. Apply these checks to input destinations and again after mapping or rewriting:

1. Reject leading/trailing U+0020, Unicode 15.0.0 control characters (`Cc`), backslashes and invalid percent escapes. Do not trim and retry.
2. Validate URL syntax and reference kind. Reject network-relative URLs and unapproved schemes. Reject encoded `/`, backslash and controls in relative paths. HTTP(S) requires `scheme://host` with a nonempty host; do not repair `https:example.com` into an accepted URL.

Assemble output without changing reference kind or interpreting data as delimiters:

| Component or source | Output rule |
| --- | --- |
| Logical path | UTF-8 percent-encode each segment's data, including `%`, `?`, `#` and characters not allowed in that segment; join with `/`. |
| Path-relative output | If the first segment contains `:`, prefix `./`: output path `a:b.html` becomes `./a:b.html`, not a scheme or root-relative reference. |
| Root-relative output | Preserve one leading `/`; reject a generated `//` prefix. |
| Destination already in URL form | Preserve checked delimiters and valid `%HH`; encode only data not allowed in its component. Do not double-encode existing escapes. |
| Query and fragment | Build separately from the path, preserving absence versus empty values; do not create delimiters from data. |

Allowed component characters follow [RFC 3986 §3](https://www.rfc-editor.org/rfc/rfc3986.html#section-3). Scheme-specific validation and conversion, including internationalized host names, belong to the renderer; disable destinations it cannot handle. Do not encode hosts as path segments.

After assembly, check the leading scheme and slashes to confirm the intended reference kind and policy. A scheme inherited from the display base is distinct from one explicitly supplied by the destination. Escape HTML attribute values last, with no later destination rewrite; a rewrite returning ` javascript:...` fails the same input checks.

These rules prevent reference-kind/scheme confusion; they do not promise identical destinations under every URL parser. A browser comparison loop or embedded WHATWG parser is not required. Character checks and component assembly can use linear scans; the entire process, including host conversion, need not be one scan.

## 5. Syntax API and processing guarantees

### 5.1. Read-only syntax and source positions

The product uses an internal parser and independent public types, with no dependency on Markdig types; Markdig, if kept for comparison, belongs only in tests and benchmarks. Expose completed results as read-only, preserving the heading levels, list kind, start and tightness, destinations and titles, and code language information needed for display and extraction.

Summary/item node identity must remain stable within one parsed document. Object references or document-plus-node IDs are both valid; neither .NET reference identity nor per-node allocation is required. Do not reuse identities across parsed documents.

All public body and source coordinates are half-open UTF-16 ranges `[start, end)`, not display columns. Retain:

- Each node's syntax range and each item's description range in extracted text.
- The smallest single source interval covering each range; multi-line ranges may include intervening `///` and indentation.
- Exact original-source ranges for documentation diagnostics, using a covering interval for multi-line targets.

Map a normalized LF to the complete original LF, CR or CRLF. An empty range maps to its text insertion point: after the removed prefix at a line start, or at the last line's content end at EOF. Never infer source offsets from displayed-string length. An autolink's original spelling may be recovered from its body range.

Per-display-character mappings and marker-free interval lists are optional. If supplied, decoded escapes/references map to their whole source spelling, and virtual spaces from partially consumed tabs map to that tab. Virtual spaces do not alter body coordinates. No per-node detailed map is required. For `- return&#58; Result.`, the description starts after `&#58;`; a diagnostic targeting the delimiter covers that entire spelling, not one decoded character.

### 5.2. Completion, concurrency and reuse

[§2.3.6](02-source-and-lexical-structure.md#236-tooling-and-diagnostics) owns when collection and processing are enabled. Once requested, whole-document eager parsing or incremental/lazy parsing is permitted. Summary structure needs a settled first paragraph; displaying it also needs inline interpretation. Do not publish unresolved fence boundaries, list tightness or section-description ranges. Optimizations must preserve syntax, display, extraction, required positions and published identity.

Concurrent requests on a parsed document share completed results and identity for the same publication unit. The implementation chooses synchronization, waiting or duplicate computation. An interrupted attempt must not publish partial results or a permanent failure, block other requests or prevent retries; preserve already published results. Duplicate work remains subject to resource verification.

Caching is optional; recomputation is permitted. Equal inputs and rules produce equal results. Reuse requires matching all dependencies below, including parser/URL rule versions and Unicode data version; the table does not require a cache per row.

| Result | Dependencies |
| --- | --- |
| Extracted text and source mapping | Immutable source, block range, extraction rules. |
| Syntax and item candidates | Text/mapping plus Markdown and extraction rules. |
| List classification and parameter association | Candidates, classification rules, declaration identity, names and receiver roles. |
| Nonempty source-relative target | Parsed destination, project, ordinary/generated source kind, logical source name, resolution rules. |
| HTML | Syntax, targets, output mapping, display page/base URL, heading/anchor/language/URL/rewrite settings, and classification if displayed. |
| Diagnostics and publication | Relevant preceding results, configuration, selection, publication scope and diagnostic settings. |

For example, identical `[guide](guide.md)` text in `a/api.kimi` and `b/api.kimi` has different logical targets; text equality alone cannot justify shared HTML. External callbacks must be deterministic for their inputs/settings; do not cache results whose dependencies cannot be identified. Never mix trees from different sources or rules. Recompute affected results when dependencies change; invalidation granularity is implementation-defined. Enabling optional diagnostics must not alter parsing. Declaration fragments remain independent under §2.3.3.

### 5.3. Complexity and interruption

For UTF-16 body length `n` and output size `o`, use `O(n + 1)` retained syntax/required mapping space and `O(n + o)` for one tree traversal or HTML generation as the baseline. Aim for parsing/extraction proportional to input; avoid repeated searches of the same ranges that cause quadratic growth. Do not copy the body or overlapping descriptions per node.

Source-position queries, declaration matching and link resolution must avoid whole-input scans per character/item. Account separately for source reads, declaration/logical-name inputs, callbacks, output storage, concurrent work and optional detailed mappings, including their input/output sizes and call counts.

Parsing, extraction, output and diagnostics must handle deep nesting without stack exhaustion. Support cancellation. If a tool imposes time, memory or depth limits, publish their conditions and report excess as a documentation interruption, never a syntax-to-text fallback or a language error. No particular parser algorithm, node arena, pool, cache, lazy strategy, candidate prefilter or synchronization mechanism is required. Acceptance evidence is specified in [Appendix A.21](appendices/A-compiler-requirements.md#a21-documentation-comments).
