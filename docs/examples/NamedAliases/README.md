# Named aliases

Run [NamedAliases.kimi](NamedAliases.kimi) with the normal build/run commands.
`Output` names the public Console group, and `Standard` names the Kimi library.
Both names apply only to this source document. They do not open members or create
new Types. The last call uses the mandatory default alias that opens Kimi.

To use bare `writeLine`, put `alias Kimi.Console` before the executable items.
See [source aliases](../../spec/18-modules-and-dependencies.md#181-external-references-and-aliases).
