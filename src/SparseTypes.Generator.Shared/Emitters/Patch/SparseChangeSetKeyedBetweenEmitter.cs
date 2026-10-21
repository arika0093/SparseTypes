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
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Between-time sparse diff for keyed sequences.</summary>
internal static class SparseChangeSetKeyedBetweenEmitter
{
    /// <summary>
    /// Emits Between-time sparse diff for a keyed sequence member.
    /// </summary>
    /// <remarks>Declares locals used to build the keyed diff.</remarks>
    internal static void AppendKeyedBetweenSparse(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        ComputePublicNames(
            System.Collections.Immutable.ImmutableArray.Create(member),
            out _,
            out var transNames
        );
        var trans = transNames[member.Id];
        // Locals.
        code.AppendLine("bool __h" + id + " = false;");
        code.AppendLine("bool __whole" + id + " = false;");
        code.AppendLine(opt + " __wb" + id + " = default;");
        code.AppendLine(opt + " __wa" + id + " = default;");
        code.AppendLine(
            "global::System.Collections.Generic.List<" + trans + ".Item>? __items" + id + " = null;"
        );
        code.AppendLine(
            "global::System.Collections.Generic.List<" + keyType + ">? __bO" + id + " = null;"
        );
        code.AppendLine(
            "global::System.Collections.Generic.List<" + keyType + ">? __aO" + id + " = null;"
        );
        var hasTemp = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempLocals(code, member, id);
        }
        code.OpenBlock();
        code.AppendLine("var __before" + id + " = bf." + escName + ";");
        code.AppendLine("var __after" + id + " = af." + escName + ";");
        code.AppendLine(
            "bool __beforeHas"
                + id
                + " = __before"
                + id
                + ".IsPresent && (object?)__before"
                + id
                + ".Value is not null;"
        );
        code.AppendLine(
            "bool __afterHas"
                + id
                + " = __after"
                + id
                + ".IsPresent && (object?)__after"
                + id
                + ".Value is not null;"
        );
        // Whole transitions do not build maps, so they validate the baseline
        // separately.
        code.AppendLine(
            "if (__before"
                + id
                + ".IsPresent != __after"
                + id
                + ".IsPresent || !__beforeHas"
                + id
                + " || !__afterHas"
                + id
                + ")"
        );
        code.OpenBlock();
        code.Indent();
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempWholeValidation(code, member, id);
            }
            else
            {
                code.AppendLine(
                    "if (__beforeHas"
                        + id
                        + ") foreach (var __baselineItem in __before"
                        + id
                        + ".Value!) if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                            member,
                            "__SparseKeyOf_ChangeSet_" + id + "(__baselineItem)"
                        )
                        + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection transition.\");"
                );
            }
        }
        code.Unindent();
        code.AppendLine(
            "if (!Fragment.__SparseEqual_" + id + "(__before" + id + ", __after" + id + "))"
        );
        code.OpenBlock();
        code.AppendLine("__h" + id + " = true; __whole" + id + " = true;");
        var keyedBefore = "__before" + id;
        var keyedAfter = "__after" + id;
        if (NeedsSnapshot(member))
        {
            keyedBefore = "__SparseSnapshot_" + id + "(" + keyedBefore + ")";
            keyedAfter = "__SparseSnapshot_" + id + "(" + keyedAfter + ")";
        }
        code.AppendLine(
            "__wb" + id + " = " + keyedBefore + "; __wa" + id + " = " + keyedAfter + ";"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        SparseChangeSetKeyedAlignedBetweenEmitter.Append(code, member, trans, dialect);
        SparseChangeSetKeyedAlignedBetweenEmitter.AppendGeneralStart(code, member, trans, runtime);
        code.Indent().Indent();
        // Granular Value -> Value: build key maps and orders (single
        // enumeration each).
        SparseKeyedCollectionEmitter.AppendChangeSetBetweenStorage(code, member, "before");
        code.AppendLine("foreach (var __item in __before" + id + ".Value!)");
        code.OpenBlock();
        code.AppendLine("var __k = __SparseKeyOf_ChangeSet_" + id + "(__item);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempBeforeBranch(code, member, id);
            }
            else
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection transition.\");"
                );
            }
        }
        code.AppendLine(
            SparseKeyedCollectionEmitter.AddUniqueEntry("__beforeMap" + id, "__k", "__item")
        );
        code.AppendLine("__beforeOrder" + id + ".Add(__k);");
        code.CloseBlock();
        SparseKeyedCollectionEmitter.AppendChangeSetBetweenStorage(code, member, "after");
        if (!hasTemp)
        {
            code.AppendLine(
                "var __unassignedAfter"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + elementType
                    + ">();"
            );
        }

        code.AppendLine("foreach (var __item in __after" + id + ".Value!)");
        code.OpenBlock();
        code.AppendLine("var __k = __SparseKeyOf_ChangeSet_" + id + "(__item);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempAfterBranch(code, member, id);
            }
            else
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                        + ") { __afterOrder"
                        + id
                        + ".Add(__k!); __unassignedAfter"
                        + id
                        + ".Add(__item); continue; }"
                );
            }
        }
        code.AppendLine(
            SparseKeyedCollectionEmitter.AddUniqueEntry("__afterMap" + id, "__k", "__item")
        );
        code.AppendLine("__afterOrder" + id + ".Add(__k!);");
        code.CloseBlock();
        // Edited (nested sparse ChangeSets for surviving keys).
        code.AppendLine(
            "var __edited"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine("foreach (var __k in __beforeMap" + id + ".Keys)");
        code.OpenBlock();
        code.AppendLine(
            "if (!__afterMap" + id + ".TryGetValue(__k, out var __afterItem)) continue;"
        );
        code.AppendLine("var __beforeItem = __beforeMap" + id + "[__k];");
        code.AppendLine("var __nested = " + elementCs + ".Between(__beforeItem, __afterItem);");
        code.AppendLine("if (!__nested.IsEmpty) __edited" + id + "[__k] = __nested;");
        code.CloseBlock();
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempEditedLoop(code, member, id);
        }
        code.AppendLine(
            "bool __orderChanged"
                + id
                + " = !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__beforeOrder"
                + id
                + ", __afterOrder"
                + id
                + ")"
                + (
                    hasTemp
                        ? " || !"
                            + facade
                            + ".KeyOrderEquals<global::System.Guid>(__bTO"
                            + id
                            + ", __aTO"
                            + id
                            + ")"
                        : ""
                )
                + ";"
        );
        // Empty when no added/removed/edited and order unchanged.
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempAddedRemovedProbes(code, member, id);
        }
        else
        {
            code.AppendLine(
                "bool __hasAdded"
                    + id
                    + " = __unassignedAfter"
                    + id
                    + ".Count > 0; if (!__hasAdded"
                    + id
                    + ") foreach (var __k in __afterOrder"
                    + id
                    + ") if (!__beforeMap"
                    + id
                    + ".ContainsKey(__k)) { __hasAdded"
                    + id
                    + " = true; break; }"
            );
            code.AppendLine(
                "bool __hasRemoved"
                    + id
                    + " = false; foreach (var __k in __beforeOrder"
                    + id
                    + ") if (!__afterMap"
                    + id
                    + ".ContainsKey(__k)) { __hasRemoved"
                    + id
                    + " = true; break; }"
            );
        }
        code.AppendLine(
            "if (!__hasAdded"
                + id
                + " && !__hasRemoved"
                + id
                + " && __edited"
                + id
                + ".Count == 0"
                + (hasTemp ? " && __editedT" + id + ".Count == 0" : "")
                + " && !__orderChanged"
                + id
                + ") { }"
        );
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("__h" + id + " = true;");
        // Surviving-rank reorder set (relative order, not absolute shifts).
        // Equal complete orders also imply equal surviving ranks.
        code.AppendLine(
            "global::System.Collections.Generic.HashSet<"
                + keyType
                + ">? __reordered"
                + id
                + " = null;"
        );
        code.AppendLine("if (__orderChanged" + id + ")");
        code.OpenBlock();
        code.Indent();
        code.AppendLine(
            "var __afterRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__hasRemoved"
                + id
                + " ? 0 : __beforeMap"
                + id
                + ".Count, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "{ var __r = 0; foreach (var __k in __afterOrder"
                + id
                + ") if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "!"
                            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                            + " && "
                        : ""
                )
                + "__beforeMap"
                + id
                + ".ContainsKey(__k!)) __afterRank"
                + id
                + "[__k!] = __r++; }"
        );
        code.AppendLine(
            "__reordered"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine(
            "{ var __r = 0; foreach (var __k in __beforeOrder"
                + id
                + ") if (__afterRank"
                + id
                + ".TryGetValue(__k!, out var __ar)) { if (__ar != __r) __reordered"
                + id
                + ".Add(__k!); __r++; } }"
        );
        code.Unindent();
        code.CloseBlock();
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempRanks(code, id);
        }
        // Pending entries share one sentinel slot in the assigned before-index
        // map.
        code.AppendLine(
            "var __beforeIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__beforeMap"
                + id
                + ".Count"
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? " + (__beforeOrder" + id + ".Count > __beforeMap" + id + ".Count ? 1 : 0)"
                        : ""
                )
                + ", "
                + comparer
                + ");"
        );
        code.AppendLine(
            "for (var __i = 0; __i < __beforeOrder"
                + id
                + ".Count; __i++) __beforeIndex"
                + id
                + "[__beforeOrder"
                + id
                + "[__i]] = __i;"
        );
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempIndexes(code, member, id);
        }
        code.AppendLine(
            "var __list"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        // After-order changed keys (added / edited / reorder-only).
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                code.AppendLine("var __tempOrdinal" + id + " = 0;");
            }
            else
            {
                code.AppendLine("var __unassignedOrdinal = 0;");
            }
        }
        code.AppendLine(
            "for (var __afterItemIndex = 0; __afterItemIndex < __afterOrder"
                + id
                + ".Count; __afterItemIndex++)"
        );
        code.OpenBlock();
        code.AppendLine("var __k = __afterOrder" + id + "[__afterItemIndex];");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempItemBranch(
                    code,
                    member,
                    trans,
                    runtime,
                    id
                );
            }
            else
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                        + ") { var __ua = __unassignedAfter"
                        + id
                        + "[__unassignedOrdinal++]; var __uaEdit = "
                        + elementCs
                        + ".Between(default, "
                        + runtime
                        + "Optional<"
                        + elementFrag
                        + "?>.Present("
                        + elementFrag
                        + ".From(__ua))); __list"
                        + id
                        + ".Add(new "
                        + trans
                        + ".Item(__k!, null, default, "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__ua), -1, __afterItemIndex, true, false, false, false, __uaEdit, false)); continue; }"
                );
            }
        }
        code.AppendLine("bool __inBefore = __beforeMap" + id + ".TryGetValue(__k, out var __b);");
        code.AppendLine("var __a = __afterMap" + id + "[__k];");
        code.AppendLine("bool __isEdited = __edited" + id + ".TryGetValue(__k, out var __edit);");
        code.AppendLine("bool __isReordered = __reordered" + id + "?.Contains(__k) == true;");
        code.AppendLine("if (!__inBefore || __isEdited || __isReordered)");
        code.OpenBlock();
        // Surviving elements were already compared; only additions need a root
        // snapshot.
        var optionalElementFragment = runtime + "Optional<" + elementFrag + "?>";
        code.AppendLine(
            "var __fullEdit = __isEdited ? __edit! : __inBefore ? "
                + elementCs
                + ".Between(default("
                + optionalElementFragment
                + "), default("
                + optionalElementFragment
                + ")) : "
                + elementCs
                + ".Between(default, "
                + optionalElementFragment
                + ".Present("
                + elementFrag
                + ".From(__a)));"
        );
        code.AppendLine(
            runtime
                + "Optional<"
                + elementType
                + "> __ib = __inBefore && !__isEdited && !__isReordered ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b!) : default;"
        );
        code.AppendLine(
            "int __bi = __beforeIndex" + id + ".TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLine("int __ai = __afterItemIndex;");
        code.AppendLine(
            runtime
                + "Optional<"
                + elementType
                + "> __ia = !__isEdited && !__isReordered ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__a) : default; __list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k!, null, __ib, __ia, __bi, __ai, !__inBefore, false, __isEdited, __isReordered, __fullEdit, false));"
        );
        code.CloseBlock();
        code.CloseBlock();
        // Removed keys in before-order.
        code.AppendLine("foreach (var __k in __beforeOrder" + id + ")");
        code.OpenBlock();
        if (hasTemp)
        {
            // Sentinel slots belong to the temporary order; assigned removal
            // below would misread them. Temporary removals follow separately.
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") continue;"
            );
        }
        code.AppendLine("if (__afterMap" + id + ".ContainsKey(__k)) continue;");
        code.AppendLine("var __b = __beforeMap" + id + "[__k];");
        code.AppendLine(
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b));"
        );
        code.AppendLine("var __fullEdit = " + elementCs + ".Between(__eb, default);");
        code.AppendLine(
            "int __bi = __beforeIndex" + id + ".TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLine(
            "__list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k, null, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b), default, __bi, -1, false, true, false, false, __fullEdit, false));"
        );
        code.CloseBlock();
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempRemovedLoop(
                code,
                member,
                trans,
                runtime,
                id
            );
        }
        code.AppendLine("__items" + id + " = __list" + id + ";");
        // Always retain full key orders (keys only.
        code.AppendLine(
            "__bO" + id + " = __beforeOrder" + id + "; __aO" + id + " = __afterOrder" + id + ";"
        );
        code.CloseBlock();
        code.Unindent().Unindent();
        SparseChangeSetKeyedAlignedBetweenEmitter.AppendGeneralEnd(code, member);
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
    }
}
