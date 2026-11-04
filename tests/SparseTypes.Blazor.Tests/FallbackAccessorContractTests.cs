using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor.Tests;

public sealed class FallbackAccessorContractTests
{
    private sealed class PlainModel
    {
        public string Writable { get; set; } = "w";

        public string GetterOnly { get; } = "g";

        public string PrivateSet { get; private set; } = "p";

        public string InitOnly { get; init; } = "i";

        public string WriteOnlyPublic
        {
            private get => "secret";
            set { }
        }
    }

    // No descriptor capability, so bindings take the reflection fallback.
    private sealed class PlainSession(PlainModel model) : ISparseEditSession<PlainModel>
    {
        public PlainModel Model => model;

        public bool HasChanges => false;

        public void AcceptChanges() { }
    }

    private static SparseFieldBinding<PlainModel, object?> Bind(PlainModel model, string path)
    {
        var session = new PlainSession(model);
        return session.Bind(SparsePath.Parse<PlainModel>(path), new EditContext(model));
    }

    [Test]
    public void WritableMemberIsEditable()
    {
        var model = new PlainModel();
        var binding = Bind(model, nameof(PlainModel.Writable));

        binding.IsReadOnly.ShouldBeFalse();
        binding.TrySet("x", out var error).ShouldBeTrue();
        error.ShouldBeNull();
        model.Writable.ShouldBe("x");
    }

    [Test]
    [Arguments(nameof(PlainModel.GetterOnly))]
    [Arguments(nameof(PlainModel.PrivateSet))]
    [Arguments(nameof(PlainModel.InitOnly))]
    public void NonPublicOrInitOnlySettersAreReadOnlyAndRejectWrites(string member)
    {
        var model = new PlainModel();
        var binding = Bind(model, member);

        binding.IsReadOnly.ShouldBeTrue();
        binding.TrySet("x", out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        model.GetterOnly.ShouldBe("g");
        model.PrivateSet.ShouldBe("p");
        model.InitOnly.ShouldBe("i");
    }

    [Test]
    public void PrivateGetterIsNotReadThroughReflection()
    {
        var binding = Bind(new PlainModel(), nameof(PlainModel.WriteOnlyPublic));

        binding.TryGetValue(out var value, out var error).ShouldBeFalse();
        value.ShouldBeNull();
        error.ShouldNotBeNull();
    }
}
