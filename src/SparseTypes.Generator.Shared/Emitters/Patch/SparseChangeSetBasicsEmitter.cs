using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Canonical sparse field/type helpers plus ChangeSet storage, constructor, and emptiness.</summary>
internal static class SparseChangeSetBasicsEmitter
{
    internal static bool IsScalar(SparseMemberModel m) => m.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    internal static bool IsNested(SparseMemberModel m) => m.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    internal static bool IsKeyed(SparseMemberModel m) => SparseKeyedCollectionEmitter.IsKeyedSequence(m);

    internal static bool IsDict(SparseMemberModel m) => SparseKeyedCollectionEmitter.IsDictionary(m);

    internal static bool IsSet(SparseMemberModel m) => m.Collection.Kind == SparseCollectionKind.Set && !IsKeyed(m) && !IsDict(m);

    internal static string FragmentValueType(SparseMemberModel m) => SparseFragmentEmitHelpers.FragmentValueType(m);

    internal static string ChildChangeSet(SparseMemberModel m, SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.ChildChangeSetName(m);

    internal static string BoxExpression(SparseFragmentPatchEmitter.SparsePatchDialect dialect, string value)
    {
        var boxType = dialect.OptionalBoxType ?? dialect.TransitionContracts?.OptionalBoxType;
        return boxType is null ? "__SparseBox(" + value + ")" : boxType + ".ToObject(" + value + ")";
    }

    internal static string BeforeField(SparseMemberModel m) => "__sparse_before_" + m.Id;

    internal static string AfterField(SparseMemberModel m) => "__sparse_after_" + m.Id;

    internal static string HasField(SparseMemberModel m) => "__sparse_has_" + m.Id;

    internal static string NestedField(SparseMemberModel m) => "__sparse_nested_" + m.Id;

    // Sparse keyed/dictionary canonical storage field names.
    internal static string KeyedWholeFlag(SparseMemberModel m) => "__sparse_kwhole_" + m.Id;

    internal static string KeyedWholeBefore(SparseMemberModel m) => "__sparse_kwholeBefore_" + m.Id;

    internal static string KeyedWholeAfter(SparseMemberModel m) => "__sparse_kwholeAfter_" + m.Id;

    internal static string KeyedItems(SparseMemberModel m) => "__sparse_kitems_" + m.Id;

    internal static string KeyedBeforeOrder(SparseMemberModel m) => "__sparse_kbeforeOrder_" + m.Id;

    internal static string KeyedAfterOrder(SparseMemberModel m) => "__sparse_kafterOrder_" + m.Id;

    // Temporary-identity orders for tempkey members: complete Guid sequences
    // parallel to the sentinel slots of the key orders above.
    internal static string KeyedTempBeforeOrder(SparseMemberModel m) => "__sparse_ktempbeforeOrder_" + m.Id;

    internal static string KeyedTempAfterOrder(SparseMemberModel m) => "__sparse_ktempafterOrder_" + m.Id;

    internal static string KeyedOrderChangedExpression(SparseMemberModel member, string facade)
    {
        var expression = $"!{facade}.KeyOrderEquals<{KeyTypeOf(member)}>({KeyedBeforeOrder(member)}, {KeyedAfterOrder(member)})";
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            expression +=
                $" || ({KeyedTempBeforeOrder(member)} is not null && {KeyedTempAfterOrder(member)} is not null && !{facade}.KeyOrderEquals<global::System.Guid>({KeyedTempBeforeOrder(member)}, {KeyedTempAfterOrder(member)}))";
        }

        return expression;
    }

    internal static void AppendFields(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseChangeSetStorageLayout storage
    )
    {
        // Canonical sparse storage is private; the nested ChangeSet.__Core
        // reads it directly.
        code.AppendLine("private readonly global::System.DateTimeOffset? __sparse_recordedAt;");
        code.AppendLine("private readonly bool __sparse_hasWhole;");
        storage.AppendFields(code, "__sparse_wholeBefore", "Fragment?");
        storage.AppendFields(code, "__sparse_wholeAfter", "Fragment?");
        ComputePublicNames(members, out _, out var transNames);
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLine("private readonly " + ChildChangeSet(member, dialect) + "? " + NestedField(member) + ";");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                // Canonical sparse storage. No full member snapshots.
                var trans = transNames[member.Id];
                var keyType = KeyTypeOf(member);
                code.AppendLine("private readonly bool " + HasField(member) + ";");
                code.AppendLine("private readonly bool " + KeyedWholeFlag(member) + ";");
                storage.AppendFields(code, KeyedWholeBefore(member), FragmentValueType(member));
                storage.AppendFields(code, KeyedWholeAfter(member), FragmentValueType(member));
                code.AppendLine("private readonly global::System.Collections.Generic.List<" + trans + ".Item>? " + KeyedItems(member) + ";");
                if (IsKeyed(member))
                {
                    code.AppendLine("private readonly global::System.Collections.Generic.List<" + keyType + ">? " + KeyedBeforeOrder(member) + ";");
                    code.AppendLine("private readonly global::System.Collections.Generic.List<" + keyType + ">? " + KeyedAfterOrder(member) + ";");
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLine(
                            "private readonly global::System.Collections.Generic.List<global::System.Guid>? " + KeyedTempBeforeOrder(member) + ";"
                        );
                        code.AppendLine(
                            "private readonly global::System.Collections.Generic.List<global::System.Guid>? " + KeyedTempAfterOrder(member) + ";"
                        );
                    }
                }
            }
            else
            {
                storage.AppendFields(code, BeforeField(member), FragmentValueType(member));
                storage.AppendFields(code, AfterField(member), FragmentValueType(member));
                code.AppendLine("private readonly bool " + HasField(member) + ";");
            }
        }
    }

    internal static void AppendConstructor(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseChangeSetStorageLayout storage
    )
    {
        ComputePublicNames(members, out _, out var transNames);
        var parts = new List<string> { "bool hasWhole", optionalFragment + " wholeBefore", optionalFragment + " wholeAfter" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                parts.Add(ChildChangeSet(member, dialect) + "? nested" + member.Id);
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var trans = transNames[member.Id];
                var keyType = KeyTypeOf(member);
                parts.Add("bool has" + member.Id);
                parts.Add("bool whole" + member.Id);
                parts.Add(opt + " wholeBefore" + member.Id);
                parts.Add(opt + " wholeAfter" + member.Id);
                parts.Add("global::System.Collections.Generic.List<" + trans + ".Item>? items" + member.Id);
                if (IsKeyed(member))
                {
                    parts.Add("global::System.Collections.Generic.List<" + keyType + ">? beforeOrder" + member.Id);
                    parts.Add("global::System.Collections.Generic.List<" + keyType + ">? afterOrder" + member.Id);
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        parts.Add("global::System.Collections.Generic.List<global::System.Guid>? tempBeforeOrder" + member.Id);
                        parts.Add("global::System.Collections.Generic.List<global::System.Guid>? tempAfterOrder" + member.Id);
                    }
                }
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                parts.Add(opt + " before" + member.Id);
                parts.Add(opt + " after" + member.Id);
                parts.Add("bool has" + member.Id);
            }
        }
        parts.Add("global::System.DateTimeOffset? recordedAt = null");
        // The canonical-state constructor is private; the nested __Core builds
        // instances directly. No external container constructs ChangeSets.
        code.AppendLine("private ChangeSet(" + string.Join(", ", parts) + ")");
        code.OpenBlock();
        code.AppendLine("__sparse_recordedAt = recordedAt;");
        code.AppendLine("__sparse_hasWhole = hasWhole;");
        storage.AppendAssignment(code, "__sparse_wholeBefore", "wholeBefore");
        storage.AppendAssignment(code, "__sparse_wholeAfter", "wholeAfter");
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLine(NestedField(member) + " = nested" + member.Id + ";");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                code.AppendLine(HasField(member) + " = has" + member.Id + ";");
                code.AppendLine(KeyedWholeFlag(member) + " = whole" + member.Id + ";");
                storage.AppendAssignment(code, KeyedWholeBefore(member), "wholeBefore" + member.Id);
                storage.AppendAssignment(code, KeyedWholeAfter(member), "wholeAfter" + member.Id);
                code.AppendLine(KeyedItems(member) + " = items" + member.Id + ";");
                if (IsKeyed(member))
                {
                    code.AppendLine(KeyedBeforeOrder(member) + " = beforeOrder" + member.Id + ";");
                    code.AppendLine(KeyedAfterOrder(member) + " = afterOrder" + member.Id + ";");
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLine(KeyedTempBeforeOrder(member) + " = tempBeforeOrder" + member.Id + ";");
                        code.AppendLine(KeyedTempAfterOrder(member) + " = tempAfterOrder" + member.Id + ";");
                    }
                }
            }
            else
            {
                storage.AppendAssignment(code, BeforeField(member), "before" + member.Id);
                storage.AppendAssignment(code, AfterField(member), "after" + member.Id);
                code.AppendLine(HasField(member) + " = has" + member.Id + ";");
            }
        }
        code.CloseBlock();
    }

    internal static void AppendRecordedAtAccessors(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        code.AppendLine("/// <summary>Gets the time this transition was recorded.</summary>");
        code.AppendLine("public global::System.DateTimeOffset? RecordedAt => __sparse_recordedAt;");
        code.AppendLine("/// <summary>Returns a copy with the specified recording time.</summary>");
        code.AppendLine("/// <param name=\"recordedAt\">The recording time, or null to remove it.</param>");
        code.AppendLine("/// <returns>A change set with unchanged transition data.</returns>");
        code.AppendLine("public ChangeSet WithRecordedAt(global::System.DateTimeOffset? recordedAt)");
        code.OpenBlock();
        var cloneArgs = new List<string> { "__sparse_hasWhole", "__sparse_wholeBefore", "__sparse_wholeAfter" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                cloneArgs.Add(NestedField(member));
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                cloneArgs.Add(HasField(member));
                cloneArgs.Add(KeyedWholeFlag(member));
                cloneArgs.Add(KeyedWholeBefore(member));
                cloneArgs.Add(KeyedWholeAfter(member));
                cloneArgs.Add(KeyedItems(member));
                if (IsKeyed(member))
                {
                    cloneArgs.Add(KeyedBeforeOrder(member));
                    cloneArgs.Add(KeyedAfterOrder(member));
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        cloneArgs.Add(KeyedTempBeforeOrder(member));
                        cloneArgs.Add(KeyedTempAfterOrder(member));
                    }
                }
            }
            else
            {
                cloneArgs.Add(BeforeField(member));
                cloneArgs.Add(AfterField(member));
                cloneArgs.Add(HasField(member));
            }
        }
        cloneArgs.Add("recordedAt");
        code.AppendLine("return new ChangeSet(" + string.Join(", ", cloneArgs) + ");");
        code.CloseBlock();
    }

    /// <summary>Aliases canonical facade state as locals for relocated bodies.</summary>
    /// <remarks>Relocated ChangeSet operations take the facade as an explicit <c>self</c> parameter.</remarks>
    internal static void AppendSelfAliases(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members, bool includeMemberState = true)
    {
        code.AppendLine("var __sparse_recordedAt = self.RecordedAt;");
        code.AppendLine("var __sparse_hasWhole = self.__sparse_hasWhole;");
        code.AppendLine("var __sparse_wholeBefore = self.__sparse_wholeBefore;");
        code.AppendLine("var __sparse_wholeAfter = self.__sparse_wholeAfter;");
        if (!includeMemberState)
        {
            return;
        }

        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLine("var " + NestedField(member) + " = self." + NestedField(member) + ";");
            }
            else if (IsKeyed(member))
            {
                code.AppendLine("var " + HasField(member) + " = self." + HasField(member) + ";");
                code.AppendLine("var " + KeyedWholeFlag(member) + " = self." + KeyedWholeFlag(member) + ";");
                code.AppendLine("var " + KeyedWholeBefore(member) + " = self." + KeyedWholeBefore(member) + ";");
                code.AppendLine("var " + KeyedWholeAfter(member) + " = self." + KeyedWholeAfter(member) + ";");
                code.AppendLine("var " + KeyedItems(member) + " = self." + KeyedItems(member) + ";");
                code.AppendLine("var " + KeyedBeforeOrder(member) + " = self." + KeyedBeforeOrder(member) + ";");
                code.AppendLine("var " + KeyedAfterOrder(member) + " = self." + KeyedAfterOrder(member) + ";");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine("var " + KeyedTempBeforeOrder(member) + " = self." + KeyedTempBeforeOrder(member) + ";");
                    code.AppendLine("var " + KeyedTempAfterOrder(member) + " = self." + KeyedTempAfterOrder(member) + ";");
                }
            }
            else if (IsDict(member))
            {
                code.AppendLine("var " + HasField(member) + " = self." + HasField(member) + ";");
                code.AppendLine("var " + KeyedWholeFlag(member) + " = self." + KeyedWholeFlag(member) + ";");
                code.AppendLine("var " + KeyedWholeBefore(member) + " = self." + KeyedWholeBefore(member) + ";");
                code.AppendLine("var " + KeyedWholeAfter(member) + " = self." + KeyedWholeAfter(member) + ";");
                code.AppendLine("var " + KeyedItems(member) + " = self." + KeyedItems(member) + ";");
            }
            else
            {
                code.AppendLine("var " + BeforeField(member) + " = self." + BeforeField(member) + ";");
                code.AppendLine("var " + AfterField(member) + " = self." + AfterField(member) + ";");
                code.AppendLine("var " + HasField(member) + " = self." + HasField(member) + ";");
            }
        }
    }

    /// <summary>Aliases whole-state plus one member's canonical fields as locals.</summary>
    /// <remarks>
    /// Expression-bodied state readers become block-bodied when relocated;
    /// these aliases let their projection expressions move verbatim.
    /// </remarks>
    internal static void AppendMemberStateAliases(SharedIndentedBuilder code, SparseMemberModel member)
    {
        code.AppendLine("var __sparse_hasWhole = self.__sparse_hasWhole;");
        code.AppendLine("var __sparse_wholeBefore = self.__sparse_wholeBefore;");
        code.AppendLine("var __sparse_wholeAfter = self.__sparse_wholeAfter;");
        if (IsNested(member))
        {
            code.AppendLine("var " + NestedField(member) + " = self." + NestedField(member) + ";");
            return;
        }

        if (IsKeyed(member) || IsDict(member))
        {
            code.AppendLine("var " + HasField(member) + " = self." + HasField(member) + ";");
            code.AppendLine("var " + KeyedWholeFlag(member) + " = self." + KeyedWholeFlag(member) + ";");
            code.AppendLine("var " + KeyedWholeBefore(member) + " = self." + KeyedWholeBefore(member) + ";");
            code.AppendLine("var " + KeyedWholeAfter(member) + " = self." + KeyedWholeAfter(member) + ";");
            code.AppendLine("var " + KeyedItems(member) + " = self." + KeyedItems(member) + ";");
            if (IsKeyed(member))
            {
                code.AppendLine("var " + KeyedBeforeOrder(member) + " = self." + KeyedBeforeOrder(member) + ";");
                code.AppendLine("var " + KeyedAfterOrder(member) + " = self." + KeyedAfterOrder(member) + ";");
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    code.AppendLine("var " + KeyedTempBeforeOrder(member) + " = self." + KeyedTempBeforeOrder(member) + ";");
                    code.AppendLine("var " + KeyedTempAfterOrder(member) + " = self." + KeyedTempAfterOrder(member) + ";");
                }
            }
        }
        else
        {
            code.AppendLine("var " + BeforeField(member) + " = self." + BeforeField(member) + ";");
            code.AppendLine("var " + AfterField(member) + " = self." + AfterField(member) + ";");
            code.AppendLine("var " + HasField(member) + " = self." + HasField(member) + ";");
        }
    }

    internal static string EmptyArgs(ImmutableArray<SparseMemberModel> members)
    {
        var parts = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            parts.AddRange(EmptyMemberArgs(member));
        }

        return string.Join(", ", parts);
    }

    internal static string[] EmptyMemberArgs(SparseMemberModel member)
    {
        if (IsNested(member))
        {
            return ["null"];
        }

        if (IsKeyed(member) || IsDict(member))
        {
            if (IsKeyed(member))
            {
                if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                {
                    return ["false", "false", "default", "default", "null", "null", "null", "null", "null"];
                }

                return ["false", "false", "default", "default", "null", "null", "null"];
            }
            return ["false", "false", "default", "default", "null"];
        }
        return ["default", "default", "false"];
    }

    internal static void AppendIsEmpty(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        code.AppendLine("/// <summary>Whether this change set contains no semantic changes.</summary>");
        var expr = new List<string> { "!__sparse_hasWhole" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                expr.Add("(" + NestedField(member) + " is null || " + NestedField(member) + ".IsEmpty)");
            }
            else
            {
                expr.Add("!" + HasField(member));
            }
        }
        code.AppendLine("public bool IsEmpty => " + string.Join(" && ", expr) + ";");
    }

    internal static void AppendEmpty(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        code.AppendLine("/// <summary>Gets an empty change set with no semantic changes.</summary>");
        code.AppendLine("public static ChangeSet Empty { get; } = new ChangeSet(" + EmptyArgs(members) + ");");
    }

    internal static string MemberEmptyTail(ImmutableArray<SparseMemberModel> members)
    {
        if (members.IsDefaultOrEmpty || members.Length == 0)
        {
            return string.Empty.Trim();
        }

        var parts = new List<string>();
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                parts.Add("null");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                parts.AddRange(new[] { "false", "false", "default", "default", "null" });
                if (IsKeyed(member))
                {
                    parts.AddRange(new[] { "null", "null" });
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        parts.AddRange(new[] { "null", "null" });
                    }
                }
            }
            else
            {
                parts.AddRange(new[] { "default", "default", "false" });
            }
        }
        return string.Join(", ", parts);
    }

    internal static string TransNameFor(ImmutableArray<SparseMemberModel> members, SparseMemberModel member)
    {
        ComputePublicNames(members, out _, out var transNames);
        return transNames[member.Id];
    }

    /// <summary>Appends canonical keyed ChangeSet constructor arguments for one member.</summary>
    /// <remarks>Centralizes the temp-order tail so every ChangeSet construction site stays consistent.</remarks>
    internal static void AddKeyedChangeSetArgs(
        List<string> args,
        SparseMemberModel member,
        string has,
        string whole,
        string wholeBefore,
        string wholeAfter,
        string items,
        string beforeOrder,
        string afterOrder,
        string? tempBeforeOrder = null,
        string? tempAfterOrder = null
    )
    {
        args.AddRange(new[] { has, whole, wholeBefore, wholeAfter, items, beforeOrder, afterOrder });
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            args.Add(tempBeforeOrder ?? "null");
            args.Add(tempAfterOrder ?? "null");
        }
    }

    internal static string KeyTypeOf(SparseMemberModel m)
    {
        if (IsDict(m))
        {
            return m.Collection.ElementType.Name;
        }

        return m.Collection.KeyTypeName ?? "object?";
    }

    internal static string ElementTypeOf(SparseMemberModel m) => m.Collection.ElementType.Name;

    internal static string ValueTypeOf(SparseMemberModel m) => m.Collection.ValueType?.Name ?? "object?";

    internal static string ElementChangeSetOf(SparseMemberModel m) => m.Collection.ElementType.NonNullableName + ".ChangeSet";

    internal static string ElementFragmentOf(SparseMemberModel m) => m.Collection.ElementType.NonNullableName + ".Fragment";

    internal static string ValueChangeSetOf(SparseMemberModel m) => m.Collection.ValueType!.Value.NonNullableName + ".ChangeSet";

    internal static string ValueFragmentOf(SparseMemberModel m) => m.Collection.ValueType!.Value.NonNullableName + ".Fragment";
}
