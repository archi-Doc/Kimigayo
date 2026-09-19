# Documentation Markdown product API

`DocumentationMarkdown` uses the independent limited-profile parser. Compiler
assemblies and packages have no Markdig dependency; Markdig remains a private
dependency of comparison tests and benchmarks only. The adopted language rules
are in [§2.3](../../../spec/02-source-and-lexical-structure.md#231-documentation-text)
and the design referenced there.

## Parsing and declaration context

```csharp
// Binding selects public fragments and handles specializations/merged declarations.
foreach (var comment in compilation.Binding.GetDocumentation(declaration, publicOnly: true))
{
    var markdown = DocumentationMarkdown.Parse(comment, cancellationToken);
    var summary = markdown.Summary; // nullable DocumentationMarkdownNode
    var items = markdown.GetItems(cancellationToken);
    var html = markdown.ToHtml(new DocumentationHtmlOptions(), cancellationToken);
}
```

Each fragment is parsed independently. Collection remains optional and ordinary
compilation does not parse Markdown. Parsing never starts Binding. Before Binding
has provided declaration facts, list candidates remain `Unclassified`; querying
the same facade after Binding classifies them using current facts. Classification
is not cached across declaration changes. Standard headings are independent of
declaration facts. All parameter namespaces are matched exactly, and receiver
exclusion uses Binding's receiver index rather than the spelling `self`.

API migration:

| Old facade member/type | Independent replacement |
| --- | --- |
| `MarkdownDocument Document` | `DocumentationMarkdownDocument Document`; enumerate `Document.Root.Children` |
| `ParagraphBlock Summary` | Nullable `DocumentationMarkdownNode` |
| `DocumentationItem` / `IReadOnlyList` | `DocumentationMarkdownItem` / `ReadOnlyMemory`; use `Items.Span` or `GetItems(token)` |
| `Label`, `DescriptionStart`, `DescriptionEnd` | `Name`, `DescriptionSpan.Start`, `DescriptionSpan.End` |
| `IsParameter` | `Kind`: Unclassified, Standard, Parameter, Ambiguous, Unknown |
| Markdig node reference identity | Immutable node equality (document identity plus node ID) |

Spans are half-open UTF-16 ranges; `Node.SourceSpan` and `SourceDescriptionSpan`
map to the original source. The default parse depth limit is 256, configurable per
parse. A limit or cancellation interrupts documentation processing without becoming
a language diagnostic. Published syntax is immutable; rendering never changes it.
Optional item diagnostics require selected, bound declarations and remain separate
from compiler diagnostics. Other optional writing diagnostics are not implemented.

## HTML and link placement

The default `ToHtml()` uses heading offset 1, LF output, HTTP/HTTPS/mailto links,
and identity placement of logical source paths **under the output URL root**.
For example, `src/api.kimi` linking to `guide.md` produces `/src/guide.md`.
This replaces the old ambiguous `src/guide.md` page-relative output. A renderer
publishing a different layout supplies a mapper:

```csharp
var options = new DocumentationHtmlOptions
{
    DeclarationHeadingLevel = 2,
    PageUrl = "https://docs.example/api/current.html?view=full",
    HtmlBaseUrl = "https://docs.example/assets/",
    MapSourcePath = target => FindOutputPath(target.ProjectIdentity, target.LogicalPath),
};
string html = markdown.ToHtml(options, cancellationToken);
```

`MapSourcePath` receives a `DocumentationLinkTarget` with project identity,
decoded logical path, query and fragment kept separate. The default identity is
the declaration's owning `Kotonoha` (module/project source boundary); tools may
supply their own stable `ProjectIdentity`. The mapper returns a **decoded output
path, not a URL**, relative to the HTML base or beginning with a single `/`.
The renderer encodes path data, prefixes `./` if a path-relative first segment
contains `:`, and attaches the original query/fragment separately. Null means no
output mapping. In particular, `#` and `%` in returned path data are not URL
delimiters. `DocumentationLinks.ResolveSource` exposes the same structured source
resolution independently.

Empty destinations, queries and fragments use the display page, including for
generated sources. Query absence inherits the page query; an explicit empty query
replaces it; absent fragments are not inherited. Specify the absolute `PageUrl`
when HTML has a separate `base`; missing page information raises an argument error
when a page-relative reference needs it. With no explicit base/page, those
references remain page-relative in the output. Generated sources cannot resolve
nonempty source-relative paths.

`RewriteLink`, if supplied, operates once on the assembled URL, before final URL
validation and HTML attribute escaping. Its null result disables the link.
`AllowScheme` can override the default ASCII scheme policy; HTTP(S) still requires
`scheme://host`. Internationalized hosts must be converted to a supported ASCII
form by the tool before use. Source paths are UTF-8 decoded exactly once; invalid
encoding, decoded separators/controls and project-root escape are rejected.
Rejected ordinary links keep their decorated label; rejected autolinks keep the
escaped original spelling including angle brackets.

Rendering uses an iterative traversal, cancellation checks and at most one cached
StringBuilder of capacity 65,536 per rendering thread. Larger builders are released;
callbacks may reenter rendering without sharing mutable buffers. No partial output
or HTML cache is published. The standalone syntax API also provides `ToHtml(options,
token)` and permits heading offset 0. Full output conditions and measurements are
in the [benchmark report](../../../Benchmark/DocumentationMarkdown.md).
