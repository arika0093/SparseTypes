namespace SparseTypes.Tests;

public sealed class SparsePathValueTests
{
    [Test]
    public void DefaultPathsHaveStableRootIdentity()
    {
        var path = default(SparsePath);
        path.ShouldBe(SparsePath.Root<object>());
        path.GetHashCode().ShouldBe(SparsePath.Root<object>().GetHashCode());
        path.IsRoot.ShouldBeTrue();
        path.Depth.ShouldBe(0);
        path.Segments.Count.ShouldBe(0);
        path.Parent.ShouldBeNull();
        path.ToString().ShouldBe("$root");
        path.Member("Name").Parent.ShouldBe(path);

        var typed = default(SparsePath<Settings, string>);
        typed.Path.ShouldBe(SparsePath.Root<Settings>());
        typed.ShouldBe(new SparsePath<Settings, string>(SparsePath.Root<Settings>()));
        typed.GetHashCode().ShouldBe(typed.Path.GetHashCode());
        SparsePath converted = typed;
        converted.RootType.ShouldBe(typeof(Settings));
        new Dictionary<SparsePath, int> { [path] = 1 }[SparsePath.Root<object>()]
            .ShouldBe(1);
    }

    [Test]
    public void DefaultBuildersRetainTheirRootAndCollection()
    {
        var builder = Settings.SparsePath;
        builder.Label.Path.ShouldBe(Settings.SparsePath.Label.Path);
        SparsePath root = builder;
        root.ShouldBe(SparsePath.Root<Settings>());

        var items = KeyedServerHolder.SparsePath.Items;
        items.Key("a").Name.Path.ShouldBe(KeyedServerHolder.SparsePath.Items.Key("a").Name.Path);
        SparsePath collection = items;
        collection.ShouldBe(SparsePath.Root<KeyedServerHolder>().Member("Items"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void PublicConstructorsKeepCallerArraysDetached(bool enumerable)
    {
        SparsePathSegment[] segments = [SparsePathSegment.Member("Name")];
        var path = enumerable
            ? new SparsePath(typeof(Settings), (IEnumerable<SparsePathSegment>)segments)
            : new SparsePath(typeof(Settings), segments);
        var child = path.Member("Child");
        var prefixed = path.Prepend(SparsePath.Root<Settings>().Member("Parent"));

        segments[0] = SparsePathSegment.Member("Changed");

        path.ToString().ShouldBe("Name");
        child.ToString().ShouldBe("Name.Child");
        child.Parent.ShouldBe(path);
        prefixed.ToString().ShouldBe("Parent.Name");
        ((object)path.Segments).ShouldNotBeOfType<IList<SparsePathSegment>>();
    }

    [Test]
    public void PrefixPathsExposeOnlyTheirLogicalSegments()
    {
        var path = SparsePath.Root<Settings>().Member("Items").Key(42).Member("Name").At(3);
        var prefix = SparseResolutionWorkspace.StagingKey(path, 3);
        var expected = SparsePath.Root<Settings>().Member("Items").Key(42).Member("Name");

        prefix.ShouldBe(expected);
        prefix.GetHashCode().ShouldBe(expected.GetHashCode());
        prefix.Depth.ShouldBe(3);
        prefix
            .Segments.Select(static segment => segment.Kind)
            .ShouldBe(new[] { SparsePathSegmentKind.Member, SparsePathSegmentKind.Key, SparsePathSegmentKind.Member });
        prefix.ToString().ShouldBe("Items[\"42\"].Name");
        prefix.IsAncestorOf(path).ShouldBeTrue();
        path.Parent.ShouldBe(SparsePath.Root<Settings>().Member("Items").Key(42).Member("Name"));

        SparseResolutionWorkspace.StagingKey(path, 0).ShouldBe(SparsePath.Root<Settings>());
        Should.Throw<ArgumentOutOfRangeException>(() => SparseResolutionWorkspace.StagingKey(path, path.Depth + 1));
    }

    [Test]
    public void DeepPrefixAndTypedKeyIdentityArePreserved()
    {
        var path = SparsePath.Root<Settings>();
        for (var index = 0; index < 32; index++)
        {
            path = path.Member("Member" + index);
        }

        var prefix = SparseResolutionWorkspace.StagingKey(path, 16);
        prefix.Depth.ShouldBe(16);
        prefix.Segments.Count.ShouldBe(16);
        prefix.Segments[15].Name.ShouldBe("Member15");
        prefix.ToString().ShouldBe(string.Join(".", Enumerable.Range(0, 16).Select(static index => "Member" + index)));

        var integerKey = SparsePath.Root<Settings>().Member("Items").Key(42);
        var stringKey = SparsePath.Root<Settings>().Member("Items").Key("42");
        var integerKeyPrefix = SparseResolutionWorkspace.StagingKey(integerKey.Member("Name"), 2);
        integerKeyPrefix.ShouldBe(integerKey);
        integerKeyPrefix.ShouldNotBe(stringKey);
        integerKeyPrefix.ToString().ShouldBe(stringKey.ToString());
    }

    [Test]
    public void SegmentViewsSupportDefaultAndIndependentEnumeration()
    {
        var empty = default(SparsePathSegments);
        empty.Count.ShouldBe(0);
        empty.ToArray().ShouldBeEmpty();
        Should.Throw<IndexOutOfRangeException>(() => empty[0]);

        var path = SparsePath.Root<Settings>().Member("Parent").Member("Name");
        var view = path.Segments;
        var names = new List<string>();
        foreach (var segment in view)
        {
            names.Add(segment.Name);
        }
        names.ShouldBe(new[] { "Parent", "Name" });
        ((IReadOnlyList<SparsePathSegment>)view)[1].Name.ShouldBe("Name");
        ((System.Collections.IEnumerable)view).Cast<SparsePathSegment>().Count().ShouldBe(2);

        using var first = view.GetEnumerator();
        using var second = view.GetEnumerator();
        Should.Throw<InvalidOperationException>(() => first.Current);
        first.MoveNext().ShouldBeTrue();
        first.Current.Name.ShouldBe("Parent");
        second.MoveNext().ShouldBeTrue();
        first.MoveNext().ShouldBeTrue();
        first.Current.Name.ShouldBe("Name");
        second.Current.Name.ShouldBe("Parent");
        first.MoveNext().ShouldBeFalse();
        first.MoveNext().ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => first.Current);
        first.Reset();
        first.MoveNext().ShouldBeTrue();
        first.Current.Name.ShouldBe("Parent");
    }
}
