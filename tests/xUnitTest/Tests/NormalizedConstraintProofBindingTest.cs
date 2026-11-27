// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class NormalizedConstraintProofBindingTest
{
    private const string Head = "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n";

    private const string Takes = "group G\n    func take<T>()\n        T is Copy\n        ()\n";

    private const string Trees = "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n" +
        "contract K\n    associate X\n    Self.(K).X is i32\n";

    // R7-C2e2-1's valid twins: `Moved`, a projection's body use, whose fill writes its scope's premises between requests (the view's
    // entry is rebuilt at the new serial); `Derived`, a view whose identity table a derived identity fact keeps Provisional (`T is K`
    // supplies `T.(K).X is i32`): its class keys are not final, so the entry stays the request's.
    private const string Moved = "func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n    T is CopyTree\n" +
        "    let x: T.(Tree).Child = value\n    let y = (x, x)\n    return (x, x)\n";

    private const string Validated = "contract K<X>\n    associate E\nfunc f<T, U, V>(t: T, u: U, v: V)\n    T is K<U>\n    T is K<V>\n    T is K<i32>\n" +
        "    T.(K<U>).E is i32\n    T.(K<V>).E is i32\n    T.(K<i32>).E is i32\n    ()\n" +
        "contract P<E>\n    func read(self: ref/Self, value: ref/E) -> i32\ncontract Left: P<i32>\ncontract Right<U>: P<U>\n" +
        "struct S<U, T>\n    Self is Left when T is i32\n    Self is Right<U> when T is string\n" +
        "    public func read(self: ref/Self, value: ref/i32) -> i32 => 1\n    public func read(self: ref/Self, value: ref/U) -> i32 => 2\n";

    private const string Derived = "func both<T, U>(a: U.(Tree).Child, b: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child, U.(Tree).Child, " +
        "U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n    T is U\n    T is CopyTree\n    T is K\n    return (a, a, b, b)\n";

    // DECISIONS R7-C2e2-1: Closure(view) forms a view's index once per view value, not once per request. n Copy requests in one view of
    // n written premises read one entry, so each added premise adds a fact to each of a few views' entries; a per-request index walks the
    // n facts once per request (4,800 more facts from 8 to 16 premises). No projection: a fill moves the view at each use.
    [Fact]
    public void AViewsIndexIsFormedOncePerViewNotPerRequest()
    {
        var (small, large) = (Work(8), Work(16));
        Assert.True(large - small <= 16 * 8, $"{small} at 8, {large} at 16");

        static int Work(int n)
        {
            var range = Enumerable.Range(0, n);
            var parameters = string.Join(", ", range.Select(static i => $"T{i}"));
            var values = string.Join(", ", range.Select(static i => $"v{i}: T{i}"));
            var results = string.Join(", ", range.Select(static i => $"T{i}, T{i}"));
            var premises = string.Concat(range.Select(static i => $"    T{i} is CopyTree\n"));
            var copies = string.Join(", ", range.Select(static i => $"v{i}, v{i}"));
            var c = MinimalEmissionTest.Analyze(Trees + $"func f<{parameters}>({values}) -> ({results})\n{premises}    return ({copies})\n" +
                "public func main() -> ()\n    return\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.ClosureIndexWork;
        }
    }

    // DECISIONS R7-C2e2-1: an entry is its view's at the serial it was formed at (Moved), and Kept only when its class keys are final
    // (Derived: a Provisional identity table; the readings' admission is x1's count row below). Each with its invalid twin: `T is Tree`
    // supplies no Copy.
    [Theory]
    [InlineData(Moved, "clean")]
    [InlineData(Derived, "clean")]
    [InlineData(Moved, "invalid")]
    [InlineData(Derived, "invalid")]
    public void AViewsIndexIsTheViewsAtItsSerialAndCompletion(string function, string records)
    {
        var twin = records == "clean" ? function : function.Replace("T is CopyTree", "T is Tree", StringComparison.Ordinal);
        Assert.Equal(records, DiagnosticCorpus.RecordProfile(Trees + twin + "public func main() => ()\n") == "clean" ? "clean" : "invalid");
    }

    // DECISIONS R7-C2e2-1: the pairs the entries index for Moved (the fill moves its view: the entry is formed again at the new serial)
    // and Derived (an identity view with the derived identity fact of `T is K`: K's state is Proven, so the fact's availability is
    // Complete (W6, R7-C2e2 e2-3a), the table Final and the entry Kept; 1,416 while W6 kept every derived fact Provisional and each
    // request formed the entry again), less the library's: exact, over the publications that form the entries again. Every row counts.
    // PLAN G949 S1-1c (dev: the certificate rounds judge the environments again only after a failure) leaves this count: the entry is
    // formed once and Kept (dev's count, W6 Provisional: 1,416 before S1-1c, 912 after it).
    // R7-C3 (C7, design §13.1): one outermost request per proof and the readings under the class key: 220 -> 172 (1,416 -> 736 before
    // e2-3a's W6 kept the entry).
    // R7-C2c2 c2-5b2 (F's CC-27 (1), CC-15 Q5 (a)): Supply's rows reader (ExactAssumption) makes one FirstRows request for every atom
    // proposition, and each request outside a Kept region forms its view's entry again: 172 -> 286, the reader's own requests.
    // R7-C2c2 c2-5c (F's CC-35): IndexNormalized files the reading of a Contract fact over a projection in a view with no identity
    // premise too (the per-fact domain), and such an entry outside a Kept region indexes those readings at each formation (plan
    // section 2's cost): 286 -> 306.
    // R7-C2e2 e2-7 (G1406-49 (4), G1406-50): the requests' pairs stay dev's (325 - 19 = 306), and the view's identity closure forms
    // one T(G) entry per view per serial (GroundsEntry: the view's facts as written and under T(G)'s keys, then its readings), 5 more
    // formations than the library's, 48 pairs and 4 readings: 306 -> 358.
    [Fact]
    public void AViewsIndexIsFormedAtEachSerialUntilComplete()
        => Assert.Equal(358, ClosureWork(Trees + Moved + Derived) - ClosureWork(Trees));

    // R7-C2e2 e2-3a's coverage rows (`coverage-e23.md`): a published read's Completion (W8), W6 the meet of every source read, the
    // walk's Completion admitting its rows, every kept table for one publication. Each program's records, exact (equal under dev's
    // rules). dv1 is valid by SPEC 8.2.3 (`T is K` makes `T.(K).X` an `i32`) and rejected on dev too: an identity in a value position,
    // not this unit's (F's PLAN row). m1e's first source is in Error because its clause is a syntax error (an applied Type is no
    // ConstraintSubject). Not pinned by a row: the walk's admission and W8's Provisional for a Pending state; ContractSolver's Kept
    // rows states them as located invariants with the mutations' evidence (a row kept inside a round, stale from its source's
    // settling until the next publication's start, moves no record while the eager derivation answers the same proposition).
    // R7-C3 (merge of dev 8a1d8cff6; SPEC 8.2.3, 8.3.1, 23.1.2; F's C2 rulings 245, 255, 287-294, 302): dv1 is accepted (the one judge
    // forms `T.(K).X` as i32 under `T is K`). A premise naming an invalid Contract (K, Pending, Marker) is its declaration's premise
    // error, reported once there (dv1n's f, m4's and c12c's caller, c12s/c12e/c12r's Target; Revision 3 of design-c31b), and its uses rest
    // (m4's and c12c's `take<T>()`); the conformance clause keeps dev's record (c12s/c12e/c12r's `Self is C`, ruling 294). m1u: caller's
    // record goes only because its other premise `T is B` (B: Copy) proves what its judgment of `T is A` reads, so the consult's limit
    // decides nothing (the valid facts decide, ruling 276 (2)); with `U is B` (another subject) the record stays, as m4's: a dependent
    // record selected by the valid facts, DECISIONS Q13.
    [Theory]
    [InlineData("dv1", "public contract Origin\npublic struct Source\n    Self is Origin\ncontract K\n    associate X\n    Self.(K).X is i32\n    Source is Origin\nfunc f<T>(v: T.(K).X) -> i32\n    T is K\n    return v\n", "clean")]
    [InlineData("dv1n", "public contract Origin\npublic struct Source\ncontract K\n    associate X\n    Self.(K).X is i32\n    Source is Origin\nfunc f<T>(v: T.(K).X) -> i32\n    T is K\n    return v\n", "1*InvalidConstraint_Kd 1*TypeMismatch_Kd 2*UnprovenConstraint_Kd 2*UnsatisfiedConstraint_Kd")] // R7-C3 C3-1b (F's C2 rulings 287-290): dev's record back: f's premise `T is K` names K, whose closed `Source is Origin` is Refuted (SPEC 8.3.1, 8.2.3)
    [InlineData("dv2", "public contract Origin\npublic struct Source\n    Self is Origin\ncontract K\n    associate X\n    Self.(K).X is i32\n    Source is Origin\nfunc g<T>(v: T.(K).X) -> (T.(K).X, T.(K).X)\n    T is K\n    return (v, v)\nfunc h<T>(v: T.(K).X) -> (T.(K).X, T.(K).X)\n    T is K\n    return (v, v)\n", "clean")]
    [InlineData("dv2n", "public contract Origin\npublic struct Source\n    Self is Origin\ncontract K\n    associate X\n    Source is Origin\nfunc g<T>(v: T.(K).X) -> (T.(K).X, T.(K).X)\n    T is K\n    return (v, v)\nfunc h<T>(v: T.(K).X) -> (T.(K).X, T.(K).X)\n    T is K\n    return (v, v)\n", "4*TransferRequired_Kd")]
    [InlineData("m1v", "public contract Origin\npublic struct Source\n    Self is Origin\npublic contract A: Copy\n    Source is Origin\npublic contract B: Copy\n" + Takes + "    func caller<T>(value: T)\n        T is A\n        T is B\n        take<T>()\n", "clean")]
    [InlineData("m1u", "public contract Origin\npublic struct Source\npublic contract A: Copy\n    Source is Origin\npublic contract B: Copy\n" + Takes + "    func caller<T>(value: T)\n        T is A\n        T is B\n        take<T>()\n", "2*UnsatisfiedConstraint_Kd")]
    [InlineData("m1e", "public struct Box<T>\n    T is Copy\npublic contract A: Copy\n    Box<string> is Copy\npublic contract B: Copy\n" + Takes + "    func caller<T>(value: T)\n        T is A\n        T is B\n        take<T>()\n", "1*ExpectedSyntax_Kd")]
    [InlineData("m2", "public contract M: Copy\n    Self is not Copy\n" + Takes + "    func caller<T>(value: T)\n        T is M\n        take<T>()\n", "2*InvalidConstraint_Kd")]
    [InlineData("m2r", Takes + "    func caller<T>(value: T)\n        T is M\n        take<T>()\npublic contract M: Copy\n    Self is not Copy\n", "2*InvalidConstraint_Kd")]
    [InlineData("m2v", "public contract M: Copy\n" + Takes + "    func caller<T>(value: T)\n        T is M\n        take<T>()\n", "clean")]
    [InlineData("m4", "public contract Origin\npublic struct Source\npublic contract Pending: Copy\n    Source is Origin\n" + Takes + "    func caller<T>(value: T)\n        T is Pending\n        take<T>()\n", "1*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")] // R7-C3 C3-1b (F's C2 rulings 287-290, DECISIONS Q13): `caller`'s premise error is back (SPEC 8.3.1, 8.2.3); dev's second record, at the call, is a dependent and rests ("never again at uses")
    [InlineData("c12s", "public contract C\npublic contract Marker: Copy\n    string is Copy\npublic struct Target<T>\n    T is Marker\n    Self is C\n", "2*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")] // R7-C3 C3-1b (F's C2 ruling 294): dev's records back: Target's premise error and `Self is C`'s kept dependent (DECISIONS Q13, as src.conf)
    [InlineData("c12r", "public contract C\npublic struct Target<T>\n    T is Marker\n    Self is C\npublic contract Marker: Copy\n    string is Copy\n", "2*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")] // R7-C3 C3-1b (F's C2 ruling 294): dev's records back: Target's premise error and `Self is C`'s kept dependent (DECISIONS Q13, as src.conf)
    [InlineData("c12e", "public contract C\npublic contract Marker: Copy\n    string is Copy\npublic enum Target<T>\n    T is Marker\n    Self is C\n    A\n", "2*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")] // R7-C3 C3-1b (F's C2 ruling 294): dev's records back: Target's premise error and `Self is C`'s kept dependent (DECISIONS Q13, as src.conf)
    [InlineData("c12v", "public contract C\npublic contract Marker: Copy\n    i32 is Copy\npublic struct Target<T>\n    T is Marker\n    Self is C\n", "clean")]
    [InlineData("c12c", "public contract Marker: Copy\n    string is Copy\n" + Takes + "    func caller<T>(value: T)\n        T is Marker\n        take<T>()\n", "1*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")] // R7-C3 C3-1b (F's C2 rulings 287-290, DECISIONS Q13): `caller`'s premise error is back (SPEC 8.3.1, 8.2.3); dev's second record, at the call, is a dependent and rests ("never again at uses")
    public void APublishedReadIsKeptOnlyWhereItIsFinal(string row, string program, string records)
        => Assert.True(records == DiagnosticCorpus.RecordProfile(program), $"{row}: {DiagnosticCorpus.RecordProfile(program)}");

    // DECISIONS R7-C2e2-1, R7-M20 (implementation review 2's twins): `(a, a)` is an in-region Copy request, which keeps the view's entry
    // with readings; `Box<U>.(K).X` then normalizes through `Box<T>: K when T is C`, whose condition is proven by premise inside that
    // associated normalization, an outermost request outside KeptRegion: it forms the entry again with its own readings. The condition
    // holds by `U is C` (x1) or through the identity by `T is C` (x2); without it (x1n) `Box<U>` is not K.
    [Theory]
    [InlineData("x1", "    U is C\n", "")]
    [InlineData("x2", "    T is C\n", "")]
    [InlineData("x1n", "", "UnprovenConstraint_Kd: let y: Box<U>.(K).X = b")]
    public void AKeptEntrysReadingsAreNotReadInsideANormalization(string form, string premise, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, Boxed(premise) + "public func main() => ()\n", records);

    // The pairs x1's entries index over the library's, exact (a fact of the identity view also under its class key; the table is Final,
    // so the entry is Kept): the request inside the normalization indexes the view again, so reading the Kept entry there, without
    // IndexOf's rebuild branch, indexes 68, as does an entry Kept with readings that were not kept.
    // R7-C3 (C7, design §13.1): the written pairs are dev's; the normalized readings are filed under the class key T, not the loop's
    // U (5 + 6 against 12 + 18): 98 -> 79.
    // R7-C2c2 c2-5b2 (F's CC-27 (1), CC-15 Q5 (a)): the rows reader's own requests for every atom proposition: 87 -> 134.
    // R7-C2e2 e2-7 (G1406-49 (4), G1406-50): the requests' pairs stay dev's (134), and the view's identity closure forms one T(G) entry
    // per view per serial (GroundsEntry), 3 formations, 26 pairs and 4 readings: 134 -> 164.
    [Fact]
    public void ARequestInsideANormalizationIndexesItsViewAgain()
        => Assert.Equal(164, ClosureWork(Boxed("    U is C\n")) - ClosureWork(string.Empty)); // dev 106 (98; 108 before f6324930d, below); R7-C3 C7 -19; c2-5b2 +47; e2-7 T(G) +30

    // DECISIONS R7-C2e2-2: a closure validator's read forms its view's entry when it has a fact to pair, and the entry goes back when
    // the read ends. The agreement reads f's view (three identities of one projection through K<U>, K<V> and K<i32>, SPEC 8.9.2);
    // disjointness reads the scopes of S's two conditional conformances (`T is i32` against `T is string`, SPEC 8.9.1). The pairs their
    // entries and the requests' index over the library's: exact.
    // R7-C3 (B2', C7, design §13.1): f's identity facts are filed once (the agreement's input proofs read CollisionScope, one request
    // per proof), the Contract facts' readings under T (9): 89 -> 71.
    // PLAN x41n (F's C2 ruling 321 (D), counted by a temporary probe): the direction check asks the rows of T in each of S's two
    // conditional conformance scopes, a scope with premises of its own (`T is i32`, `T is string`); each request forms that view's entry
    // with its one written pair and does not keep it, so disjointness's read forms it again as before: 77 -> 79. Its other seed requests
    // (f's, Left's and Right's) form entries other readers form anyway (0).
    // R7-C2c2 c2-5b2 (F's CC-27 (1), CC-15 Q5 (a)): the rows reader's own requests for every atom proposition: 79 -> 133.
    // R7-C2e2 e2-7 (G1406-49 (4), G1406-50): the requests' pairs stay dev's (133), and the view's identity closure forms one T(G) entry
    // per view per serial (GroundsEntry), 15 formations, 52 pairs and 8 readings: 133 -> 193.
    [Fact]
    public void AValidatorsReadIndexesItsViewOncePerRead()
        => Assert.Equal(193, ClosureWork(Validated) - ClosureWork(string.Empty)); // dev 77 (95; 89; 97 before f6324930d, below); R7-C3 C7 -18; x41n +2; c2-5b2 +54; e2-7 T(G) +60

    // R7-C2c2 c2-3b at R7-C2e2-2's merge (merge review N1): a Callable fact is a row kind's fact, so a request's entry files it under its
    // subject and its class key, and a validator's read (no classes) under its subject alone. In f's view (`U is T`, whose class key is T)
    // the entry is formed by three outermost requests (one more since R7-C2e2 e2-3a: a publication's start forgets the Kept entry, so
    // a request after it forms the entry again) and by the agreement's one read (SPEC 8.9.2, `T.(K).E is i32`): the fact on the class
    // mate U adds 2 + 2 + 2 + 1 pairs, on the key T 1 + 1 + 1 + 1 (no class-key copy), on U without the projection identity 2 + 2 + 2
    // (no read). The pairs the fact adds, exact; valid without the fact too (`(a, a)` is a Copy request through T, SPEC 8.2.3).
    // R7-C2c2 c2-5b2 (F's CC-27 (1), CC-15 Q5 (a)): the rows reader's own requests for every atom proposition: 7, 4, 6 -> 9, 5, 8.
    // R7-C2e2 e2-7 (G1406-49 (4), G1406-50): the requests' pairs stay dev's (9, 5, 8), and f's view's identity closure forms one T(G)
    // entry per view per serial (GroundsEntry), 2 formations with or without the fact, each filing the fact on U under its subject and
    // its class key T (2 + 2), on T under T alone (1 + 1): 9, 5, 8 -> 13, 7, 12.
    [Theory]
    [InlineData("U", true, 13)] // 5, 3 and 4 before e2-3a's publication lifetime (with ForgetKeptTables skipped)
    [InlineData("T", true, 7)]
    [InlineData("U", false, 12)]
    public void ACallableFactIsFiledUnderItsClassKeyOnlyInARequestsEntry(string subject, bool read, int pairs)
    {
        var head = "contract K\n    associate E\nfunc f<T, U>(a: U, b: T.(K).E) -> (U, U)\n    U is T\n    T is Copy\n    T is K\n" + (read ? "    T.(K).E is i32\n" : string.Empty);
        Assert.Equal(pairs, ClosureWork(head + $"    {subject} is Callable<(i32) -> i32>\n    return (a, a)\n") - ClosureWork(head + "    return (a, a)\n"));
    }

    // DECISIONS R7-C2e2-2 (the read's lifetime): the entry a validator's read forms goes back when the read ends. Three paths to P<i32>
    // (SPEC 8.9.1): pair (1, 0)'s disjointness read forms L1's view's entry, which holds no class-key pairs (a read has no request's
    // classes); pair (2, 0) then proves L3's conditions in L1's scope, an outermost request whose classes join T and U, so it forms
    // that view's entry again with its class-key pairs. Kept until that request returned, the read's entry is reused without them:
    // 4 fewer pairs on t-q (the struct's `U is C` met by L1's `T is U`), 3 on t-q with the roles swapped (measured with the prototype's
    // lifetime, EndClosureRead keeping the read's entry: 18 and 17). L1's condition names Q, a struct that is C, so L1's scope is
    // consistent: with `{joined} is i32` (R7-C2e2-2's l2, l2r) the struct's premise meets i32's closed world in L1's scope, N9's premise
    // error (the owner's O-296 (a); RecursiveContractTest's l2 rows). Q joins the class where i32 did and its own conformance forms no
    // view entry, so the pairs are l2's and l2r's. The pairs over the library's: exact.
    // These three counts under e2-3a (each publication forgets the Kept entries, ForgetKeptTables, so a walk after one forms its views'
    // entries again): dev's f6324930d (PLAN G949 S1-1c) skips the program pass's certificate round when its validation walk recorded no
    // failure, and with it that round's entries: 108 -> 106, 97 -> 95, 28/26 -> 24/22 (dev, whose entries live for the epoch: 98, 89,
    // 22/20 either way). With the skip disabled the counts are e2-3a's again. R7-C3 (F's C2 ruling 282 (1), the owner's O-296 (a); SPEC
    // 8.2.3: a premise error is reported where the premises first meet): each conformance scope with premises judges the struct's
    // premises in its own view (ValidateConstraintEnvironments), whose entries add 6 and 5: 30 and 27.
    // R7-C2c2 c2-5b2 (F's CC-27 (1)): one pair fewer each with the rows reader's requests for every atom proposition, observed and not
    // located: 30 -> 29, 27 -> 26.
    // R7-C2e2 e2-7 (G1406-49 (4), G1406-50): the requests' pairs stay dev's (29, 26), and the views' identity closures form one T(G)
    // entry per view per serial (GroundsEntry), 6 formations, 18 pairs on t-q and 15 with the roles swapped, no reading:
    // 29 -> 47, 26 -> 41.
    [Theory]
    [InlineData("T", "U", 47)]
    [InlineData("U", "T", 41)]
    public void AValidatorsReadEntryGoesBackWhenTheReadEnds(string joined, string premised, int pairs)
    {
        var source = "struct Q\n    Self is C\n    public var v: i32\n    public init(v: i32) => self.v = v\n" +
            "contract C\ncontract P<E>\n    func read(self: ref/Self, value: ref/E) -> i32\ncontract L1: P<i32>\ncontract L2: P<i32>\n" +
            $"contract L3: P<i32>\nstruct S<T, U>\n    {premised} is C\n    Self is L1 when {joined} is {premised}, {joined} is Q\n" +
            $"    Self is L2 when {joined} is string\n    Self is L3 when {joined} is C\n" +
            "    public func read(self: ref/Self, value: ref/i32) -> i32 => 1\n";
        Assert.Equal(pairs, ClosureWork(source) - ClosureWork(string.Empty));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindThatRebuildsAViewsIndexAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Trees + Moved + Derived + "public func main() => ()\n");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("The rebind failed.");
            }
        }));
    }

    [Theory]
    [InlineData("Source.(Origin).Item", "i32", ConstraintProof.Proven)]
    [InlineData("i32", "Source.(Origin).Item", ConstraintProof.Proven)]
    [InlineData("Source.(Origin).Item or string", "i32 or string", ConstraintProof.Proven)]
    [InlineData("i32 or string", "Source.(Origin).Item or string", ConstraintProof.Proven)]
    [InlineData("not Source.(Origin).Item", "i32", ConstraintProof.Refuted)]
    [InlineData("not i32", "Source.(Origin).Item", ConstraintProof.Refuted)]
    [InlineData("[2 of Source.(Origin).Item] or string", "[2 of i32] or string", ConstraintProof.Proven)]
    [InlineData("(Source.(Origin).Item, string) or bool", "(i32, string) or bool", ConstraintProof.Proven)]
    [InlineData("Source.(Origin).Item or string", "i32", ConstraintProof.Unknown)]
    [InlineData("Source.(Origin).Item or string\n    T is not i32", "string", ConstraintProof.Unknown)]
    [InlineData("Source.(Origin).Item\n    T is not i32", "bool", ConstraintProof.Error)]
    public void EquivalentRequirementSpellingsUseTheSameEvidence(string evidence, string requirement, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func context<T>(value: T)\n    T is " + evidence + "\n    ()\nfunc query<T>(value: T)\n    T is " + requirement + "\n    ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        AssertProof(c, expected);
        c.Bind();
        AssertProof(c, expected);
        var restored = CompilationTestHelper.Reload(c);
        restored.Bind();
        AssertProof(restored, expected);
    }

    [Theory]
    [InlineData("Source.(Origin).Item or string", "i32 or string")]
    [InlineData("i32 or string", "Source.(Origin).Item or string")]
    [InlineData("not Source.(Origin).Item", "not i32")]
    [InlineData("not i32", "not Source.(Origin).Item")]
    public void DependentCallsUseEquivalentCompoundPremises(string evidence, string requirement)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is " + requirement + "\n    ()\nfunc caller<U>()\n    U is " + evidence + "\n    take<U>()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.NotNull(Assert.IsType<InvocationKoto>(Function(c, "caller").Body!.Items.Single()).CallOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConcreteProofNormalizesTheAssociatedRequirement(string argument, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(
            Head + "func query<T>(value: T)\n    T is Source.(Origin).Item\n    ()\nfunc context(value: " + argument + ") => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        AssertProof(c, expected);
    }

    // Pins Binding's proof value Unknown for the [3 of i32] and i32 rows, which SPEC 8.2.3 refutes, pending PLAN G170's C2-C4 (R4-M115).
    [Theory]
    [InlineData("Source.(Origin).Item and Copy", "i32 and Copy", ConstraintProof.Proven)]
    [InlineData("not (Source.(Origin).Item and string)", "not i32", ConstraintProof.Unknown)]
    [InlineData("not (Source.(Origin).Item and string)", "not (i32 and string)", ConstraintProof.Proven)]
    [InlineData("[2 of Source.(Origin).Item]", "[3 of i32]", ConstraintProof.Unknown)]
    [InlineData("ref/Source.(Origin).Item during static", "i32", ConstraintProof.Unknown)]
    public void NormalizationDoesNotAddLogicalRulesOrEraseTypeStructure(string evidence, string requirement, ConstraintProof expected)
        => this.EquivalentRequirementSpellingsUseTheSameEvidence(evidence, requirement, expected);

    [Fact]
    public void ReplacingAssociatedIdentityRebuildsProofEvidence()
    {
        const string functions = "func context<T>(value: T)\n    T is Source.(Origin).Item or string\n    ()\nfunc query<T>(value: T)\n    T is i32 or string\n    ()";
        var c = MinimalEmissionTest.Analyze(Head + functions);
        AssertProof(c, ConstraintProof.Proven);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(Head.Replace("is i32", "is bool", StringComparison.Ordinal) + functions);
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Unknown);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Proven);
    }

    // DECISIONS R7-C2e1 (F1): `T is K` supplies `T.(K).X is Q` and Q's clause `T.(K).X.(Q).Y is i32` (SPEC 8.3.2), so the written premise
    // asks `i32 is Marker`, which a primitive's fixed conformances refute (SPEC 8.7.1): f's premises contradict, and each use of them is
    // InvalidConstraint_Kd. f's view's environment is written after its facts are first read in the pass, so a kept reading is valid only
    // under the serial naming the environment's state. Both orders. R7-C3 (C2 rulings 243, 250 (2), F's (c); Q13): a use that reads the
    // failed premise rests on f's record: 9 -> 3 (f@11, the two g calls).
    [Theory]
    [InlineData("x.q()\n    g<i32>()\n")]
    [InlineData("g<i32>()\n    x.q()\n")]
    public void AReadingIsKeptOnlyForTheEnvironmentItWasFormedIn(string uses)
        => Assert.Equal(
            "3*InvalidConstraint_Kd",
            DiagnosticCorpus.RecordProfile("contract Marker\ncontract Q\n    associate Y\n    Self.(Q).Y is i32\n    func q(self: ref/Self) -> ()\n" +
                "contract K\n    associate X is Q\nfunc g<Z>()\n    Z is Marker\n    ()\nfunc f<T>(x: ref/T.(K).X)\n    T is K\n    T.(K).X.(Q).Y is Marker\n" +
                "    g<T.(K).X.(Q).Y>()\n    " + uses));

    // DECISIONS R7-C2e1 (F5):a requirement's signature is formed in its declaration's view under the call's reference (SPEC 8.6.4), so
    // P's clause reads `A.(Src).Item is not Marker` under P<A> and `B.(Src).Item is not Marker` under P<B>, and each call's candidate is
    // refuted under its own reference (SPEC 8.3.2): a reading is one per requirement Contract. Both orders.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadingIsOnePerRequirementReference(bool reversed)
    {
        const string Declarations = "contract Marker\ncontract Src\n    associate Item\nstruct Box<T>\n    T is Marker\n    public init() => ()\n" +
            "contract P<E>\n    E is Src\n    E.(Src).Item is not Marker\n    func read(self: ref/Self) -> Box<E.(Src).Item>\n";
        var (first, second) = (Use("via", "A"), Use("via2", "B"));
        var source = Declarations + (reversed ? second + first : first + second);
        Assert.Equal("2*NoApplicableOverload_Kd 1*UnsatisfiedConstraint_Kd", DiagnosticCorpus.RecordProfile(source)); // the requirement `read` carries its written result `Box<E.(Src).Item>`, which keeps the record: the Function's restating one goes (G1278-A, Q13; F's G1278-3)

        static string Use(string name, string slot)
            => $"func {name}<T, {slot}>(t: ref/T)\n    T is P<{slot}>\n    {slot} is Src\n    {slot}.(Src).Item is not Marker\n    t.read()\n";
    }

    // DECISIONS R7-C2e1 (F6): the builtin specification of `T.(Addable<T>).Output` proves T's arithmetic conditions in f's view, where
    // both premises naming the projection meet the open normalization and stay unreduced. Those readings are not the view's: `g<V>()`
    // reads the second premise as `V is Marker` (SPEC 8.2.3). Both orders; without that premise `V is Marker` is unproven.
    [Theory]
    [InlineData("T.(Addable<T>).Output is V\n    T.(Addable<T>).Output is Marker\n", "clean")]
    [InlineData("T.(Addable<T>).Output is Marker\n    T.(Addable<T>).Output is V\n", "clean")]
    [InlineData("T.(Addable<T>).Output is V\n", "1*UnprovenConstraint_Kd")]
    public void AReadingFormedUnderANormalizationIsNotKept(string premises, string records)
        => Assert.Equal(
            records,
            DiagnosticCorpus.RecordProfile("contract Marker\nfunc g<X>()\n    X is Marker\n    ()\nfunc f<T, V>()\n    T is Addable<T>\n    " + premises + "    g<V>()\n"));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNormalizedProofsAndCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is [2 of i32] or string\n    ()\nfunc caller<U>()\n    U is [2 of Source.(Origin).Item] or string\n    take<U>()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Normalized constraint proof failed.");
            }
        }));
    }

    private static string Boxed(string premise)
        => "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ncontract C\n" +
            "contract K\n    associate X\nstruct Box<T>\n    public let v: i32\n    Self is K when T is C\n        associate K.X is i32\n" +
            "func f<T, U>(a: U.(Tree).Child, b: i32) -> (U.(Tree).Child, U.(Tree).Child)\n    T is U\n    T is CopyTree\n" + premise +
            "    let p = (a, a)\n    let y: Box<U>.(K).X = b\n    return p\n";

    // Closure(view)'s pairs indexed by a compilation of `source` (the class's scaling measure).
    private static int ClosureWork(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        return c.Binding.ClosureIndexWork;
    }

    private static void AssertProof(Compilation c, ConstraintProof expected)
    {
        var context = Function(c, "context");
        var query = Function(c, "query");
        var proposition = ((IsKoto)query.TypeConstraints[0]).ConstraintOf()!;
        Assert.Equal(expected, c.Binding.Prove(proposition, query, [context.Parameters[0].Type.TypeOf()], context));
    }

    private static FunctionKoto Function(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);
}
