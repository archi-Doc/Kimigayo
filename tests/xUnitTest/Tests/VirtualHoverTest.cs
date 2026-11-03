// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class VirtualHoverTest
{
    private const string Source = "open struct A\n    /// Public contract.\n    public virtual func read(self: objref/Self) -> i32\n        effect confined\n        return 1\nopen struct B : A\n    /// B implementation.\n    override func read(self: objref/Self) -> i32 => 2\nstruct C : B\n    override func read(self: objref/Self) -> i32 => base.read() + self.read()\nfunc inspect(value: objref/A) -> i32\n    let operation = C.read\n    return operation(value)\n()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsRetainThePublicContractAndSelectedDispatch(bool markdown)
    {
        var c = Create(Source);
        var snapshot = HoverBuilder.Create(c);
        var direct = At(snapshot, Source.IndexOf("base.read", StringComparison.Ordinal) + 5);
        var dynamic = At(snapshot, Source.IndexOf("self.read", StringComparison.Ordinal) + 5);
        Assert.Same(Assert.Single(direct.Declarations), Assert.Single(dynamic.Declarations));
        Assert.Contains("Dispatch: direct base", direct.Use);
        Assert.Contains("Base lookup: B", direct.Use);
        Assert.Contains("Implementation: B.read", direct.Use);
        Assert.Contains("Dispatch: dynamic", dynamic.Use);
        Assert.Contains("inherited override-entry evidence", dynamic.Use);
        Assert.DoesNotContain("Implementation:", dynamic.Use);
        foreach (var info in new[] { direct, dynamic, At(snapshot, Source.IndexOf("operation(value)", StringComparison.Ordinal)) })
        {
            var rendered = HoverRenderer.Render(info, markdown, HoverPlacement.Canonical).Body;
            Assert.Contains("Public slot: A.read", rendered);
            Assert.Contains("Available bound: confined", rendered);
            Assert.Contains("Declared by: A.read", rendered);
            Assert.Contains(markdown ? "Public contract\\." : "Public contract.", rendered);
            Assert.DoesNotContain("B implementation.", rendered);
        }
    }

    [Fact]
    public void OverrideDeclarationsRetainTheirOwnDocumentationAndInheritedContract()
    {
        var snapshot = HoverBuilder.Create(Create(Source));
        var info = At(snapshot, Source.IndexOf("override func read", StringComparison.Ordinal) + "override func ".Length);
        Assert.Equal(2, info.Declarations.Length);
        Assert.Contains("Inherited public contract: A.read", info.Declarations[0].Details);
        var rendered = HoverRenderer.Render(info, false, HoverPlacement.Canonical).Body;
        Assert.Contains("B implementation.", rendered);
        Assert.Contains("Public contract.", rendered);
        Assert.Contains("effect confined", rendered);
        Assert.Contains("Ownership checks: pending", rendered);
    }

    // R6-check H6 (DECISIONS R5-M185): the ownership line reads the MIR of the latest analysis, completed when the function's body has no
    // merged problem; before an analysis it is pending (above), and hover never runs one.
    [Fact]
    public void OwnershipChecksReadTheLatestAnalysisMir()
    {
        var c = Create(Source);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var info = At(HoverBuilder.Create(c), Source.IndexOf("override func read", StringComparison.Ordinal) + "override func ".Length);
        Assert.Contains("Ownership checks: completed", HoverRenderer.Render(info, false, HoverPlacement.Canonical).Body);
    }

    // GR-1a (H0b §2.1; DECISIONS R5-M183): a body in which a construction record lies is not judged without a problem. The virtual
    // function's body holds the builder's located Unsupported_Kd, a Field of a Place whose Type an identity premise makes a struct
    // (MirBuildTest.AnUnsupportedFieldsSiteNamesItsReceiversRoot), the analysis's one row, so its line is pending (fail-before:
    // completed, since a record's body is its class, never an ordinal).
    [Fact]
    public void OwnershipChecksArePendingForABodyHoldingAConstructionRecord()
    {
        const string Text = "struct Pair\n    public var a: i32 = 1\n    public init() => ()\nopen struct A<T>\n    T is Pair\n" +
            "    public virtual func read(self: objref/Self, s: ref/T) -> i32 => s.a\n()\n";
        var c = Create(Text);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        c.Analyze();
        Assert.Equal(DiagnosticCode.Unsupported_Kd, Assert.Single(c.Problems).Code);
        var info = At(HoverBuilder.Create(c), Text.IndexOf("func read", StringComparison.Ordinal) + "func ".Length);
        Assert.Contains("Ownership checks: pending", HoverRenderer.Render(info, false, HoverPlacement.Canonical).Body);
    }

    // GR-1a (DECISIONS R5-M183): the analyses skip a body with a validator finding, so it is not judged and its line is pending
    // (fail-before: completed, no problem having its ordinal). No source program makes a finding, a builder defect, so the test injects
    // the fact as R5-M183's tests do.
    [Fact]
    public void OwnershipChecksArePendingForABodyWithAValidatorFinding()
    {
        var c = Create(Source);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        foreach (var body in c.BuildMir().Bodies)
        {
            body.HasFinding |= body.Key.Decl?.Name == "read";
        }

        var info = At(HoverBuilder.Create(c), Source.IndexOf("override func read", StringComparison.Ordinal) + "override func ".Length);
        Assert.Contains("Ownership checks: pending", HoverRenderer.Render(info, false, HoverPlacement.Canonical).Body);
    }

    [Fact]
    public void AgreementIncludesTheDirectImplementationEvenWhenPresentationIsEqual()
    {
        var c = Create(Source);
        var offset = Source.IndexOf("base.read", StringComparison.Ordinal) + 5;
        var before = At(HoverBuilder.Create(c), offset);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.CallOf() is { Kind: CalleeKind.Virtual, VirtualIsDirect: true }).CallOf()!;
        call.VirtualImplementation = call.VirtualSlot.Original;
        call.VirtualImplementingType = call.VirtualSlot.DeclaringType;
        var after = At(HoverBuilder.Create(c), offset) with { Use = before.Use };
        Assert.False(new HoverAgreement().Equal(new(default, before), new(default, after)));
    }

    [Fact]
    public void VirtualReferencesKeepTheBoundDeclaringTypeInAgreement()
    {
        const string Text = "open struct A<T>\n    public virtual func read(self: objref/Self) -> i32 => 1\nlet first = A<i32>.read\nlet second = A<string>.read\n()";
        var snapshot = HoverBuilder.Create(Create(Text));
        var first = At(snapshot, Text.IndexOf(".read", StringComparison.Ordinal) + 1);
        var second = At(snapshot, Text.LastIndexOf(".read", StringComparison.Ordinal) + 1);
        Assert.Contains("A<i32>.read", first.Use);
        Assert.Contains("A<string>.read", second.Use);
        Assert.False(new HoverAgreement().Equal(new(default, first), new(default, second with { Use = first.Use })));
    }

    [Fact]
    public void ContractEditsRevokeBoundsWithoutMutatingThePreviousSnapshot()
    {
        var c = Create(Source);
        var old = HoverBuilder.Create(c);
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        var donor = Create(Source.Replace("        effect confined\n", string.Empty, StringComparison.Ordinal));
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsVirtual);
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        c.Bind();
        var current = HoverBuilder.Create(c);
        var offset = Source.IndexOf("self.read", StringComparison.Ordinal) + 5;
        Assert.Contains("Available bound: confined", At(old, offset).Effects);
        Assert.Contains("Available bound: none", At(current, offset).Effects);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void CachedVirtualFactsReleaseTheCompilerAndAllocateNothingOnRepeatedLookup()
    {
        var (snapshot, compilation) = Detached();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(compilation.TryGetTarget(out _));
        var document = snapshot.Documents[SourceIdentity.FromPath("main.kimi")];
        var state = new HoverState([new(new(SourceIdentity.FromPath("main.kimi"), UnitKind.Product, "test"), 1, document)], false, HoverPlacement.Canonical);
        using var text = new TextDocument(Source);
        var position = document.Source.GetSourceRange(new(Source.IndexOf("base.read", StringComparison.Ordinal) + 5, 4)).Start;
        Assert.Contains("Implementation: B.read", state.Find(text, position).Body);
        Assert.Equal(0, AllocationMeasurement.Measure(() => state.Find(text, position)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (HoverSnapshot Snapshot, WeakReference<Compilation> Compilation) Detached()
    {
        var c = Create(Source);
        return (HoverBuilder.Create(c), new(c));
    }

    private static Compilation Create(string text)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.CollectHover = true;
        c.CollectDocumentation = true;
        c.Kotonoha.AddSource(new("main.kimi", text));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        return c;
    }

    private static HoverInfo At(HoverSnapshot snapshot, int offset)
    {
        var document = snapshot.Documents[SourceIdentity.FromPath("main.kimi")];
        var index = document.Find(offset);
        Assert.True(index >= 0, $"Missing Hover at {offset}");
        return document.Entries[index].Info;
    }
}
