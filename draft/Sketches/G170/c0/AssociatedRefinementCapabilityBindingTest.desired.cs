// Desired regressions of PLAN G170 / G172, written in their owner AssociatedRefinementCapabilityBindingTest at dev 8b2350aa (BU, unit C0, 2026-10-21).
// They FAIL at that head (desired-regressions-at-head.txt) and are kept as data, never landed failing (PlanB 6).
// The members below are the added lines of desired-regressions.patch, hunk by hunk; apply the patch to restore them.


    private const string Trees = "public contract Tree\n    associate Child is Tree\npublic contract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";


    // G170 (SPEC 8.3.2): the input T is CopyTree supplies T.Child is CopyTree, then T.Child.Child is CopyTree and so on, each Copy by
    // refinement. Proving the same Type in the same scope depends neither on an unused parameter spelling the projection through the
    // stronger CopyTree nor on whether it precedes or follows the queried one; the effect premises (TyCtxt.Copy) give the same answer.
    [Theory]
    [InlineData(1, "")]
    [InlineData(1, "before")]
    [InlineData(1, "after")]
    [InlineData(2, "")]
    [InlineData(2, "before")]
    [InlineData(2, "after")]
    [InlineData(4, "")]
    [InlineData(4, "before")]
    [InlineData(4, "after")]
    [InlineData(8, "")]
    [InlineData(8, "before")]
    [InlineData(8, "after")]
    public void RecursiveRefinementPremisesProveDeepProjectionsWhateverTheSpelling(int depth, string hint)
    {
        var value = "value: T" + string.Concat(Enumerable.Repeat(".(Tree).Child", depth));
        var stronger = "hint: ref/T" + string.Concat(Enumerable.Repeat(".(CopyTree).Child", depth));
        var parameters = hint switch { "before" => stronger + ", " + value, "after" => value + ", " + stronger, _ => value };
        var c = MinimalEmissionTest.Analyze(Trees + "group G\n    func inspect<T>(" + parameters + ")\n        T is CopyTree\n        ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete);
        var function = Function(c);
        var type = function.Parameters.Single(x => x.InternalName == "value").Type.TypeOf()!;
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(type, function));
        Assert.True(c.Types.Copy(function.SymbolOf(), type));
    }

    // G170: a Type no source spells, as A4 generates by substitution, has the same answer as a spelled one.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void RecursiveRefinementPremisesProveGeneratedProjections(int depth)
    {
        var c = MinimalEmissionTest.Analyze(Trees + "group G\n    func inspect<T>(value: T.(Tree).Child)\n        T is CopyTree\n        ()");
        Assert.True(c.Binding.Result.IsComplete);
        var function = Function(c);
        var child = function.Parameters[0].Type.TypeOf()!;
        var type = child;
        for (var i = 1; i < depth; i++)
        {
            type = c.Types.Intern(BoundTypeKind.AssociatedProjection, child.Symbol, SemanticsKind.Owner, [type, child.Components[1]]);
        }

        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(type, function));
        Assert.True(c.Types.Copy(function.SymbolOf(), type));
    }

    // G170 through a non-recursive Contract: CopyOuter's clause supplies T.Inner is CopyHolder, whose clause makes T.Inner.Item Copy.
    [Theory]
    [InlineData("")]
    [InlineData("before")]
    [InlineData("after")]
    public void NestedRefinementPremisesProveTheProjectionWhateverTheSpelling(string hint)
    {
        const string value = "value: T.(Outer).Inner.(Holder).Item";
        const string stronger = "hint: ref/T.(Outer).Inner.(CopyHolder).Item";
        var parameters = hint switch { "before" => stronger + ", " + value, "after" => value + ", " + stronger, _ => value };
        var c = MinimalEmissionTest.Analyze("public contract Holder\n    associate Item\npublic contract CopyHolder: Holder\n    Self.(Holder).Item is Copy\n" +
            "public contract Outer\n    associate Inner is Holder\npublic contract CopyOuter: Outer\n    Self.(Outer).Inner is CopyHolder\n" +
            "group G\n    func inspect<T>(" + parameters + ")\n        T is CopyOuter\n        ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        var function = Function(c);
        var type = function.Parameters.Single(x => x.InternalName == "value").Type.TypeOf()!;
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(type, function));
        Assert.True(c.Types.Copy(function.SymbolOf(), type));
    }
