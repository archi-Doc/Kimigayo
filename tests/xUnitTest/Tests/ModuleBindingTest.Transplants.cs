// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public partial class ModuleBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransplantedCallsUseTheRecipientModuleAndKeepTheirSourceLocation(bool recipientHasTarget)
    {
        var c = Create(
            "public func main() => ()\nfunc moved() => ()\n" + (recipientHasTarget ? "func target() => ()" : string.Empty),
            "func target() => ()\nfunc moved() => target()");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var old = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), node => node.Name == "moved");
        var donor = c.SourceModules[1];
        var moved = Assert.Single(KotoTree.Walk(donor.RootKoto).OfType<FunctionKoto>(), node => node.Name == "moved");
        var call = Assert.IsType<InvocationKoto>(moved.ExpressionBody);
        var document = call.CodeContext.SourceDocument;
        var span = call.Method.Span;
        ParseTestHelper.Detach(moved, new CodeBlockKoto(moved.CodeContext));
        Assert.True(KotoHelper.Replace(old.Parent!, old, moved));
        Assert.Same(c.Kotonoha, call.Kotonoha);
        Assert.Same(document, call.CodeContext.SourceDocument);
        Assert.Equal(recipientHasTarget, c.Bind().IsComplete);
        if (recipientHasTarget)
        {
            Assert.Same(c.Kotonoha, call.CallOf()!.Target.Declaration.Kotonoha);
        }
        else
        {
            var error = Assert.Single(CompilationTestHelper.BindingErrors(c));
            Assert.Equal("UnresolvedBinding_Kd", error.Code);
            Assert.Equal("library.kimi", error.Path);
            Assert.Equal(span, error.Span);
            Assert.Null(call.CallOf());
        }
    }

    // G949 N5-1 (DECISIONS BU-220; the owner's O-380 (a)): a SourceDocument's execution scope is keyed by its module root and the
    // document, the module the one that holds the document's top-level items now (AST2), so items of one document in two modules
    // bind in their own module. P1 (SPEC 18.1): after another module's generated body takes a block of the same document, a module's
    // untouched top-level call still resolves in its own module. P2 (O-380 (a) with AST2): a moved top-level call binds the
    // recipient's declaration. P2c (O-380 (a) with AST2): moved code no longer sees the donor document's root function. P4: one
    // SourceDocument object added to two modules (Kotonoha.AddSource) binds in each, as two objects with its text do (P4b, the control).
    [Theory]
    [InlineData("P1")]
    [InlineData("P2")]
    [InlineData("P2c")]
    [InlineData("P4")]
    [InlineData("P4b")]
    public void ADocumentsTopLevelItemsBindInTheModuleThatHoldsThem(string id)
    {
        const string Picks = "group Pick\n    internal func one() -> i32 => 1\n";
        var c = id switch
        {
            "P1" => HostBodyMoved(Picks + "group Host\n    func host()\n        func inner() -> i32 => 3\nlet a = Pick.one()\n", "func helper() -> i32 => 2\n", 0),
            "P2" or "P2c" => HostBodyMoved(
                Picks + "let start = Pick.one()\n",
                "group Pick\n    internal func one() -> i32 => 2\ngroup Host\n    func host()\n        " + (id == "P2" ? "let moved = Pick.one()" : "let moved = helper()") + "\nfunc helper() -> i32 => Pick.one()\n",
                1),
            _ => SharedDocument(Picks + "let a = Pick.one()\n", "group Pick\n    internal func one() -> i32 => 2\n", id == "P4"),
        };

        c.Bind();
        var errors = CompilationTestHelper.BindingErrors(c).Select(static x => $"{x.Code} {x.Path} {x.Text}").ToArray();
        string[] expected = id == "P2c" ? ["UnresolvedBinding_Kd library.kimi helper"] : [];
        Assert.Equal(expected, errors);
        for (var m = 0; m < c.SourceModules.Length; m++)
        {
            foreach (var call in KotoTree.Walk(c.SourceModules[m].RootKoto).OfType<InvocationKoto>())
            {
                Assert.True(call.CallOf() is { } plan ? ReferenceEquals(plan.Target.Declaration.Kotonoha, c.SourceModules[m]) : errors.Length != 0, $"{id}: `{call}` in module {m}");
            }
        }
    }

    // Module `donor`'s `Host.host` body block becomes the other module's generated function's Body after a first Bind (AST2's
    // attachment at the Body slot: each item keeps its document and takes the recipient module).
    private static Compilation HostBodyMoved(string root, string library, int donor)
    {
        var c = Create(root, library);
        c.Bind();
        var host = Assert.Single(KotoTree.Walk(c.SourceModules[donor].RootKoto).OfType<FunctionKoto>(), static x => x.Name == "host");
        var block = host.Body!;
        ParseTestHelper.Detach(block, new CodeBlockKoto(block.CodeContext));
        var recipient = c.SourceModules[1 - donor].GeneratedFunction!;
        Assert.True(KotoHelper.Replace(recipient, recipient.Body!, block));
        return c;
    }

    // `shared.kimi` added to both modules: one SourceDocument object, or two with the same text.
    private static Compilation SharedDocument(string root, string library, bool oneObject)
    {
        var c = Create(root, library);
        const string Text = "func f() -> i32 => Pick.one()\n";
        var shared = new SourceDocument("shared.kimi", Text);
        c.SourceModules[0].AddSource(shared);
        c.SourceModules[1].AddSource(oneObject ? shared : new SourceDocument("shared.kimi", Text));
        return c;
    }
}
