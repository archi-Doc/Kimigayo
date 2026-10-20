// Desired regressions of PLAN G170 / G172, written in their owner AssociatedInferenceTest at dev 8b2350aa (BU, unit C0, 2026-10-21).
// They FAIL at that head (desired-regressions-at-head.txt) and are kept as data, never landed failing (PlanB 6).
// The members below are the added lines of desired-regressions.patch, hunk by hunk; apply the patch to restore them.


    // G172 (SPEC 8.5.5's example): PB.W is i32 from PA.Z, a value PA determines independently of its own inferred values, and PA.X is
    // i64 from PB.Y; neither declaration order nor requirement order decides the outcome.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void IndependentValuesOfAnInProgressDefinitionSupplyEvidence(bool reverseDeclarations, bool reverseP, bool reverseQ)
    {
        var p = "contract P\n    associate X\n    associate Z\n" + Pair("    func x(self: ref/Self) -> Self.X\n", "    func z(self: ref/Self) -> Self.Z\n", reverseP);
        var q = "contract Q\n    associate Y\n    associate W\n" + Pair("    func y(self: ref/Self) -> Self.Y\n", "    func w(self: ref/Self) -> Self.W\n", reverseQ);
        const string pa = "struct PA\n    Self is P\n    public func x(self: ref/Self) -> PB.(Q).Y => 0\n    public func z(self: ref/Self) -> i32 => 0\n";
        const string pb = "struct PB\n    Self is Q\n    public func y(self: ref/Self) -> i64 => 0\n    public func w(self: ref/Self) -> PA.(P).Z => 0\n";
        var c = Bound(p + q + Pair(pa, pb, reverseDeclarations));
        Assert.Equal("i64", Value(c, "PA", "P", "X"));
        Assert.Equal("i32", Value(c, "PA", "P", "Z"));
        Assert.Equal("i64", Value(c, "PB", "Q", "Y"));
        Assert.Equal("i32", Value(c, "PB", "Q", "W"));
    }

    // G172: D.X is i32 from `a`. `b`'s result E.W depends on D.X, a value inferred in D, so it supplies D no evidence and meets only the
    // ordinary result check; E.W is i32 from the determined D.X. Neither declaration nor requirement order decides the outcome.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EvidenceOnTheDefinitionsOwnValueIsExcludedWhileIndependentEvidenceCompletes(bool reverseDeclarations, bool reverseRequirements)
    {
        var p = "contract P\n    associate X\n" + Pair("    func a(self: ref/Self) -> Self.X\n", "    func b(self: ref/Self) -> Self.X\n", reverseRequirements);
        const string q = "contract Q\n    associate W\n    func w(self: ref/Self) -> Self.W\n";
        const string d = "struct D\n    Self is P\n    public func a(self: ref/Self) -> i32 => 0\n    public func b(self: ref/Self) -> E.(Q).W => 0\n";
        const string e = "struct E\n    Self is Q\n    public func w(self: ref/Self) -> D.(P).X => 0\n";
        var c = Bound(p + q + Pair(d, e, reverseDeclarations));
        Assert.Equal("i32", Value(c, "D", "P", "X"));
        Assert.Equal("i32", Value(c, "E", "Q", "W"));
    }

    // G172 (PlanB 1): A.Z is i32, B.Y comes from A.Z and A.X's only evidence, B.Y, depends on A.Z, a value inferred in A, so A.X cannot be
    // inferred. Both declaration orders give the same outcome. (B's outcome is G172's to settle: by SPEC 8.5.5 B.Y is i32, not asserted.)
    [Fact]
    public void EvidenceThroughAnotherDefinitionOnTheDefinitionsOwnValueIsExcluded()
    {
        const string contracts = "contract P\n    associate X\n    associate Z\n    func x(self: ref/Self) -> Self.X\n    func z(self: ref/Self) -> Self.Z\ncontract Q\n    associate Y\n    func y(self: ref/Self) -> Self.Y\n";
        const string a = "struct A\n    Self is P\n    public func x(self: ref/Self) -> B.(Q).Y => 0\n    public func z(self: ref/Self) -> i32 => 0\n";
        const string b = "struct B\n    Self is Q\n    public func y(self: ref/Self) -> A.(P).Z => 0\n";
        var outcomes = new List<string>();
        foreach (var reverse in new[] { false, true })
        {
            var c = CompilationTestHelper.ParseSuccess(contracts + Pair(a, b, reverse), "associated-inference.kimi");
            Assert.False(c.Bind().IsComplete);
            Assert.False(Conformance(c, "A", "P").IsVerified);
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.AssociatedTypeInferenceFailed_Kd && x.Node.ToString()!.Contains("Self is P", StringComparison.Ordinal));
            var conformance = Conformance(c, "B", "Q");
            outcomes.Add($"B verified {conformance.IsVerified}, B.Y {(conformance.IsVerified ? conformance.AssociatedTypes.Single().Value.Name : "-")}; " +
                string.Join(", ", c.Binding.Issues.Select(x => x.Code).Order()));
        }

        Assert.Equal(outcomes[0], outcomes[1]);
    }



    private static string Pair(string first, string second, bool reverse) => reverse ? second + first : first + second;

    private static BoundConformance Conformance(Compilation c, string typeName, string contractName)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == typeName);
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == contractName);
        return c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!;
    }

    private static string Value(Compilation c, string typeName, string contractName, string name)
        => Conformance(c, typeName, contractName).AssociatedTypes.Single(x => x.Key.Symbol.Name == name).Value.Name;
