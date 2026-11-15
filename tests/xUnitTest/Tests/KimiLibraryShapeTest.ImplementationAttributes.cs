// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The implementation Attributes the library's sources state (IMPL 21.3.4; R8-G1 rows L1, L3, L4 and L5; row L2 is
// BodilessFunctionsAreCompilerFunctions, row L6 ParserOptimizationTest.TheLibrarysLinkedConstructorsCarryLang). Each row holds the
// Attributes equal to the catalog's columns, which still register the declarations.
public partial class KimiLibraryShapeTest
{
    // Row L1: `#Lang("Name")` names a KimiDeclarationId member and sits on the one declaration the catalog registers under it; each Name
    // is declared once, a declaration carries one Name, and every lang item the library declares in source carries its Name.
    [Fact]
    public void EachLangItemIsDeclaredOnce()
    {
        var library = Library.Value.Library;
        var declared = new Dictionary<KimiDeclarationId, Koto>();
        foreach (var (attribute, target) in LibraryAttributes(AttributeKind.Lang))
        {
            var name = Assert.Single(Texts(attribute));
            Assert.True(Enum.TryParse<KimiDeclarationId>(name, out var id) && id.ToString() == name, name);
            Assert.True(declared.TryAdd(id, target), name);
            Assert.Same(library.GetSymbol(id)?.Declaration, target);
        }

        var sourced = new List<KimiDeclarationId>();
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if (library.GetSymbol(entry.Id) is not null)
            {
                sourced.Add(entry.Id);
            }
        }

        Assert.Equal(sourced.Order(), declared.Keys.Order());
        Assert.Equal(declared.Count, declared.Values.Distinct().Count());
        Assert.Equal(190, declared.Count);
    }

    // Row L3: `#Intrinsic("Name")` names an IntrinsicId member, the disposition the intrinsic table gives the declaration's compiler
    // function; `#Intrinsic("Fail", "Code")` carries Fail's SPEC 22.5.2 code. An intrinsic other than Fail is named once. The
    // intrinsics no declaration names are those the MIR builder emits without a library function, `$abort`'s among them.
    [Fact]
    public void EachIntrinsicAttributeNamesItsIntrinsic()
    {
        var named = new List<IntrinsicId>();
        foreach (var (attribute, target) in LibraryAttributes(AttributeKind.Intrinsic))
        {
            var texts = Texts(attribute);
            Assert.True(Enum.TryParse<IntrinsicId>(texts[0], out var id) && id.ToString() == texts[0] && id != IntrinsicId.None, Line(target));
            Assert.Equal(id == IntrinsicId.Fail ? 2 : 1, texts.Length);
            Assert.Equal(IntrinsicDisposition.Of(target.SymbolOf()!.CompilerFunction), new(DispositionKind.Intrinsic, id, id == IntrinsicId.Fail ? texts[1] : null));
            named.Add(id);
        }

        var once = named.Where(static x => x != IntrinsicId.Fail).ToArray();
        Assert.Equal(once.Distinct(), once);
        Assert.Equal((18, 5), (once.Length, named.Count - once.Length));
        Assert.Equal(
            [
                IntrinsicId.None, IntrinsicId.Abort, IntrinsicId.TypeTest, IntrinsicId.TestObserve, IntrinsicId.TestMessage, IntrinsicId.TestRequireAbort,
                IntrinsicId.TestRecordValue, IntrinsicId.StaticEnsure,
            ],
            Enum.GetValues<IntrinsicId>().Except(named));
    }

    // Row L4: a published row (SPEC 4.9.6, 22.10.1) is keyed by a lang item the library declares, and the requirement it runs is one too.
    [Fact]
    public void PublishedRowsAreKeyedByLangItems()
    {
        var library = Library.Value.Library;
        var rows = 0;
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if (entry.Row is not { } row)
            {
                continue;
            }

            rows++;
            Assert.Equal(entry.Id.ToString(), LangName(library.GetSymbol(entry.Id)!.Declaration));
            if (row.Runs is { } runs)
            {
                Assert.Equal(runs.ToString(), LangName(library.GetSymbol(runs)!.Declaration));
            }
        }

        Assert.Equal(36, rows);
    }

    // Row L5: the functions carrying `#CallerLocation` (SPEC 22.5.1) are exactly the catalog's CallerLocation column, whose private ABI
    // ModuleCodegen reads (KimiLibraryCatalog.RequiresCallerLocation); the Attribute takes no argument.
    [Fact]
    public void TheCallerLocationAttributesAreTheCatalogsColumn()
    {
        var declared = new List<KimiDeclarationId>();
        foreach (var (attribute, target) in LibraryAttributes(AttributeKind.CallerLocation))
        {
            Assert.Empty(Texts(attribute));
            declared.Add(Assert.IsType<FunctionKoto>(target).SymbolOf()!.LibraryDeclaration!.Value);
        }

        var column = new List<KimiDeclarationId>();
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            if (entry.CallerLocation)
            {
                column.Add(entry.Id);
            }
        }

        Assert.Equal(column.Order(), declared.Order());
        Assert.Equal(81, declared.Count);
    }

    // The library's Attributes of one kind, each with the declaration it is written on, in source order.
    private static List<(AttributeKoto Attribute, Koto Target)> LibraryAttributes(AttributeKind kind)
    {
        var found = new List<(AttributeKoto, Koto)>();
        foreach (var node in KotoTree.Walk(Library.Value.Library.Kotonoha.RootKoto))
        {
            if (node is AttributeKoto attribute && attribute.Kind == kind)
            {
                var target = attribute.Parent;
                while (target is AttributeKoto)
                {
                    target = target.Parent;
                }

                found.Add((attribute, target!));
            }
        }

        return found;
    }

    // An implementation Attribute's arguments: non-interpolated string literals (IMPL 21.3.4).
    private static string[] Texts(AttributeKoto attribute)
        => attribute.Operand is InvocationKoto call ? call.ArgumentNodes.Select(static x => Assert.IsType<StringLiteralKoto>(x).Literal).ToArray() : [];

    private static string? LangName(Koto declaration)
        => declaration.FindAttribute(AttributeKind.Lang) is { } attribute ? Assert.Single(Texts(attribute)) : null;
}
