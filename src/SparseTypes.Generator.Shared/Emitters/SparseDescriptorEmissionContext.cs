namespace SparseTypes.Generator.Shared;

internal sealed class SparseDescriptorEmissionContext
{
    private SparseDescriptorEmissionContext(string receiver, string? observableType)
    {
        Receiver = receiver;
        ObservableType = observableType;
    }

    internal static SparseDescriptorEmissionContext Inline { get; } = new("this", null);

    internal string Receiver { get; }

    private string? ObservableType { get; }

    internal static SparseDescriptorEmissionContext ForFactory(string observableType) => new("observable", observableType);

    internal string Member(string name) => Receiver + "." + name;

    internal static string Attributes(SparseMemberModel member)
    {
        if (string.IsNullOrEmpty(member.Property.AttributeExpressions))
        {
            return "global::System.Array.Empty<global::System.Attribute>()";
        }

        return "new global::System.Attribute[] { " + member.Property.AttributeExpressions + " }";
    }

    internal string MethodDeclaration(string modelType, SparseDescriptorDialect dialect, string pathType)
    {
        if (ObservableType is null)
        {
            return "internal "
                + dialect.DescriptorSetInterface
                + " "
                + SparseObservableDescriptorEmitter.AccessorName(modelType)
                + "("
                + pathType
                + " pathPrefix)";
        }

        return "internal static " + dialect.DescriptorSetInterface + " Create(" + ObservableType + " observable, " + pathType + " pathPrefix)";
    }
}
