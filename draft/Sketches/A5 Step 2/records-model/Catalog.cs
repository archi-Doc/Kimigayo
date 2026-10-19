// One closed code-to-existing-category contract. Rows never duplicate the category.
// Domain validates producer assessments; finalization consumes this same catalog for copy and cover eligibility.
namespace RecordsModel;

enum Category { Language, Proof, Unsupported }

static class Catalog
{
    public static Category Of(Code code) => code switch
    {
        Code.Loan or Code.DefaultBorrow or Code.DefaultAccess or Code.OriginRefuted or Code.Source => Category.Language,
        Code.OriginUnknown => Category.Proof,
        Code.Unsupported => Category.Unsupported,
        _ => throw new ProtocolFault(Fault.Domain),
    };
}
