// Inputs only. expected.txt is a separately handwritten complete output oracle; no bless/update mode exists.
namespace RecordsModel;

sealed record Scenario(string Name, string Meaning, Domain Domain, Input[] Rows);

sealed class Fixtures
{
    public Domain Domain { get; } = Domain.Fixture();
    public Id<T> Id<T>(T value) where T : struct, Enum => Domain.Id(value);
    public Finite Borrow(BorrowName name = BorrowName.A) => new Finite.Borrow(Id(name));
    public Finite Guard(GuardName name = GuardName.A) => new Finite.Guard(Id(name));
    public Fact Site(Role role, SiteName site) => new Fact.Site(role, Id(site));
    public Fact Cover(Coverage rule, IssueRef target) => new Fact.CoveredBy(rule, target);
    public EffectKey Key(FieldName field, AccessKind access) => new EffectKey.Static(Id(field), access);
    public Reason Fit() => new Reason.Origin(Relation.Outlives, new End.Expression(Id(OriginValue.A), Notation.Qualified),
        new End.Expression(Id(OriginValue.B), Notation.Qualified), new OriginSource.Fit(new FitDestination.Type(Id(TypeValue.RefA))));
    public IssueRef Origin(ObligationName name, Finite chain) => new(Domain.Body(),
        name == ObligationName.Unknown ? Code.OriginUnknown : Code.OriginRefuted, Id(SiteName.Exit), new Issue.Origin(Id(name), chain));
    public IssueRef Conflict(ConflictKind kind, Finite cause, PointName point = PointName.Return, AccessName access = AccessName.First)
        => new(Domain.Body(), kind switch { ConflictKind.DefaultBorrow => Code.DefaultBorrow, ConflictKind.DefaultAccess => Code.DefaultAccess, _ => Code.Loan },
            Id(point switch { PointName.Cleanup => SiteName.Cleanup, PointName.Earlier => SiteName.Earlier, PointName.Call => SiteName.Call, _ => SiteName.Exit }),
            new Issue.Conflict(Id(point), Id(access), cause, kind));
    public Input Row(IssueRef reference, ulong mask, params Fact[] facts) => new(reference, Requirement.Ownership,
        reference.Issue is Issue.Origin ? Fit() : new Reason.Empty(), Assessment.Checked, new Copy.Outside(), new(facts), mask);
    public Scenario Case(string name, string meaning, params Input[] rows) => new(name, meaning, Domain, rows);

    public IEnumerable<Scenario> All()
    {
        var oa = Origin(ObligationName.ReturnA, Borrow());
        var ob = Origin(ObligationName.ReturnA, Borrow(BorrowName.B));
        var wa = Site(Role.Use, SiteName.WitnessA);
        var wb = Site(Role.Use, SiteName.WitnessB);
        yield return Case("duplicates", "Same full rows OR masks; different facts and finite chains remain.",
            Row(oa, 1, wa), Row(oa, 4, wa, wa), Row(oa, 2, wb), Row(ob, 2, wa));

        var independent = Origin(ObligationName.Independent, Borrow());
        var exprA = new End.Expression(Id(OriginValue.A), Notation.Qualified);
        var exprB = new End.Expression(Id(OriginValue.B), Notation.Qualified);
        var clause = Site(Role.Clause, SiteName.Clause);
        var fixedBy = Site(Role.FixedBy, SiteName.Fixed);
        yield return Case("reasons", "Same endpoints retain equality/outlives, fit/declared/wellFormed, Type/OriginOnly and complete end recipes.",
            Row(oa, 1, clause, fixedBy),
            Row(oa, 2, clause) with { Reason = new Reason.Origin(Relation.Equal, exprA, exprB, new OriginSource.Fit(new FitDestination.Type(Id(TypeValue.RefB)))) },
            Row(independent, 1, clause) with { Reason = new Reason.Origin(Relation.Equal, exprA, exprB, new OriginSource.Declared()) },
            Row(independent, 2, fixedBy) with { Reason = new Reason.Origin(Relation.Outlives, exprA, exprB, new OriginSource.WellFormed()) },
            Row(oa, 4, Site(Role.Origin, SiteName.TypeSite)) with { Reason = new Reason.Origin(Relation.Outlives, exprA, new End.Omitted(Id(OriginValue.C), Id(SiteName.TypeSite)), new OriginSource.Fit(new FitDestination.OriginOnly())) },
            Row(oa, 8, Site(Role.Origin, SiteName.Header)) with { Reason = new Reason.Origin(Relation.Outlives, new End.Closure(Id(OriginValue.A), Id(SiteName.Header), ClosurePart.CallReceiver), new End.Closure(Id(OriginValue.B), Id(SiteName.Header), ClosurePart.CallResult), new OriginSource.Fit(new FitDestination.OriginOnly())) });

        Input Effect(EffectKey key, ulong mask, Fact witness) => Row(new(Domain.Body(), Code.Loan, Id(SiteName.Call), new Issue.Effect(Id(PointName.Call), Id(BorrowName.A), key)), mask,
            new Fact.Effect(key, Id(SiteName.Call)), witness);
        yield return Case("effects", "Actual Field/read/write/Unknown and paired witnesses survive per view beside same-code same-site destruction.",
            Effect(Key(FieldName.X, AccessKind.Read), 1, wa), Effect(Key(FieldName.X, AccessKind.Write), 2, wa),
            Effect(Key(FieldName.Y, AccessKind.Write), 2, wb), Effect(new EffectKey.Unknown(), 2, wb),
            Effect(Key(FieldName.X, AccessKind.Read), 4, wb),
            Row(Conflict(ConflictKind.Destruction, Borrow(), PointName.Call), 4, Site(Role.Reservation, SiteName.WitnessA)));

        foreach (var state in new[] { "checked", "held", "limited", "copied", "missing" })
        {
            var cleanup = Conflict(ConflictKind.GuardEnd, Guard(), PointName.Cleanup);
            var preferred = Row(cleanup, 1, wa);
            if (state == "held") preferred = preferred with { Assessment = Assessment.Held };
            if (state == "copied") preferred = preferred with { Copy = new Copy.Inlined(1, Id(SiteName.Call)) };
            if (state == "limited") preferred = Row(cleanup with { Code = Code.Unsupported }, 1) with { Assessment = Assessment.Limit, Copy = new Copy.Inlined(1, Id(SiteName.Call)) };
            var rows = new List<Input>
            {
                Row(Origin(ObligationName.GuardA, Guard()), 3, Cover(Coverage.GuardReverse, preferred.Ref), wa),
                Row(Origin(ObligationName.GuardB, Guard(GuardName.B)), 3, wb),
                Row(Conflict(ConflictKind.Access, Borrow(), PointName.Earlier), 3, wa),
            };
            if (state != "missing") rows.Add(preferred);
            yield return Case($"guard-{state}", "Proposed GuardReverse: only the exact surviving checked cleanup covers; independent guard/write remain.", rows.ToArray());
        }
        yield return Case("guard-unknown", "Unknown is independent and has no coverage edge.",
            Row(Origin(ObligationName.Unknown, Guard()), 3, wa), Row(Conflict(ConflictKind.GuardEnd, Guard(), PointName.Cleanup), 1, wa));

        var defaultBorrow = Conflict(ConflictKind.DefaultBorrow, Borrow());
        var storage = Conflict(ConflictKind.StorageEnd, Borrow());
        var write = Conflict(ConflictKind.Access, Borrow(), PointName.Earlier);
        yield return Case("default-chain", "DefaultBorrow removes Origin bit 0; that removed Origin cannot cover storage-end bit 0. Other chain/boundary/write remain.",
            Row(defaultBorrow, 1, wa), Row(oa, 3, Cover(Coverage.DefaultOrigin, defaultBorrow), wa),
            Row(storage, 3, Cover(Coverage.ReturnStorageEnd, oa), wa), Row(write, 3, wa), Row(ob, 3, wb),
            Row(Origin(ObligationName.SecondBoundary, Borrow()), 3, wb));

        var secondRelation = Origin(ObligationName.ReturnB, Borrow());
        yield return Case("explicit-multichain", "Two exact relation/chain rows explicitly name one preferred issue; no fan-out to another chain.",
            Row(defaultBorrow, 1, wa), Row(oa, 3, Cover(Coverage.DefaultOrigin, defaultBorrow), wa),
            Row(secondRelation, 3, Cover(Coverage.DefaultOrigin, defaultBorrow), wb),
            Row(storage, 3, Cover(Coverage.ReturnStorageEnd, oa), Cover(Coverage.ReturnStorageEnd, secondRelation), wa), Row(ob, 3, wb));

        var defaultSecond = Conflict(ConflictKind.DefaultBorrow, Borrow(), PointName.Return2);
        yield return Case("boundaries", "Two default boundaries of one Borrow are distinct, and disjoint masks suppress nothing.",
            Row(defaultBorrow, 1, wa), Row(defaultSecond, 2, wb), Row(oa, 2, Cover(Coverage.DefaultOrigin, defaultBorrow), wa),
            Row(Origin(ObligationName.SecondBoundary, Borrow()), 3, Cover(Coverage.DefaultOrigin, defaultSecond), wb), Row(write, 3, wa));

        yield return Case("default-access", "Exact DefaultAccess subordinate loses one view; an independent mutation is retained.",
            Row(defaultBorrow, 1, wa), Row(Conflict(ConflictKind.DefaultAccess, Borrow()), 3, Cover(Coverage.DefaultAccess, defaultBorrow), wa), Row(write, 3, wa));

        foreach (var state in new[] { "held", "limited", "copied", "missing" })
        {
            var preferred = Row(defaultBorrow, 1, wa);
            if (state == "held") preferred = preferred with { Assessment = Assessment.Held };
            if (state == "copied") preferred = preferred with { Copy = new Copy.Inlined(1, Id(SiteName.Call)), Assessment = Assessment.Held };
            if (state == "limited") preferred = Row(defaultBorrow with { Code = Code.Unsupported }, 1) with { Assessment = Assessment.Limit, Copy = new Copy.Inlined(1, Id(SiteName.Call)) };
            var rows = new List<Input> { Row(oa, 3, Cover(Coverage.DefaultOrigin, preferred.Ref), wa), Row(storage, 3, Cover(Coverage.ReturnStorageEnd, oa), wa), Row(write, 3, wa) };
            if (state != "missing") rows.Add(preferred);
            yield return Case($"default-{state}", "A non-surviving preferred default does not cover Origin; the surviving Origin may cover storage-end.", rows.ToArray());
        }

        yield return Case("return-exact", "Return-fit covers only its carried Borrow storage-end; another Loan, inner use, parameter store and write remain.",
            Row(oa, 1, wa), Row(storage, 3, Cover(Coverage.ReturnStorageEnd, oa), wa),
            Row(Conflict(ConflictKind.StorageEnd, Borrow(BorrowName.B)), 3, wb), Row(write, 3, wa),
            Row(Conflict(ConflictKind.Access, Borrow(), PointName.Call), 3, Site(Role.Use, SiteName.WitnessB)),
            Row(Conflict(ConflictKind.Access, Borrow(), PointName.Call, AccessName.Second), 3, Site(Role.Store, SiteName.WitnessB)));

        var source = new IssueRef(Domain.Body(), Code.Source, Id(SiteName.Call), new Issue.Source(0));
        yield return Case("body-cases", "Body zero, a different universe with the same raw mask, and NoBody publish independently.",
            Row(source, 3, wa), Row(source with { Body = Domain.Body(BodyName.Other) }, 3, wa), Row(source with { Body = Body.None }, 1, wa));
        yield return Case("same-span", "Same source/root and different semantic issue positions are retained; duplicate issue merges masks.",
            Row(source with { Issue = new Issue.Source(1) }, 1, wa), Row(source with { Issue = new Issue.Source(1) }, 2, wa),
            Row(source with { Issue = new Issue.Source(2) }, 1, wb));
        yield return Case("borrow-recipes", "Different private finite identities may share a public Reason while omitted and Borrow recipes remain different.",
            Row(oa, 1, wa) with { Reason = new Reason.Origin(Relation.Outlives, new End.Borrow(Id(OriginValue.A), Borrow(), Id(SiteName.TypeSite)), exprB, new OriginSource.Fit(new FitDestination.Type(Id(TypeValue.RefA)))) },
            Row(ob, 2, wb) with { Reason = new Reason.Origin(Relation.Outlives, new End.Borrow(Id(OriginValue.A), Borrow(BorrowName.B), Id(SiteName.TypeSite)), exprB, new OriginSource.Fit(new FitDestination.Type(Id(TypeValue.RefA)))) },
            Row(oa, 4, wa) with { Reason = new Reason.Origin(Relation.Outlives, new End.Omitted(Id(OriginValue.A), Id(SiteName.TypeSite)), exprB, new OriginSource.Fit(new FitDestination.Type(Id(TypeValue.RefA)))) });

        var unsupported = source with { Code = Code.Unsupported };
        var copied = new Copy.Inlined(1, Id(SiteName.Call));
        yield return Case("category-copy", "Copied Proof and Checked Unsupported retain their category; known Language drops; valid Limit and NoBody Unsupported stay location-only.",
            Row(Origin(ObligationName.Unknown, Guard()), 3, wa) with { Copy = copied },
            Row(unsupported, 1) with { Copy = copied }, Row(unsupported, 2),
            Row(source with { Issue = new Issue.Source(1) }, 3, wb) with { Copy = copied },
            Row(unsupported with { Issue = new Issue.Source(2) }, 4) with { Assessment = Assessment.Limit, Copy = copied },
            Row(unsupported with { Body = Body.None }, 1));
        var unsupportedCleanup = Conflict(ConflictKind.GuardEnd, Guard(), PointName.Cleanup) with { Code = Code.Unsupported };
        yield return Case("category-cover", "Checked Unsupported cannot contribute a cover mask; its independent Origin and earlier write survive.",
            Row(unsupportedCleanup, 1),
            Row(Origin(ObligationName.GuardA, Guard()), 3, wa, Cover(Coverage.GuardReverse, unsupportedCleanup)),
            Row(write, 3, wa));
    }
}
