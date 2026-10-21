using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Typed surface (scalar/nested transitions).</summary>
internal static class SparseChangeSetTransitionEmitter
{
    /// <summary>Emits typed members and returns private fields for deferred placement.</summary>
    internal static string AppendTypedSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        if (members.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        System.Text.StringBuilder? projectionFields = null;
        var runtime = dialect.RuntimeNamespace;
        SparseChangeSetNaming.ComputePublicNames(members, out var propNames, out var transNames);
        // Canonical-state readers and snapshot helpers move into the
        // operation container when relocating; the typed shells below stay.
        var helpers = target?.ChangeSetOperations ?? code;
        // Project before/after from canonical sparse storage; granular keyed
        // transitions expose whole endpoints only for presence changes.
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                continue;
            }

            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
            var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsKeyed(member) || IsDict(member))
            {
                var keyedBefore =
                    "__sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " && "
                    + KeyedWholeFlag(member)
                    + " ? "
                    + KeyedWholeBefore(member)
                    + " : default)";
                var keyedAfter =
                    "__sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " && "
                    + KeyedWholeFlag(member)
                    + " ? "
                    + KeyedWholeAfter(member)
                    + " : default)";
                if (target is not null)
                {
                    helpers.AppendLine(
                        "internal static "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.OpenBlock();
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLine("return " + SnapshotWrap(member, keyedBefore) + ";");
                    helpers.CloseBlock();
                    helpers.AppendLine(
                        "internal static "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.OpenBlock();
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLine("return " + SnapshotWrap(member, keyedAfter) + ";");
                    helpers.CloseBlock();
                }
                else
                {
                    helpers.AppendLine(
                        "private "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, keyedBefore)
                            + ";"
                    );
                    helpers.AppendLine(
                        "private "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, keyedAfter)
                            + ";"
                    );
                }
            }
            else
            {
                var scalarBefore =
                    "__sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " ? "
                    + BeforeField(member)
                    + " : default)";
                var scalarAfter =
                    "__sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " ? "
                    + AfterField(member)
                    + " : default)";
                if (target is not null)
                {
                    helpers.AppendLine(
                        "internal static "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.OpenBlock();
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLine("return " + SnapshotWrap(member, scalarBefore) + ";");
                    helpers.CloseBlock();
                    helpers.AppendLine(
                        "internal static "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.OpenBlock();
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLine("return " + SnapshotWrap(member, scalarAfter) + ";");
                    helpers.CloseBlock();
                }
                else
                {
                    helpers.AppendLine(
                        "private "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, scalarBefore)
                            + ";"
                    );
                    helpers.AppendLine(
                        "private "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, scalarAfter)
                            + ";"
                    );
                }
            }
            AppendSnapshotHelper(helpers, member, opt);
        }
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            if (IsSet(member))
            {
                AppendSetTransition(
                    code,
                    member,
                    prop,
                    transNames[member.Id],
                    runtime,
                    dialect.HashSetImplementsReadOnlySet,
                    target,
                    dialect
                );
            }
            else if (IsScalar(member))
            {
                AppendScalarTransition(
                    code,
                    member,
                    prop,
                    transNames[member.Id],
                    runtime,
                    target,
                    dialect
                );
            }
            else if (IsNested(member))
            {
                AppendNestedProperty(code, member, prop, dialect, target);
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var cacheBuilder = new System.Text.StringBuilder(
                    "__sparse_projection_cache_"
                ).Append(member.Id);
                var cacheName = cacheBuilder.ToString();
                while (
                    propNames.Values.Contains(cacheName) || transNames.Values.Contains(cacheName)
                )
                {
                    cacheBuilder.Append('_');
                    cacheName = cacheBuilder.ToString();
                }
                if (IsKeyed(member))
                {
                    AppendKeyedTransition(
                        code,
                        member,
                        prop,
                        transNames[member.Id],
                        cacheName,
                        dialect,
                        target
                    );
                }
                else
                {
                    AppendDictTransition(
                        code,
                        member,
                        prop,
                        transNames[member.Id],
                        cacheName,
                        dialect,
                        target
                    );
                }
                projectionFields ??= new System.Text.StringBuilder();
                projectionFields
                    .Append("private ")
                    .Append(transNames[member.Id])
                    .Append("? ")
                    .Append(cacheName)
                    .AppendLine(";");
            }
        }
        return projectionFields?.ToString() ?? string.Empty;
    }

    /// <summary>Whether member values need a defensive container snapshot.</summary>
    /// <remarks>
    /// Collection containers are caller-mutable aliases; plain scalars and
    /// nested change sets need no snapshot.
    /// </remarks>
    internal static bool NeedsSnapshot(SparseMemberModel member) =>
        !IsNested(member) && member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported;

    private static string SnapshotWrap(SparseMemberModel member, string expression) =>
        NeedsSnapshot(member)
            ? "__SparseSnapshot_" + member.Id + "(" + expression + ")"
            : expression;

    /// <summary>Emits the defensive container snapshot for one member.</summary>
    /// <remarks>The container is copied so later caller-side mutation cannot alter retained history, while element values stay shared.</remarks>
    internal static void AppendSnapshotHelper(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string opt
    )
    {
        if (!NeedsSnapshot(member))
        {
            return;
        }

        var valueType = FragmentValueType(member);
        // A branch is emitted only when its copy is provably assignable to the
        // declared member type.
        var bareValueType = valueType.TrimEnd('?');
        code.AppendLine(
            "/// <summary>Defensive container snapshot for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLine(
            "private static " + opt + " __SparseSnapshot_" + member.Id + "(" + opt + " value)"
        );
        code.OpenBlock();
        code.AppendLine("if (!value.IsPresent || (object?)value.Value is null) return value;");
        code.AppendLine("var __source" + member.Id + " = (object)value.Value!;");
        if (member.Collection.CloneKind == SparseCloneCollectionKind.Dictionary)
        {
            var keyType = KeyTypeOf(member);
            var elementValueType = ValueTypeOf(member);
            var dictInterfaces = new[]
            {
                "global::System.Collections.Generic.IDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "global::System.Collections.Generic.IReadOnlyDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
            };
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "__dict" + member.Id,
                "new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">(__dict"
                    + member.Id
                    + ", __dict"
                    + member.Id
                    + ".Comparer)",
                dictInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.SortedDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "__sorted" + member.Id,
                "new global::System.Collections.Generic.SortedDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">(__sorted"
                    + member.Id
                    + ", __sorted"
                    + member.Id
                    + ".Comparer)",
                dictInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.SortedList<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "__sortedList" + member.Id,
                "new global::System.Collections.Generic.SortedList<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">(__sortedList"
                    + member.Id
                    + ", __sortedList"
                    + member.Id
                    + ".Comparer)",
                dictInterfaces
            );
        }
        else if (member.Collection.CloneKind == SparseCloneCollectionKind.Set)
        {
            var elementType = ElementTypeOf(member);
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.HashSet<" + elementType + ">",
                "__set" + member.Id,
                "new global::System.Collections.Generic.HashSet<"
                    + elementType
                    + ">(__set"
                    + member.Id
                    + ", __set"
                    + member.Id
                    + ".Comparer)",
                [
                    "global::System.Collections.Generic.ISet<" + elementType + ">",
                    "global::System.Collections.Generic.IReadOnlySet<" + elementType + ">",
                ]
            );
        }
        else
        {
            var elementType = ElementTypeOf(member);
            var listInterfaces = new[]
            {
                "global::System.Collections.Generic.IList<" + elementType + ">",
                "global::System.Collections.Generic.ICollection<" + elementType + ">",
                "global::System.Collections.Generic.IEnumerable<" + elementType + ">",
                "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">",
                "global::System.Collections.Generic.IReadOnlyCollection<" + elementType + ">",
            };
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.List<" + elementType + ">",
                "__list" + member.Id,
                "new global::System.Collections.Generic.List<"
                    + elementType
                    + ">(__list"
                    + member.Id
                    + ")",
                listInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                elementType + "[]",
                "__array" + member.Id,
                "(" + elementType + "[])__array" + member.Id + ".Clone()",
                listInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.ObjectModel.Collection<" + elementType + ">",
                "__collection" + member.Id,
                "new global::System.Collections.ObjectModel.Collection<"
                    + elementType
                    + ">(new global::System.Collections.Generic.List<"
                    + elementType
                    + ">(__collection"
                    + member.Id
                    + "))",
                listInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.ObjectModel.ObservableCollection<" + elementType + ">",
                "__observable" + member.Id,
                "new global::System.Collections.ObjectModel.ObservableCollection<"
                    + elementType
                    + ">(__observable"
                    + member.Id
                    + ")",
                listInterfaces
            );
        }
        code.AppendLine("return value;");
        code.CloseBlock();
    }

    private static void AppendSnapshotBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string opt,
        string valueType,
        string bareValueType,
        string sourceType,
        string local,
        string copyExpression,
        IReadOnlyList<string> implementedInterfaces
    )
    {
        if (
            !string.Equals(bareValueType, sourceType, System.StringComparison.Ordinal)
            && !implementedInterfaces.Any(candidate =>
                string.Equals(bareValueType, candidate, System.StringComparison.Ordinal)
            )
        )
        {
            return;
        }

        code.AppendLine("if (__source" + member.Id + " is " + sourceType + " " + local + ")");
        code.OpenBlock();
        code.AppendLine(
            "return " + opt + ".Present((" + valueType + ")(object)" + copyExpression + ");"
        );
        code.CloseBlock();
    }

    internal static void AppendScalarTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime,
        SparseOperationTarget? target = null,
        SparseFragmentPatchEmitter.SparsePatchDialect? dialect = null
    )
    {
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        if (dialect?.TransitionContracts?.ScalarTransitionType is string sharedTransitionType)
        {
            var valueType = FragmentValueType(member);
            var transitionType = sharedTransitionType + "<" + valueType + ">";
            code.AppendLine(
                "/// <summary>Gets the typed transition for member '"
                    + member.Property.Name
                    + "'.</summary>"
            );
            SparseChangePayloadJsonBridgeEmitter.AppendBareJsonIgnore(code, dialect.Value.EmitJson);
            if (target is not null)
            {
                code.AppendLine(
                    "public "
                        + transitionType
                        + " "
                        + prop
                        + " => "
                        + target.ChangeSetOperationsType
                        + ".__SparseGet_"
                        + member.Id
                        + "(this);"
                );
                var operations = target.ChangeSetOperations;
                operations.AppendLine(
                    "internal static "
                        + transitionType
                        + " __SparseGet_"
                        + member.Id
                        + "(ChangeSet self)"
                );
                operations.OpenBlock();
                operations.AppendLine("var __b = __SparseBefore_" + member.Id + "(self);");
                operations.AppendLine("var __a = __SparseAfter_" + member.Id + "(self);");
                operations.AppendLine(
                    "return new "
                        + transitionType
                        + "(__b, __a, !Fragment.__SparseEqual_"
                        + member.Id
                        + "(__b, __a));"
                );
                operations.CloseBlock();
                return;
            }

            code.AppendLine("public " + transitionType + " " + prop);
            code.OpenBlock();
            code.AppendLine("get");
            code.OpenBlock();
            code.AppendLine("var __b = __SparseBefore_" + member.Id + "();");
            code.AppendLine("var __a = __SparseAfter_" + member.Id + "();");
            code.AppendLine(
                "return new "
                    + transitionType
                    + "(__b, __a, !Fragment.__SparseEqual_"
                    + member.Id
                    + "(__b, __a));"
            );
            code.CloseBlock();
            code.CloseBlock();
            return;
        }

        code.AppendLine(
            "/// <summary>Typed transition for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLine(
            "public readonly struct "
                + trans
                + (
                    dialect.HasValue
                        ? SparseChangeSetContractEmitter.ScalarBases(
                            dialect.Value,
                            FragmentValueType(member)
                        )
                        : string.Empty
                )
        );
        code.OpenBlock();
        code.AppendLine(
            "/// <summary>Gets whether the member changed between the endpoints.</summary>"
        );
        code.AppendLine("public bool IsChanged => _isChanged;");
        code.AppendLine("/// <summary>Gets the value before the transition.</summary>");
        code.AppendLine("public " + opt + " Before => _before;");
        code.AppendLine("/// <summary>Gets the value after the transition.</summary>");
        code.AppendLine("public " + opt + " After => _after;");
        code.AppendLine(
            "internal " + trans + "(" + opt + " before, " + opt + " after, bool isChanged)"
        );
        code.OpenBlock();
        code.AppendLine("_before = before;");
        code.AppendLine("_after = after;");
        code.AppendLine("_isChanged = isChanged;");
        code.CloseBlock();
        code.AppendLine("private readonly " + opt + " _before;");
        code.AppendLine("private readonly " + opt + " _after;");
        code.AppendLine("private readonly bool _isChanged;");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Gets the typed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        SparseChangePayloadJsonBridgeEmitter.AppendBareJsonIgnore(code, dialect?.EmitJson ?? true);
        if (target is not null)
        {
            code.AppendLine(
                "public "
                    + trans
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseGet_"
                    + member.Id
                    + "(this);"
            );
            var ops = target.ChangeSetOperations;
            ops.AppendLine("/// <summary>Projects the typed transition for one member.</summary>");
            ops.AppendLine(
                "internal static " + trans + " __SparseGet_" + member.Id + "(ChangeSet self)"
            );
            ops.OpenBlock();
            ops.AppendLine("var __b = __SparseBefore_" + member.Id + "(self);");
            ops.AppendLine("var __a = __SparseAfter_" + member.Id + "(self);");
            ops.AppendLine(
                "return new "
                    + trans
                    + "(__b, __a, !Fragment.__SparseEqual_"
                    + member.Id
                    + "(__b, __a));"
            );
            ops.CloseBlock();
            return;
        }
        code.AppendLine("public " + trans + " " + prop);
        code.OpenBlock();
        code.AppendLine("get");
        code.OpenBlock();
        code.AppendLine("var __b = __SparseBefore_" + member.Id + "();");
        code.AppendLine("var __a = __SparseAfter_" + member.Id + "();");
        code.AppendLine(
            "return new "
                + trans
                + "(__b, __a, !Fragment.__SparseEqual_"
                + member.Id
                + "(__b, __a));"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendSetTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime
    ) => AppendSetTransition(code, member, prop, trans, runtime, supportsReadOnlySet: true);

    internal static void AppendSetTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime,
        bool supportsReadOnlySet,
        SparseOperationTarget? target = null,
        SparseFragmentPatchEmitter.SparsePatchDialect? dialect = null
    )
    {
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var elementType = ElementTypeOf(member);
        var readOnlyList = "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
        code.AppendLine(
            "/// <summary>Typed set transition for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLine(
            "public sealed class "
                + trans
                + (
                    dialect.HasValue
                        ? SparseChangeSetContractEmitter.ScalarBases(
                            dialect.Value,
                            FragmentValueType(member)
                        )
                        : string.Empty
                )
        );
        code.OpenBlock();
        code.AppendLine(
            "/// <summary>Whether this set transition contains no semantic changes.</summary>"
        );
        code.AppendLine("public bool IsEmpty { get; }");
        code.AppendLine(
            "/// <summary>Whether this set transition contains any semantic changes.</summary>"
        );
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        code.AppendLine(
            "/// <summary>The presence-aware set value before the transition.</summary>"
        );
        code.AppendLine("public " + opt + " Before { get; }");
        code.AppendLine(
            "/// <summary>The presence-aware set value after the transition.</summary>"
        );
        code.AppendLine("public " + opt + " After { get; }");
        code.AppendLine("/// <summary>Values present only in the after set.</summary>");
        code.AppendLine("public " + readOnlyList + " Added { get; }");
        code.AppendLine("/// <summary>Values present only in the before set.</summary>");
        code.AppendLine("public " + readOnlyList + " Removed { get; }");
        code.AppendLine(
            "internal "
                + trans
                + "("
                + opt
                + " before, "
                + opt
                + " after, bool isEmpty, global::System.Collections.Generic.List<"
                + elementType
                + "> added, global::System.Collections.Generic.List<"
                + elementType
                + "> removed)"
        );
        code.OpenBlock();
        code.AppendLine("Before = before;");
        code.AppendLine("After = after;");
        code.AppendLine("IsEmpty = isEmpty;");
        code.AppendLine("Added = added.AsReadOnly();");
        code.AppendLine("Removed = removed.AsReadOnly();");
        code.CloseBlock();
        // IReadOnlySet<T> only exists on netstandard2.1 and later.
        var contains = supportsReadOnlySet
            ? "((object?)values.Value is global::System.Collections.Generic.ISet<"
                + elementType
                + "> set ? set.Contains(value) : (object?)values.Value is global::System.Collections.Generic.IReadOnlySet<"
                + elementType
                + "> readOnly ? readOnly.Contains(value) : global::System.Linq.Enumerable.Contains(values.Value!, value))"
            : "((object?)values.Value is global::System.Collections.Generic.ISet<"
                + elementType
                + "> set ? set.Contains(value) : global::System.Linq.Enumerable.Contains(values.Value!, value))";
        code.AppendLine(
            "internal static bool __Contains("
                + opt
                + " values, "
                + elementType
                + " value) => values.IsPresent && (object?)values.Value is not null && "
                + contains
                + ";"
        );
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Gets the typed set transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        SparseChangePayloadJsonBridgeEmitter.AppendBareJsonIgnore(code, dialect?.EmitJson ?? true);
        if (target is not null)
        {
            code.AppendLine(
                "public "
                    + trans
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseGet_"
                    + member.Id
                    + "(this);"
            );
            var ops = target.ChangeSetOperations;
            ops.AppendLine(
                "/// <summary>Projects the typed set transition for one member.</summary>"
            );
            ops.AppendLine(
                "internal static " + trans + " __SparseGet_" + member.Id + "(ChangeSet self)"
            );
            ops.OpenBlock();
            ops.AppendLine("var __b = __SparseBefore_" + member.Id + "(self);");
            ops.AppendLine("var __a = __SparseAfter_" + member.Id + "(self);");
            AppendSetProjectionBody(ops, member, trans, elementType);
            ops.CloseBlock();
            return;
        }
        code.AppendLine("public " + trans + " " + prop);
        code.OpenBlock();
        code.AppendLine("get");
        code.OpenBlock();
        code.AppendLine("var __b = __SparseBefore_" + member.Id + "();");
        code.AppendLine("var __a = __SparseAfter_" + member.Id + "();");
        AppendSetProjectionBody(code, member, trans, elementType);
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendSetProjectionBody(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string elementType
    )
    {
        code.AppendLine("var __isEmpty = Fragment.__SparseEqual_" + member.Id + "(__b, __a);");
        code.AppendLine(
            "var __added = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLine(
            "if (!__isEmpty && __a.IsPresent && (object?)__a.Value is not null) foreach (var __item in __a.Value!) if (!"
                + trans
                + ".__Contains(__b, __item)) __added.Add(__item);"
        );
        code.AppendLine(
            "var __removed = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLine(
            "if (!__isEmpty && __b.IsPresent && (object?)__b.Value is not null) foreach (var __item in __b.Value!) if (!"
                + trans
                + ".__Contains(__a, __item)) __removed.Add(__item);"
        );
        code.AppendLine("return new " + trans + "(__b, __a, __isEmpty, __added, __removed);");
    }

    internal static void AppendNestedProperty(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var childCs = ChildChangeSet(member, dialect);
        var runtime = dialect.RuntimeNamespace;
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        code.AppendLine(
            "/// <summary>Gets the nested typed change set for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        SparseChangePayloadJsonBridgeEmitter.AppendBareJsonIgnore(code, dialect.EmitJson);
        if (target is not null)
        {
            code.AppendLine(
                "public "
                    + childCs
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseGet_"
                    + member.Id
                    + "(this);"
            );
            var ops = target.ChangeSetOperations;
            ops.AppendLine(
                "/// <summary>Projects the nested typed change set for one member.</summary>"
            );
            ops.AppendLine(
                "internal static " + childCs + " __SparseGet_" + member.Id + "(ChangeSet self)"
            );
            ops.OpenBlock();
            SparseChangeSetBasicsEmitter.AppendMemberStateAliases(ops, member);
            AppendNestedProjectionBody(ops, member, esc, childCs, runtime);
            ops.CloseBlock();
            return;
        }
        code.AppendLine("public " + childCs + " " + prop);
        code.OpenBlock();
        code.AppendLine("get");
        code.OpenBlock();
        AppendNestedProjectionBody(code, member, esc, childCs, runtime);
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendNestedProjectionBody(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string childCs,
        string runtime
    )
    {
        code.AppendLine("if (__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine(
            "var __wb = __sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                + esc
                + " : default;"
        );
        code.AppendLine(
            "var __wa = __sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                + esc
                + " : default;"
        );
        code.AppendLine("return " + childCs + ".Between(__wb, __wa);");
        code.CloseBlock();
        code.AppendLine(
            "return "
                + NestedField(member)
                + " ?? "
                + childCs
                + ".Between("
                + runtime
                + "Optional<"
                + member.ChildFragmentType
                + "?>.Missing, "
                + runtime
                + "Optional<"
                + member.ChildFragmentType
                + "?>.Missing);"
        );
    }
}
