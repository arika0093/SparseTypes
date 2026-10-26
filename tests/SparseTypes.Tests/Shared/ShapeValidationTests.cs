using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

/// <summary>
/// Direct shared-unit tests plus generator-level parity coverage for the
/// centralized validation helpers.
/// </summary>
public sealed class ShapeValidationTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = tpa.Select(path => (MetadataReference)TestMetadataReferences.FromFile(path)).ToList();
        references.Add(TestMetadataReferences.FromFile(typeof(SparseModelAttribute).Assembly.Location));
        return CSharpCompilation.Create(
            "SparseShapeProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Sources) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (runResult.Diagnostics, runResult.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray());
    }

    private static SparseMemberModel ScalarMember(int id, string name, string? wireName = null, bool ignored = false, bool transportIgnored = false)
    {
        var type = new SparseTypeModel("global::System.String?", "global::System.String", "string", true, false, null);
        var property = new SparsePropertyModel(
            name,
            type,
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: wireName ?? name,
            HasExplicitJsonPropertyName: wireName is not null,
            JsonIgnoreCondition: ignored ? 1 : 0,
            IsTransportIgnored: transportIgnored
        );
        return new SparseMemberModel(id, property, null, SparseMergeModes.Replace, SparseCollectionInfo.Unsupported, null, null, false, true);
    }

    [Test]
    public void ReservedCollisions_UsesCallerProvidedNames()
    {
        var members = ImmutableArray.Create(ScalarMember(0, "Name"), ScalarMember(1, "FragmentBuilder"), ScalarMember(2, "PayloadJson"));
        SparseShapeValidation
            .FindFirstReservedNameCollision(members, ImmutableArray.Create("FragmentBuilder", "PayloadJson"))
            .ShouldBe("FragmentBuilder");
        SparseShapeValidation
            .FindReservedNameCollisions(
                members.Select(static member => member.Property.Name),
                ImmutableArray.Create("FragmentBuilder", "PayloadJson")
            )
            .ShouldBe(["FragmentBuilder", "PayloadJson"]);
    }

    [Test]
    public void ReservedCollisions_ComposeGeneratedAndProductNames()
    {
        var reserved = ImmutableArray.Create("Fragment", "FragmentBuilder", "Merge").Add("CustomReserved");
        SparseShapeValidation.FindReservedNameCollisions(["Name", "Merge", "CustomReserved"], reserved).ShouldBe(["Merge", "CustomReserved"]);
        // Without the core set, product-only collisions still resolve.
        SparseShapeValidation.FindReservedNameCollisions(["Name", "Merge"], ImmutableArray.Create("CustomReserved")).ShouldBeEmpty();
    }

    [Test]
    public void DuplicateWireNames_DetectsExactDuplicates()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "First", "dup"),
            ScalarMember(1, "Second", "dup"),
            ScalarMember(2, "Plain"),
            ScalarMember(3, "Ignored", "dup", ignored: true)
        );
        // Ignored members never participate, so only one duplicate remains.
        SparseShapeValidation.FindDuplicateWireNames(members).ShouldBe(["dup"]);
    }

    [Test]
    public void DuplicateWireNames_AcceptsDistinctNames()
    {
        var members = ImmutableArray.Create(ScalarMember(0, "First", "a"), ScalarMember(1, "Second", "b"), ScalarMember(2, "Plain"));
        SparseShapeValidation.FindDuplicateWireNames(members).ShouldBeEmpty();
    }

    [Test]
    public void DuplicatePayloadWireNames_UsesCanonicalCamelCase()
    {
        var contract = SparsePayloadContract.Create(ImmutableArray.Create(ScalarMember(0, "Name"), ScalarMember(1, "name")));
        SparseShapeValidation.FindDuplicatePayloadWireNames(contract).ShouldBe(["name"]);
    }

    [Test]
    public void DuplicatePayloadWireNamesIncludesJsonExcludedTransportMembers()
    {
        var contract = SparsePayloadContract.Create(
            ImmutableArray.Create(ScalarMember(0, "Hidden", "visible", ignored: true), ScalarMember(1, "Visible"))
        );
        SparseShapeValidation.FindDuplicatePayloadWireNames(contract).ShouldBe(["visible"]);
    }

    [Test]
    public void DuplicatePayloadWireNamesIncludesTransportExcludedMembers()
    {
        var contract = SparsePayloadContract.Create(
            ImmutableArray.Create(ScalarMember(0, "LocalOnly", "visible", transportIgnored: true), ScalarMember(1, "Visible"))
        );
        SparseShapeValidation.FindDuplicatePayloadWireNames(contract).ShouldBe(["visible"]);
    }

    [Test]
    public void DuplicateWireNames_ExcludesTransportIgnoredMembers()
    {
        var members = ImmutableArray.Create(ScalarMember(0, "Visible", "same"), ScalarMember(1, "TransportIgnored", "same", transportIgnored: true));
        SparseShapeValidation.FindDuplicateWireNames(members).ShouldBeEmpty();
    }

    [Test]
    public void PayloadContractSeparatesNeutralAndJsonMembership()
    {
        var contract = SparsePayloadContract.Create(
            ImmutableArray.Create(
                ScalarMember(0, "JsonIgnored", ignored: true),
                ScalarMember(1, "TransportIgnored", transportIgnored: true),
                ScalarMember(2, "Visible")
            )
        );

        contract.Members.Select(static member => member.ClrName).ShouldBe(["JsonIgnored", "TransportIgnored", "Visible"]);
        contract.TransportMembers.Select(static member => member.ClrName).ShouldBe(["JsonIgnored", "Visible"]);
        contract.JsonMembers.Select(static member => member.ClrName).ShouldBe(["Visible"]);
        contract.ModelChangeOperations.ShouldBe([SparsePayloadOperationKind.Update, SparsePayloadOperationKind.Replace]);
        contract.EndpointStates.ShouldBe([
            SparsePayloadEndpointState.Missing,
            SparsePayloadEndpointState.Null,
            SparsePayloadEndpointState.Redacted,
            SparsePayloadEndpointState.Value,
        ]);
        contract.DtoRoles.ShouldBe([
            SparsePayloadDtoRole.Root,
            SparsePayloadDtoRole.MembersDelta,
            SparsePayloadDtoRole.ModelChange,
            SparsePayloadDtoRole.CollectionChange,
            SparsePayloadDtoRole.ItemOperation,
            SparsePayloadDtoRole.Endpoint,
        ]);
        contract.EnvelopeVersionPropertyName.ShouldBe("version");
        contract.EnvelopeChangePropertyName.ShouldBe("change");
        contract.OperationKindPropertyName.ShouldBe("kind");
        contract.MembersPropertyName.ShouldBe("members");
        contract.EndpointStatePropertyName.ShouldBe("state");
        contract.EndpointValuePropertyName.ShouldBe("value");
        contract.IgnoreUnknownMembers.ShouldBeTrue();
        contract.RejectDuplicateProperties.ShouldBeTrue();
        contract.RejectUnknownOperations.ShouldBeTrue();
        SparsePayloadContract.Create(contract.Members.Select(static member => member.Member).ToImmutableArray()).ShouldBe(contract);
    }

    [Test]
    public void PayloadContractResolvesCamelCaseAndExplicitWireNames()
    {
        var contract = SparsePayloadContract.Create(
            ImmutableArray.Create(ScalarMember(0, "URLValue"), ScalarMember(1, "DisplayName", "display-name"))
        );
        contract.Members[0].JsonName.ShouldBe(global::System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName("URLValue"));
        contract.Members[1].JsonName.ShouldBe("display-name");
    }

    [Test]
    public void PayloadContractDefinesOperationsForNestedAndKeyedMembers()
    {
        var childType = new SparseTypeModel("global::Child", "global::Child", "Child", true, true, null);
        var nested = ScalarMember(0, "Nested") with { ChildModel = childType };
        var keyed = ScalarMember(1, "Items") with
        {
            Collection = new SparseCollectionInfo(
                SparseCollectionKind.List,
                SparseCloneCollectionKind.List,
                childType,
                null,
                "global::System.Collections.Generic.List<T>",
                SparseCollectionSemantic.KeyedSequence,
                ImmutableArray.Create("Id"),
                "int",
                SparseKeyKind.Property
            ),
        };

        var contract = SparsePayloadContract.Create(ImmutableArray.Create(nested, keyed));
        contract.ModelDependencies.ShouldBe(["global::Child"]);
        contract.Members[0].Shape.ShouldBe(SparsePayloadMemberShape.NestedModel);
        contract.Members[0].ChangeOperations.ShouldBe([SparsePayloadOperationKind.Update, SparsePayloadOperationKind.Replace]);
        contract.Members[1].Shape.ShouldBe(SparsePayloadMemberShape.KeyedCollection);
        contract.Members[1].KeyTypeName.ShouldBe("int");
        contract.Members[1].KeyPropertyNames.ShouldBe(["Id"]);
        contract.Members[1].CarriesOrder.ShouldBeTrue();
        contract.Members[1].ChangeOperations.ShouldBe([SparsePayloadOperationKind.Update, SparsePayloadOperationKind.Replace]);
        contract
            .Members[1]
            .ItemOperations.ShouldBe([
                SparsePayloadOperationKind.Add,
                SparsePayloadOperationKind.Remove,
                SparsePayloadOperationKind.Update,
                SparsePayloadOperationKind.Replace,
            ]);
    }

    private static ITypeSymbol GetPropertyType(string source, string holder, string property)
    {
        var compilation = CreateCompilation(source);
        var type = compilation.GetTypeByMetadataName(holder) ?? throw new InvalidOperationException($"Type '{holder}' not found.");
        return ((IPropertySymbol)type.GetMembers(property).Single()).Type;
    }

    [Test]
    public void MemberReason_AcceptsRepresentableShapes()
    {
        const string source = """
            using System.Collections.Generic;
            public class ShapeHolder
            {
                public int Count { get; set; }
                public string? Name { get; set; }
                public List<string> Tags { get; set; } = new();
                public int[] Scores { get; set; } = [];
                public int? Maybe { get; set; }
            }
            """;
        foreach (var property in new[] { "Count", "Name", "Tags", "Scores", "Maybe" })
        {
            SparseShapeValidation.GetUnsupportedMemberReason(GetPropertyType(source, "ShapeHolder", property)).ShouldBeNull(property);
        }
    }

    [Test]
    public void MemberReason_RejectsUnrepresentableLeaves()
    {
        const string source = """
            using System;
            public class BadHolder
            {
                public dynamic Dyn { get; set; } = null!;
                public unsafe int* Pointer { get; set; }
                public Span<int> Span { get; set; }
            }
            """;
        SparseShapeValidation.GetUnsupportedMemberReason(GetPropertyType(source, "BadHolder", "Dyn")).ShouldBe(SparseMemberShapeProblem.Dynamic);
        SparseShapeValidation.GetUnsupportedMemberReason(GetPropertyType(source, "BadHolder", "Pointer")).ShouldBe(SparseMemberShapeProblem.Pointer);
        SparseShapeValidation.GetUnsupportedMemberReason(GetPropertyType(source, "BadHolder", "Span")).ShouldBe(SparseMemberShapeProblem.RefLike);
    }

    [Test]
    public void MemberReason_RecursesThroughWrappers()
    {
        const string source = """
            using System.Collections.Generic;
            public class WrapHolder
            {
                public unsafe List<int*> Pointers { get; set; } = new();
                public unsafe int*[] PointerArray { get; set; } = [];
            }
            """;
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "WrapHolder", "Pointers"))
            .ShouldBe(SparseMemberShapeProblem.Pointer);
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "WrapHolder", "PointerArray"))
            .ShouldBe(SparseMemberShapeProblem.Pointer);
    }

    [Test]
    public void RootShape_ValidPartialPasses()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class ShapeRoot
            {
                public string? Name { get; set; }
            }
            """;
        var compilation = CreateCompilation(source);
        var model = compilation.GetTypeByMetadataName("ShapeRoot")!;
        var declaration = (Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax)model.DeclaringSyntaxReferences.Single().GetSyntax();
        SparseShapeValidation.ValidateRootShape(model, declaration, CancellationToken.None).ShouldBeNull();
    }

    [Test]
    public void RootShape_RejectsNonPartialNestedRefLikeAndFileLocal()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public class NonPartialRoot
            {
                public string? Name { get; set; }
            }
            public class Outer
            {
                [SparseModel]
                public partial class NestedRoot
                {
                    public string? Name { get; set; }
                }
            }
            [SparseModel]
            public ref partial struct RefRoot
            {
                public int Count { get; set; }
            }
            [SparseModel]
            public file partial class FileRoot
            {
                public string? Name { get; set; }
            }
            """;
        var compilation = CreateCompilation(source);
        SparseRootShapeProblem? Check(string identifier)
        {
            var tree = compilation.SyntaxTrees.Single();
            var semantic = compilation.GetSemanticModel(tree);
            var declaration = tree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                .Single(node => node.Identifier.Text == identifier);
            var model = (INamedTypeSymbol)semantic.GetDeclaredSymbol(declaration)!;
            return SparseShapeValidation.ValidateRootShape(model, declaration, CancellationToken.None);
        }

        Check("NonPartialRoot").ShouldBe(SparseRootShapeProblem.MustBePartial);
        Check("NestedRoot").ShouldBe(SparseRootShapeProblem.UnsupportedModel);
        Check("RefRoot").ShouldBe(SparseRootShapeProblem.RefLikeModel);
        Check("FileRoot").ShouldBe(SparseRootShapeProblem.FileLocalModel);
    }

    [Test]
    public void Generator_RefLikeRootReportsSpf002()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public ref partial struct RefModel
            {
                public int Count { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF002").ToImmutableArray();
        match.Length.ShouldBe(1);
        match[0].GetMessage().ShouldContain("RefModel");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_PointerMemberReportsSingleSpf008()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class PointerModel
            {
                public unsafe int* Value { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF008").ToImmutableArray();
        match.Length.ShouldBe(1, "expected exactly one SPF008, got: " + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage())));
        match[0].GetMessage().ShouldContain("Value");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_DynamicMemberReportsSingleSpf008()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class DynamicModel
            {
                public dynamic Value { get; set; } = null!;
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF008").ToImmutableArray();
        match.Length.ShouldBe(1, "expected exactly one SPF008, got: " + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage())));
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_DuplicateWireNameReportsSpf021()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using SparseTypes;
            [SparseModel]
            public partial class WireModel
            {
                [JsonPropertyName("dup")]
                public string? First { get; set; }
                [JsonPropertyName("dup")]
                public string? Second { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF021").ToImmutableArray();
        match.Length.ShouldBe(1, "expected exactly one SPF021, got: " + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage())));
        match[0].GetMessage().ShouldContain("dup");
        match[0].Location.IsInSource.ShouldBeTrue();
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_CamelCasePayloadNameCollisionReportsSpf021()
    {
        const string source = """
            using SparseTypes;
            [SparseModel]
            public partial class WireModel
            {
                public string? Name { get; set; }
                public string? name { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF021").ToImmutableArray();
        match.Length.ShouldBe(1, "expected exactly one SPF021, got: " + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage())));
        match[0].GetMessage().ShouldContain("name");
        match[0].Location.IsInSource.ShouldBeTrue();
        sources.ShouldBeEmpty();
    }
}
