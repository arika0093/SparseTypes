namespace SparseTypes.Blazor;

internal interface ISparseValidationScope
{
    IEnumerable<string> GetValidationMessages(SparsePath path);
}
