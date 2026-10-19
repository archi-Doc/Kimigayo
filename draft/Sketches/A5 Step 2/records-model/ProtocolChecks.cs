namespace RecordsModel;

static class ProtocolChecks
{
    public static (string[] Lines, int Failures) Run()
    {
        var log = new List<string>();
        var failures = 0;
        var fixture = new Fixtures();
        var domain = fixture.Domain;
        var input = fixture.All().First().Rows[0];
        void Faulted(string name, Fault expected, Action action)
        {
            Fault? actual = null;
            try { action(); } catch (ProtocolFault fault) { actual = fault.Fault; }
            log.Add($"{name}: expected={expected}; actual={actual?.ToString() ?? "none"}");
            if (actual != expected) failures++;
        }
        void Pass(string name, bool condition)
        {
            log.Add($"{name}: expected=true; actual={condition.ToString().ToLowerInvariant()}");
            if (!condition) failures++;
        }
        Report Collecting()
        {
            var report = new Report(); report.Begin(domain); return report;
        }
        Faulted("read before Begin", Fault.Lifecycle, () => _ = new Report().All);
        Faulted("Record before Begin", Fault.Lifecycle, () => new Report().Record(input));
        Faulted("Complete before Begin", Fault.Lifecycle, () => new Report().Complete());
        Faulted("Begin while collecting", Fault.Lifecycle, () => Collecting().Begin(domain));
        Faulted("read while collecting", Fault.Lifecycle, () => _ = Collecting().All);
        var completed = Collecting(); completed.Record(input); completed.Complete();
        Faulted("double Complete", Fault.Lifecycle, completed.Complete);
        Faulted("Record after Complete", Fault.Lifecycle, () => completed.Record(input));
        Faulted("zero mask", Fault.Mask, () => Collecting().Record(input with { Mask = 0 }));
        Faulted("out of universe mask", Fault.Mask, () => Collecting().Record(input with { Mask = 16 }));
        var bodyless = fixture.All().Single(c => c.Name == "body-cases").Rows[2];
        Faulted("NoBody is not body zero", Fault.Mask, () => Collecting().Record(bodyless with { Mask = 3 }));
        Faulted("stale source identity", Fault.Domain, () => Collecting().Record(input with { Ref = input.Ref with { Subject = new(2, SiteName.Exit) } }));
        Faulted("out of domain symbol", Fault.Domain, () => Collecting().Record(input with { Facts = new([new Fact.Effect(new EffectKey.Static(new(1, (FieldName)99), AccessKind.Read), fixture.Id(SiteName.Call))]) }));
        Faulted("Borrow in wrong body domain", Fault.Domain, () => Collecting().Record(input with { Ref = input.Ref with { Body = domain.Body(BodyName.Other) } }));
        Faulted("requirement disagreement", Fault.Requirement, () => { var r = Collecting(); r.Record(input); r.Record(input with { Requirement = Requirement.Other }); });
        var guard = fixture.Origin(ObligationName.GuardA, fixture.Guard());
        var cleanup = fixture.Conflict(ConflictKind.GuardEnd, fixture.Guard(), PointName.Cleanup);
        Faulted("coverage wrong target kind", Fault.Pair, () => Collecting().Record(fixture.Row(guard, 1, fixture.Cover(Coverage.GuardReverse, fixture.Conflict(ConflictKind.Access, fixture.Guard(), PointName.Cleanup)))));
        Faulted("coverage wrong finite identity", Fault.Pair, () => Collecting().Record(fixture.Row(guard, 1, fixture.Cover(Coverage.GuardReverse, fixture.Conflict(ConflictKind.GuardEnd, fixture.Guard(GuardName.B), PointName.Cleanup)))));
        Faulted("coverage wrong body", Fault.Domain, () => Collecting().Record(fixture.Row(guard, 1, fixture.Cover(Coverage.GuardReverse, cleanup with { Body = domain.Body(BodyName.Other) }))));
        Faulted("Unknown cannot carry GuardReverse", Fault.Pair, () => Collecting().Record(fixture.Row(fixture.Origin(ObligationName.Unknown, fixture.Guard()), 1, fixture.Cover(Coverage.GuardReverse, cleanup))));
        Faulted("Proof cannot be a failure row", Fault.Outcome, () => Collecting().Record(fixture.Row(fixture.Origin(ObligationName.Proof, fixture.Guard()), 1)));
        var defaultBorrow = fixture.Conflict(ConflictKind.DefaultBorrow, fixture.Borrow());
        Faulted("DefaultOrigin wrong boundary", Fault.Pair, () => Collecting().Record(fixture.Row(fixture.Origin(ObligationName.SecondBoundary, fixture.Borrow()), 1, fixture.Cover(Coverage.DefaultOrigin, defaultBorrow))));
        Faulted("DefaultOrigin wrong Borrow", Fault.Pair, () => Collecting().Record(fixture.Row(fixture.Origin(ObligationName.ReturnA, fixture.Borrow(BorrowName.B)), 1, fixture.Cover(Coverage.DefaultOrigin, defaultBorrow))));
        Faulted("DefaultAccess wrong access occurrence", Fault.Pair, () => Collecting().Record(fixture.Row(fixture.Conflict(ConflictKind.DefaultAccess, fixture.Borrow(), access: AccessName.Second), 1, fixture.Cover(Coverage.DefaultAccess, defaultBorrow))));
        Faulted("Origin identity cannot name two positions", Fault.Position, () => domain.Declare(ObligationName.ReturnA, PointName.Return2, AccessName.First, Outcome.Refuted, true));
        var oldHandle = completed.HandleAt(0);
        completed.Begin(Domain.Fixture(2)); completed.Complete();
        Faulted("reset invalidates row handle", Fault.Handle, () => completed.Read(oldHandle));
        Pass("reset clears prior rows and indexes", completed.All.Count == 0 && completed.CapturedCount == 0);
        var next = new Report(); next.Begin(Domain.Fixture(2));
        Faulted("reset rejects old typed IDs", Fault.Domain, () => next.Record(input));
        next.Complete();
        var driver = new Driver();
        var first = driver.Run(domain, false, r => r.Record(input));
        var again = driver.Run(domain, false, _ => throw new Exception("cached driver reran inference"));
        Pass("same version returns completed report", ReferenceEquals(first, again) && again.All.Count == 1 && driver.Evaluations == 1);
        var rejected = driver.Run(Domain.Fixture(3), true, _ => throw new Exception("validator-rejected driver ran analysis"));
        Pass("validator rejected completes empty report", rejected.All.Count == 0 && driver.Evaluations == 1);
        var empty = Collecting(); empty.Complete();
        Pass("empty Complete and publication", empty.All.Count == 0 && empty.Publish().Length == 0);
        var source = new IssueRef(domain.Body(), Code.Source, fixture.Id(SiteName.Call), new Issue.Source(0));
        var unsupported = source with { Code = Code.Unsupported };
        var locationOnly = fixture.Row(unsupported, 1);
        var witness = fixture.Site(Role.Use, SiteName.WitnessA);
        Faulted("Checked Unsupported rejects facts", Fault.Pair, () => Collecting().Record(locationOnly with { Facts = new([witness]) }));
        Faulted("Checked Unsupported rejects Reason", Fault.Pair, () => Collecting().Record(locationOnly with { Reason = fixture.Fit() }));
        Faulted("Held rejects Proof category", Fault.Pair, () => Collecting().Record(fixture.Row(fixture.Origin(ObligationName.Unknown, fixture.Guard()), 1, witness) with { Assessment = Assessment.Held }));
        Faulted("Held rejects Unsupported category", Fault.Pair, () => Collecting().Record(locationOnly with { Assessment = Assessment.Held }));
        Faulted("Held rejects Origin Language", Fault.Pair, () => Collecting().Record(input with { Assessment = Assessment.Held }));
        Faulted("Held rejects Source Other requirement", Fault.Pair, () => Collecting().Record(fixture.Row(source, 1, witness) with { Assessment = Assessment.Held, Requirement = Requirement.Other }));
        Faulted("Held rejects NoBody", Fault.Pair, () => Collecting().Record(fixture.Row(source with { Body = Body.None }, 1) with { Assessment = Assessment.Held }));
        Faulted("Limit rejects facts", Fault.Pair, () => Collecting().Record(locationOnly with { Assessment = Assessment.Limit, Facts = new([witness]) }));
        Faulted("Limit rejects Reason", Fault.Pair, () => Collecting().Record(locationOnly with { Assessment = Assessment.Limit, Reason = fixture.Fit() }));
        // Preserve the first candidate's malformed Limited preparations as explicit negative inputs.
        Faulted("old limited cleanup witness input", Fault.Pair, () => Collecting().Record(fixture.Row(cleanup with { Code = Code.Unsupported }, 1, witness) with { Assessment = Assessment.Limit, Copy = new Copy.Inlined(1, fixture.Id(SiteName.Call)) }));
        Faulted("old limited default witness input", Fault.Pair, () => Collecting().Record(fixture.Row(defaultBorrow with { Code = Code.Unsupported }, 1, witness) with { Assessment = Assessment.Limit, Copy = new Copy.Inlined(1, fixture.Id(SiteName.Call)) }));
        Faulted("Inlined rejects NoBody", Fault.Pair, () => Collecting().Record(fixture.Row(source with { Body = Body.None }, 1) with { Copy = new Copy.Inlined(1, fixture.Id(SiteName.Call)) }));
        Faulted("undefined code rejected", Fault.Domain, () => Collecting().Record(locationOnly with { Ref = unsupported with { Code = (Code)99 } }));
        Faulted("Proof cannot be a cover target", Fault.Pair, () => Collecting().Record(fixture.Row(guard, 1, fixture.Cover(Coverage.GuardReverse, fixture.Origin(ObligationName.Unknown, fixture.Guard())))));
        return (log.ToArray(), failures);
    }
}
