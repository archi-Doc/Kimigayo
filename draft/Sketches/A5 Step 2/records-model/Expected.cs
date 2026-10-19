// Handwritten complete row/publication expectations. This file does not read Fixtures, Report, or Format.
// Repeated literal fragments abbreviate ONLY authoring; expected.txt expands every full row before the model runs.
namespace RecordsModel;

static class Expected
{
    private const string Fit = "Outlives(A:expr(Qualified),B:expr(Qualified),Fit(Type(RefA)))";
    private const string PublicFit = "Outlives(A:Expression:Qualified:-:-,B:Expression:Qualified:-:-,Fit(Type(RefA)))";
    private const string OriginA = "Zero:OriginRefuted@Exit:origin:ReturnA/borrow:A";
    private const string OriginB = "Zero:OriginRefuted@Exit:origin:ReturnA/borrow:B";
    private const string Default = "Zero:DefaultBorrow@Exit:DefaultBorrow:Return/First/borrow:A";
    private const string Storage = "Zero:Loan@Exit:StorageEnd:Return/First/borrow:A";
    private const string Write = "Zero:Loan@Earlier:Access:Earlier/First/borrow:A => Loan|Ownership|empty|Checked|outside|[Use@WitnessA]|3";
    private const string WritePublic = "PUBLIC Loan@Earlier|empty|Zero{Alpha,Beta}:[Use@WitnessA]";

    public static Dictionary<string, string[]> All()
    {
        var result = new Dictionary<string, string[]>();
        void Add(string name, string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
            result.Add(name, lines.Where(line => !line.StartsWith("PUBLIC ", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
                .Concat(lines.Where(line => line.StartsWith("PUBLIC ", StringComparison.Ordinal)).Order(StringComparer.Ordinal)).ToArray());
        }
        Add("duplicates", $$"""
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA]|5
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB]|2
            {{OriginB}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA]|2
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha,Beta,Gamma}:[Use@WitnessA];Zero{Beta}:[Use@WitnessB]
            """);
        Add("reasons", $$"""
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Clause@Clause,FixedBy@Fixed]|1
            {{OriginA}} => OriginRefuted|Ownership|Equal(A:expr(Qualified),B:expr(Qualified),Fit(Type(RefB)))|Checked|outside|[Clause@Clause]|2
            Zero:OriginRefuted@Exit:origin:Independent/borrow:A => OriginRefuted|Ownership|Equal(A:expr(Qualified),B:expr(Qualified),Declared)|Checked|outside|[Clause@Clause]|1
            Zero:OriginRefuted@Exit:origin:Independent/borrow:A => OriginRefuted|Ownership|Outlives(A:expr(Qualified),B:expr(Qualified),WellFormed)|Checked|outside|[FixedBy@Fixed]|2
            {{OriginA}} => OriginRefuted|Ownership|Outlives(A:expr(Qualified),C:omitted(TypeSite),Fit(OriginOnly))|Checked|outside|[Origin@TypeSite]|4
            {{OriginA}} => OriginRefuted|Ownership|Outlives(A:closure(Header/CallReceiver),B:closure(Header/CallResult),Fit(OriginOnly))|Checked|outside|[Origin@Header]|8
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha}:[Clause@Clause,FixedBy@Fixed]
            PUBLIC OriginRefuted@Exit|Equal(A:Expression:Qualified:-:-,B:Expression:Qualified:-:-,Fit(Type(RefB)))|Zero{Beta}:[Clause@Clause]
            PUBLIC OriginRefuted@Exit|Equal(A:Expression:Qualified:-:-,B:Expression:Qualified:-:-,Declared)|Zero{Alpha}:[Clause@Clause]
            PUBLIC OriginRefuted@Exit|Outlives(A:Expression:Qualified:-:-,B:Expression:Qualified:-:-,WellFormed)|Zero{Beta}:[FixedBy@Fixed]
            PUBLIC OriginRefuted@Exit|Outlives(A:Expression:Qualified:-:-,C:Omitted:-:TypeSite:-,Fit(OriginOnly))|Zero{Gamma}:[Origin@TypeSite]
            PUBLIC OriginRefuted@Exit|Outlives(A:Closure:-:Header:CallReceiver,B:Closure:-:Header:CallResult,Fit(OriginOnly))|Zero{Delta}:[Origin@Header]
            """);
        Add("effects", """
            Zero:Loan@Call:effect:Call/borrow:A/X/Read => Loan|Ownership|empty|Checked|outside|[Use@WitnessA,effect(X/Read@Call)]|1
            Zero:Loan@Call:effect:Call/borrow:A/X/Read => Loan|Ownership|empty|Checked|outside|[Use@WitnessB,effect(X/Read@Call)]|4
            Zero:Loan@Call:effect:Call/borrow:A/X/Write => Loan|Ownership|empty|Checked|outside|[Use@WitnessA,effect(X/Write@Call)]|2
            Zero:Loan@Call:effect:Call/borrow:A/Y/Write => Loan|Ownership|empty|Checked|outside|[Use@WitnessB,effect(Y/Write@Call)]|2
            Zero:Loan@Call:effect:Call/borrow:A/Unknown => Loan|Ownership|empty|Checked|outside|[Use@WitnessB,effect(Unknown@Call)]|2
            Zero:Loan@Call:Destruction:Call/First/borrow:A => Loan|Ownership|empty|Checked|outside|[Reservation@WitnessA]|4
            PUBLIC Loan@Call|empty|Zero{Alpha}:[Use@WitnessA,effect(X/Read@Call)];Zero{Beta}:[Use@WitnessA,effect(X/Write@Call)];Zero{Beta}:[Use@WitnessB,effect(Unknown@Call)];Zero{Beta}:[Use@WitnessB,effect(Y/Write@Call)];Zero{Gamma}:[Reservation@WitnessA];Zero{Gamma}:[Use@WitnessB,effect(X/Read@Call)]
            """);

        // The fixed status matrix is handwritten. No protocol decision computes its expected mask.
        foreach (var (name, mask, cases, targetCode, preferred) in new[]
        {
            ("checked", "2", "Beta", "Loan", "Zero:Loan@Cleanup:GuardEnd:Cleanup/First/guard:A => Loan|Ownership|empty|Checked|outside|[Use@WitnessA]|1\nPUBLIC Loan@Cleanup|empty|Zero{Alpha}:[Use@WitnessA]"),
            ("held", "3", "Alpha,Beta", "Loan", "Zero:Loan@Cleanup:GuardEnd:Cleanup/First/guard:A => Unsupported|Ownership|empty|Held|outside|[]|1\nPUBLIC Unsupported@Cleanup|empty|Zero{Alpha}:[]"),
            ("limited", "3", "Alpha,Beta", "Unsupported", "Zero:Unsupported@Cleanup:GuardEnd:Cleanup/First/guard:A => Unsupported|Ownership|empty|Limit|inline:1@Call|[]|1\nPUBLIC Unsupported@Cleanup|empty|Zero{Alpha}:[]"),
            ("copied", "3", "Alpha,Beta", "Loan", ""),
            ("missing", "3", "Alpha,Beta", "Loan", ""),
        })
            Add($"guard-{name}", $$"""
                Zero:OriginRefuted@Exit:origin:GuardA/guard:A => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA,covered(GuardReverse=>Zero:{{targetCode}}@Cleanup:GuardEnd:Cleanup/First/guard:A)]|{{mask}}
                Zero:OriginRefuted@Exit:origin:GuardB/guard:B => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB]|3
                {{Write}}
                {{WritePublic}}
                PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessB];Zero{ {{cases}} }:[Use@WitnessA]
                {{preferred}}
                """.Replace("{ ", "{", StringComparison.Ordinal).Replace(" }", "}", StringComparison.Ordinal));
        // For equal case sets, the explanation's facts provide the tie-breaker (A before B).
        foreach (var name in new[] { "held", "limited", "copied", "missing" })
            result[$"guard-{name}"] = result[$"guard-{name}"].Select(line => line.Replace(
                "Zero{Alpha,Beta}:[Use@WitnessB];Zero{Alpha,Beta}:[Use@WitnessA]",
                "Zero{Alpha,Beta}:[Use@WitnessA];Zero{Alpha,Beta}:[Use@WitnessB]", StringComparison.Ordinal)).ToArray();
        Add("guard-unknown", $$"""
            Zero:OriginUnknown@Exit:origin:Unknown/guard:A => OriginUnknown|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA]|3
            Zero:Loan@Cleanup:GuardEnd:Cleanup/First/guard:A => Loan|Ownership|empty|Checked|outside|[Use@WitnessA]|1
            PUBLIC OriginUnknown@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessA]
            PUBLIC Loan@Cleanup|empty|Zero{Alpha}:[Use@WitnessA]
            """);
        Add("default-chain", $$"""
            {{Default}} => DefaultBorrow|Ownership|empty|Checked|outside|[Use@WitnessA]|1
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA,covered(DefaultOrigin=>{{Default}})]|2
            {{Storage}} => Loan|Ownership|empty|Checked|outside|[Use@WitnessA,covered(ReturnStorageEnd=>{{OriginA}})]|1
            {{Write}}
            {{OriginB}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB]|3
            Zero:OriginRefuted@Exit:origin:SecondBoundary/borrow:A => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB]|3
            PUBLIC DefaultBorrow@Exit|empty|Zero{Alpha}:[Use@WitnessA]
            PUBLIC Loan@Exit|empty|Zero{Alpha}:[Use@WitnessA]
            {{WritePublic}}
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessB];Zero{Beta}:[Use@WitnessA]
            """);
        Add("explicit-multichain", $$"""
            {{Default}} => DefaultBorrow|Ownership|empty|Checked|outside|[Use@WitnessA]|1
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA,covered(DefaultOrigin=>{{Default}})]|2
            Zero:OriginRefuted@Exit:origin:ReturnB/borrow:A => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB,covered(DefaultOrigin=>{{Default}})]|2
            {{Storage}} => Loan|Ownership|empty|Checked|outside|[Use@WitnessA,covered(ReturnStorageEnd=>{{OriginA}}),covered(ReturnStorageEnd=>Zero:OriginRefuted@Exit:origin:ReturnB/borrow:A)]|1
            {{OriginB}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB]|3
            PUBLIC DefaultBorrow@Exit|empty|Zero{Alpha}:[Use@WitnessA]
            PUBLIC Loan@Exit|empty|Zero{Alpha}:[Use@WitnessA]
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessB];Zero{Beta}:[Use@WitnessA]
            """);
        Add("boundaries", $$"""
            {{Default}} => DefaultBorrow|Ownership|empty|Checked|outside|[Use@WitnessA]|1
            Zero:DefaultBorrow@Exit:DefaultBorrow:Return2/First/borrow:A => DefaultBorrow|Ownership|empty|Checked|outside|[Use@WitnessB]|2
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA,covered(DefaultOrigin=>{{Default}})]|2
            Zero:OriginRefuted@Exit:origin:SecondBoundary/borrow:A => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessB,covered(DefaultOrigin=>Zero:DefaultBorrow@Exit:DefaultBorrow:Return2/First/borrow:A)]|1
            {{Write}}
            {{WritePublic}}
            PUBLIC DefaultBorrow@Exit|empty|Zero{Alpha}:[Use@WitnessA];Zero{Beta}:[Use@WitnessB]
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha}:[Use@WitnessB];Zero{Beta}:[Use@WitnessA]
            """);
        Add("default-access", $$"""
            {{Default}} => DefaultBorrow|Ownership|empty|Checked|outside|[Use@WitnessA]|1
            Zero:DefaultAccess@Exit:DefaultAccess:Return/First/borrow:A => DefaultAccess|Ownership|empty|Checked|outside|[Use@WitnessA,covered(DefaultAccess=>{{Default}})]|2
            {{Write}}
            {{WritePublic}}
            PUBLIC DefaultBorrow@Exit|empty|Zero{Alpha}:[Use@WitnessA]
            PUBLIC DefaultAccess@Exit|empty|Zero{Beta}:[Use@WitnessA]
            """);
        foreach (var (name, targetCode, preferred) in new[]
        {
            ("held", "DefaultBorrow", "Zero:DefaultBorrow@Exit:DefaultBorrow:Return/First/borrow:A => Unsupported|Ownership|empty|Held|outside|[]|1\nPUBLIC Unsupported@Exit|empty|Zero{Alpha}:[]"),
            ("limited", "Unsupported", "Zero:Unsupported@Exit:DefaultBorrow:Return/First/borrow:A => Unsupported|Ownership|empty|Limit|inline:1@Call|[]|1\nPUBLIC Unsupported@Exit|empty|Zero{Alpha}:[]"),
            ("copied", "DefaultBorrow", ""),
            ("missing", "DefaultBorrow", ""),
        })
            Add($"default-{name}", $$"""
                {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA,covered(DefaultOrigin=>Zero:{{targetCode}}@Exit:DefaultBorrow:Return/First/borrow:A)]|3
                {{Write}}
                {{WritePublic}}
                PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessA]
                {{preferred}}
                """);
        Add("return-exact", $$"""
            {{OriginA}} => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA]|1
            {{Storage}} => Loan|Ownership|empty|Checked|outside|[Use@WitnessA,covered(ReturnStorageEnd=>{{OriginA}})]|2
            Zero:Loan@Exit:StorageEnd:Return/First/borrow:B => Loan|Ownership|empty|Checked|outside|[Use@WitnessB]|3
            {{Write}}
            Zero:Loan@Call:Access:Call/First/borrow:A => Loan|Ownership|empty|Checked|outside|[Use@WitnessB]|3
            Zero:Loan@Call:Access:Call/Second/borrow:A => Loan|Ownership|empty|Checked|outside|[Store@WitnessB]|3
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha}:[Use@WitnessA]
            PUBLIC Loan@Exit|empty|Zero{Alpha,Beta}:[Use@WitnessB];Zero{Beta}:[Use@WitnessA]
            {{WritePublic}}
            PUBLIC Loan@Call|empty|Zero{Alpha,Beta}:[Store@WitnessB];Zero{Alpha,Beta}:[Use@WitnessB]
            """);
        Add("body-cases", """
            Zero:Source@Call:source:0 => Source|Ownership|empty|Checked|outside|[Use@WitnessA]|3
            Other:Source@Call:source:0 => Source|Ownership|empty|Checked|outside|[Use@WitnessA]|3
            NoBody:Source@Call:source:0 => Source|Ownership|empty|Checked|outside|[Use@WitnessA]|1
            PUBLIC Source@Call|empty|NoBody{present}:[Use@WitnessA];Other{Red,Blue}:[Use@WitnessA];Zero{Alpha,Beta}:[Use@WitnessA]
            """);
        Add("same-span", """
            Zero:Source@Call:source:1 => Source|Ownership|empty|Checked|outside|[Use@WitnessA]|3
            Zero:Source@Call:source:2 => Source|Ownership|empty|Checked|outside|[Use@WitnessB]|1
            PUBLIC Source@Call|empty|Zero{Alpha,Beta}:[Use@WitnessA];Zero{Alpha}:[Use@WitnessB]
            """);
        Add("borrow-recipes", $$"""
            {{OriginA}} => OriginRefuted|Ownership|Outlives(A:borrow(borrow:A@TypeSite),B:expr(Qualified),Fit(Type(RefA)))|Checked|outside|[Use@WitnessA]|1
            {{OriginB}} => OriginRefuted|Ownership|Outlives(A:borrow(borrow:B@TypeSite),B:expr(Qualified),Fit(Type(RefA)))|Checked|outside|[Use@WitnessB]|2
            {{OriginA}} => OriginRefuted|Ownership|Outlives(A:omitted(TypeSite),B:expr(Qualified),Fit(Type(RefA)))|Checked|outside|[Use@WitnessA]|4
            PUBLIC OriginRefuted@Exit|Outlives(A:Borrow:-:TypeSite:-,B:Expression:Qualified:-:-,Fit(Type(RefA)))|Zero{Alpha}:[Use@WitnessA];Zero{Beta}:[Use@WitnessB]
            PUBLIC OriginRefuted@Exit|Outlives(A:Omitted:-:TypeSite:-,B:Expression:Qualified:-:-,Fit(Type(RefA)))|Zero{Gamma}:[Use@WitnessA]
            """);
        Add("category-copy", $$"""
            Zero:OriginUnknown@Exit:origin:Unknown/guard:A => OriginUnknown|Ownership|{{Fit}}|Checked|inline:1@Call|[Use@WitnessA]|3
            Zero:Unsupported@Call:source:0 => Unsupported|Ownership|empty|Checked|inline:1@Call|[]|1
            Zero:Unsupported@Call:source:0 => Unsupported|Ownership|empty|Checked|outside|[]|2
            Zero:Unsupported@Call:source:2 => Unsupported|Ownership|empty|Limit|inline:1@Call|[]|4
            NoBody:Unsupported@Call:source:0 => Unsupported|Ownership|empty|Checked|outside|[]|1
            PUBLIC OriginUnknown@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessA]
            PUBLIC Unsupported@Call|empty|NoBody{present}:[];Zero{Alpha,Beta,Gamma}:[]
            """);
        Add("category-cover", $$"""
            Zero:Unsupported@Cleanup:GuardEnd:Cleanup/First/guard:A => Unsupported|Ownership|empty|Checked|outside|[]|1
            Zero:OriginRefuted@Exit:origin:GuardA/guard:A => OriginRefuted|Ownership|{{Fit}}|Checked|outside|[Use@WitnessA,covered(GuardReverse=>Zero:Unsupported@Cleanup:GuardEnd:Cleanup/First/guard:A)]|3
            {{Write}}
            PUBLIC Unsupported@Cleanup|empty|Zero{Alpha}:[]
            PUBLIC OriginRefuted@Exit|{{PublicFit}}|Zero{Alpha,Beta}:[Use@WitnessA]
            {{WritePublic}}
            """);
        return result;
    }
}
