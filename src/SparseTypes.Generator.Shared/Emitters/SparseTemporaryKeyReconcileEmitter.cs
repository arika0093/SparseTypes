namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity reconcile helpers for fragment models.</summary>
/// <remarks>Builds the temporary-to-permanent correlation between a submitted model and its authoritative persisted counterpart.</remarks>
internal static class SparseTemporaryKeyReconcileEmitter
{
    internal static void AppendReconcileHelpers(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType = true
    )
    {
        SparseTemporaryKeyCollectEmitter.AppendBuildTempMap(code, modelType, members);
        SparseTemporaryKeyCollectEmitter.AppendCollectTempMap(code, modelType, members);
        AppendRetargetTemps(code, modelType, runtimeNamespace, members, modelIsReferenceType);
    }

    private static void AppendRetargetTemps(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        code.AppendLine("/// <summary>Assigns mapped temporary identities into the live model.</summary>");
        code.AppendLine(
            "/// <remarks>Replays recorded post-submit edits onto persisted elements; rows without their own edits adopt the persisted element, and unmapped temporary identities stay pending. Returns an error without partial writes when the live state is incompatible.</remarks>"
        );
        code.AppendLine(
            "internal static string? __SparseRetargetTemps("
                + modelType
                + " live, "
                + modelType
                + "? submittedAfter, "
                + modelType
                + "? persisted, global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?> map, "
                + modelType
                + ".ChangeSet? pending, string __scope)"
        );
        code.OpenBlock();
        code.AppendLine("if ((object?)live is null) throw new global::System.ArgumentNullException(nameof(live));");
        code.AppendLine("if (map is null) throw new global::System.ArgumentNullException(nameof(map));");
        code.AppendLine("if (__scope is null) throw new global::System.ArgumentNullException(nameof(__scope));");
        // Models without keyed fragment members or fragment children emit no
        // failing paths.
        var needsFailHelper = false;
        foreach (var member in members)
        {
            if (SparseChangeSetBasicsEmitter.IsKeyed(member) && member.Collection.ElementType.IsFragmentModel)
            {
                needsFailHelper = true;
                break;
            }

            if (member.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(member) && member.ChildModel.Value.IsFragmentModel)
            {
                needsFailHelper = true;
                break;
            }
        }

        if (needsFailHelper)
        {
            AppendRetargetBackups(code, members);
            // Fail helper restores earlier commits so a late failure never
            // leaves a sibling collection changed.
            code.AppendLine("string? __Fail(string __err)");
            code.OpenBlock();
            AppendRetargetRestoreStatements(code, members);
            code.AppendLine("return __err;");
            code.CloseBlock();
        }
        foreach (var member in members)
        {
            if (SparseChangeSetBasicsEmitter.IsKeyed(member) && member.Collection.ElementType.IsFragmentModel)
            {
                AppendKeyedRetarget(code, runtimeNamespace, member, modelIsReferenceType);
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
            )
            {
                AppendNestedRetarget(code, member, runtimeNamespace, modelIsReferenceType);
            }
        }
        code.AppendLine("return null;");
        code.CloseBlock();
    }

    private static void AppendRetargetBackups(SharedIndentedBuilder code, System.Collections.Immutable.ImmutableArray<SparseMemberModel> members)
    {
        // Shallow backups preserve original row references for rollback.
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (SparseChangeSetBasicsEmitter.IsKeyed(member) && member.Collection.ElementType.IsFragmentModel)
            {
                var elementName = member.Collection.ElementType.Name;
                code.AppendLine("var __origRef" + member.Id + " = live." + prop + ";");
                code.AppendLine(
                    "var __backup"
                        + member.Id
                        + " = __origRef"
                        + member.Id
                        + " is null ? null : new global::System.Collections.Generic.List<"
                        + elementName
                        + ">(__origRef"
                        + member.Id
                        + ");"
                );
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
                && !member.Property.IsReadOnly
                && !member.Property.IsInitOnly
            )
            {
                code.AppendLine("var __origChild" + member.Id + " = live." + prop + ";");
            }
        }
    }

    private static void AppendRetargetRestoreStatements(
        SharedIndentedBuilder code,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        // Inline in __Fail so original references stay captured.
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (SparseChangeSetBasicsEmitter.IsKeyed(member) && member.Collection.ElementType.IsFragmentModel)
            {
                var elementName = member.Collection.ElementType.Name;
                var settable = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
                code.OpenBlock();
                code.AppendLine("var __cur = live." + prop + ";");
                code.AppendLine("var __orig = __origRef" + member.Id + ";");
                code.AppendLine("var __bak = __backup" + member.Id + ";");
                code.AppendLine("if (!object.ReferenceEquals(__cur, __orig))");
                code.OpenBlock();
                if (settable)
                {
                    code.AppendLine("live." + prop + " = __orig!;");
                }
                else
                {
                    code.AppendLine(
                        "if (__cur is global::System.Collections.Generic.IList<"
                            + elementName
                            + "> __curList && __bak is not null && !__curList.IsReadOnly) { __curList.Clear(); foreach (var __rb in __bak) __curList.Add(__rb); }"
                    );
                }
                code.CloseBlock();
                code.AppendLine(
                    "else if (__cur is global::System.Collections.Generic.IList<"
                        + elementName
                        + "> __sameList && __bak is not null && !__sameList.IsReadOnly)"
                );
                code.OpenBlock();
                code.AppendLine("if (__sameList.Count != __bak.Count) { __sameList.Clear(); foreach (var __rb in __bak) __sameList.Add(__rb); }");
                code.AppendLine(
                    "else { var __diff = false; for (var __bi = 0; __bi < __bak.Count; __bi++) if (!object.ReferenceEquals(__sameList[__bi], __bak[__bi])) { __diff = true; break; } if (__diff) { __sameList.Clear(); foreach (var __rb in __bak) __sameList.Add(__rb); } }"
                );
                code.CloseBlock();
                code.CloseBlock();
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
                && !member.Property.IsReadOnly
                && !member.Property.IsInitOnly
            )
            {
                code.AppendLine("live." + prop + " = __origChild" + member.Id + "!;");
            }
        }
    }

    private static void AppendKeyedRetarget(SharedIndentedBuilder code, string runtimeNamespace, SparseMemberModel member, bool modelIsReferenceType)
    {
        var elementType = member.Collection.ElementType.NonNullableName;
        var keyType = member.Collection.KeyTypeName ?? "object?";
        var keyProp = member.Collection.KeyPropertyNames.Length == 1 ? SparseNaming.EscapeIdentifier(member.Collection.KeyPropertyNames[0]) : null;
        var tempProp = member.Collection.TemporaryKeyPropertyName is null
            ? null
            : SparseNaming.EscapeIdentifier(member.Collection.TemporaryKeyPropertyName);
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var unassigned = member.Collection.UnassignedKeyExpression;
        var optionalFragment = runtimeNamespace + "Optional<" + elementType + ".Fragment?>";
        // Nullable element annotations ride the working list so indexable casts
        // never fail on variance.
        var elementName = member.Collection.ElementType.Name;
        var elementRef2 = member.Collection.ElementType.IsReferenceType;
        var elementNullableStruct2 = !elementRef2 && (elementName.TrimEnd().EndsWith("?") || elementName.Contains("Nullable<"));
        code.OpenBlock();
        code.AppendLine("var __liveList = live." + prop + ";");
        code.AppendLine("var __subList = submittedAfter?." + prop + ";");
        code.AppendLine("var __perList = persisted?." + prop + ";");
        code.AppendLine("if (__liveList is not null)");
        code.OpenBlock();
        code.AppendLine("var __transition = pending?." + prop + ";");
        // Collection scope matches the correlation scope so sibling
        // collections never cross-target a reused Guid.
        code.AppendLine("var __collectionScope = string.IsNullOrEmpty(__scope) ? \"" + prop + "\" : __scope + \"." + prop + "\";");
        // Detached staging: the live list is never aliased, so late
        // shape failures return before any mutation.
        code.AppendLine("var __backing = __liveList as global::System.Collections.Generic.IList<" + elementName + ">;");
        code.AppendLine("var __working = new global::System.Collections.Generic.List<" + elementName + ">(__liveList);");
        code.AppendLine("var __dirty = false;");
        if (keyProp is not null)
        {
            SparseTemporaryKeyOrderEmitter.EmitBuildKeyLookup(
                code,
                "__subList",
                "__subByKey",
                "__subHasNullKey",
                "__subNullKey",
                elementType,
                keyType,
                keyProp,
                member.Collection.KeyIsReferenceType,
                elementRef2,
                elementNullableStruct2
            );
            SparseTemporaryKeyOrderEmitter.EmitBuildKeyLookup(
                code,
                "__perList",
                "__perByKey",
                "__perHasNullKey",
                "__perNullKey",
                elementType,
                keyType,
                keyProp,
                member.Collection.KeyIsReferenceType,
                elementRef2,
                elementNullableStruct2
            );
        }
        code.AppendLine("var __sourceCount = __working.Count;");
        code.AppendLine("var __write = 0;");
        code.AppendLine("for (var __i = 0; __i < __sourceCount; __i++)");
        code.OpenBlock();
        code.AppendLine("var __e = __working[__i];");
        // Normalized element variable: references promote, nullable structs
        // unwrap, plain structs flow through.
        var eUse = "__e";
        if (elementRef2)
        {
            code.AppendLine("if (__e is null) { __working[__write++] = __e!; continue; }");
        }
        else if (elementNullableStruct2)
        {
            code.AppendLine("if (!__e.HasValue) { __working[__write++] = __e; continue; }");
            code.AppendLine("var __en = __e.Value;");
            eUse = "__en";
        }
        if (keyProp is not null)
        {
            code.AppendLine("var __k = " + eUse + "." + keyProp + ";");
            // Keys without an unassigned state are always assigned; the
            // temporary branch below only exists for unassigned-capable keys.
            var hasUnassignedState = unassigned is not null;
            if (hasUnassignedState)
            {
                var isUnassignedCheck =
                    "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default.Equals(__k, " + unassigned + ")";
                code.AppendLine("if (" + isUnassignedCheck + ")");
                code.OpenBlock();
                if (tempProp is not null)
                {
                    code.AppendLine("var __t = " + eUse + "." + tempProp + ";");
                    code.AppendLine(
                        "if (!__t.HasValue || __t.Value == global::System.Guid.Empty) return __Fail(\"The live model contains an unassigned element without a temporary identity.\");"
                    );
                    code.AppendLine(
                        "if (!map.TryGetValue((__collectionScope, __t.Value), out var __persisted)) { __working[__write++] = __working[__i]"
                            + (elementRef2 ? "!" : "")
                            + "; continue; }"
                    );
                    code.AppendLine("var __edit = __transition?.GetTemporaryChange(__t.Value)?.Edit;");
                    code.AppendLine(elementType + " __merged;");
                    // The live row carries no divergent local state, so the
                    // server state (identity plus values it computed) decides.
                    code.AppendLine(
                        "if (__edit is null || __edit.IsEmpty) __merged = "
                            + elementType
                            + ".Fragment.From(("
                            + elementType
                            + ")__persisted!).ToModel();"
                    );
                    code.AppendLine("else");
                    code.OpenBlock();
                    // The edit sits on the submitted temporary state, so
                    // applying it to the assigned persisted state goes stale.
                    code.AppendLine("var __persistedElement = (" + elementType + ")__persisted!;");
                    code.AppendLine("var __applied = __edit.ToPatch().Apply(" + elementType + ".Fragment.From(__persistedElement));");
                    code.AppendLine(
                        "if (!__applied.IsPresent || __applied.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
                    );
                    code.AppendLine("__merged = __applied.Value.ToModel();");
                    code.CloseBlock();
                    code.AppendLine("__working[__i] = __merged; __dirty = true;");
                }
                else
                {
                    code.AppendLine("return __Fail(\"The live model contains an unassigned element without temporary support.\");");
                }
                code.CloseBlock();
                code.AppendLine("else");
                code.OpenBlock();
                SparseTemporaryKeyElementEmitter.AppendKeyedAssignedElement(
                    code,
                    runtimeNamespace,
                    member,
                    elementType,
                    keyType,
                    keyProp,
                    optionalFragment,
                    eUse,
                    elementRef2,
                    elementNullableStruct2,
                    member.Collection.KeyIsReferenceType,
                    "__subByKey",
                    "__subHasNullKey",
                    "__subNullKey",
                    "__perByKey",
                    "__perHasNullKey",
                    "__perNullKey"
                );
                code.CloseBlock();
            }
            else
            {
                // No unassigned state: every element processes as assigned.
                SparseTemporaryKeyElementEmitter.AppendKeyedAssignedElement(
                    code,
                    runtimeNamespace,
                    member,
                    elementType,
                    keyType,
                    keyProp,
                    optionalFragment,
                    eUse,
                    elementRef2,
                    elementNullableStruct2,
                    member.Collection.KeyIsReferenceType,
                    "__subByKey",
                    "__subHasNullKey",
                    "__subNullKey",
                    "__perByKey",
                    "__perHasNullKey",
                    "__perNullKey"
                );
            }
        }
        else
        {
            // Keyless keyed members cannot correlate; nested recursion still
            // applies.
            code.AppendLine(
                "var __elemScope = __collectionScope + \"[\" + __i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\";"
            );
            code.AppendLine("var __recurseError = " + elementType + ".__SparseRetargetTemps(" + eUse + ", null, null, map, null, __elemScope);");
            code.AppendLine("if (__recurseError is not null) return __Fail(__recurseError);");
        }
        code.AppendLine("__working[__write++] = __working[__i]" + (elementRef2 ? "!" : "") + ";");
        code.CloseBlock();
        code.AppendLine("if (__write != __working.Count) __working.RemoveRange(__write, __working.Count - __write);");
        SparseTemporaryKeyElementEmitter.AppendKeyedMembershipReconcile(
            code,
            member,
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            optionalFragment,
            elementRef2,
            elementNullableStruct2
        );
        AppendRetargetWriteback(code, member, modelIsReferenceType);
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendRetargetWriteback(SharedIndentedBuilder code, SparseMemberModel member, bool modelIsReferenceType)
    {
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var writeable =
            modelIsReferenceType
            && !member.Property.IsReadOnly
            && !member.Property.IsInitOnly
            && (
                member.Collection.NamedTypeDefinition is null
                || member.Collection.NamedTypeDefinition.StartsWith("System.", System.StringComparison.Ordinal)
            );
        // Atomic commit: preflight every shape constraint before touching the
        // live list, then publish the detached staging in one step.
        var elementName = member.Collection.ElementType.Name;
        var conversion = member.Property.Type.Name.TrimEnd().EndsWith("[]")
            ? "new global::System.Collections.Generic.List<" + elementName + ">(__working).ToArray()"
            : "new global::System.Collections.Generic.List<" + elementName + ">(__working)";
        code.AppendLine("if (__dirty)");
        code.OpenBlock();
        if (!writeable)
        {
            code.AppendLine("_ = __backing;");
            code.AppendLine("return __Fail(\"The live collection shape cannot be reconciled in place.\");");
        }
        else
        {
            code.AppendLine("if (__backing is not null)");
            code.OpenBlock();
            code.AppendLine("if (__backing.IsReadOnly) return __Fail(\"The live collection shape cannot be reconciled in place.\");");
            code.AppendLine("__backing.Clear();");
            code.AppendLine("foreach (var __commit in __working) __backing.Add(__commit);");
            code.CloseBlock();
            // Arrays never reach the backing branch as writable lists.
            code.AppendLine("else live." + prop + " = " + conversion + ";");
        }
        code.CloseBlock();
    }

    private static void AppendNestedRetarget(SharedIndentedBuilder code, SparseMemberModel member, string runtimeNamespace, bool modelIsReferenceType)
    {
        var childType = member.ChildModel!.Value.NonNullableName;
        var childFrag = childType + ".Fragment";
        var childOptionalFrag = runtimeNamespace + "Optional<" + childType + ".Fragment?>";
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var childRef = member.ChildModel.Value.IsReferenceType;
        var childNullableValue = !childRef && member.ChildModel.Value.Name.TrimEnd().EndsWith("?");
        var settable = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        var defaultExpr = (childRef || childNullableValue) ? "default!" : "default";
        // The ?. accessors above always yield nullable locals.
        var perAccess = childRef ? "__childPer!" : "__childPer.Value";
        // Child live value with nullability normalized; server-side drops with
        // local edits fail deterministically instead of losing edits silently.
        code.OpenBlock();
        code.AppendLine("var __childScope" + member.Id + " = string.IsNullOrEmpty(__scope) ? \"" + prop + "\" : __scope + \"." + prop + "\";");
        code.AppendLine("var __childLive = live." + prop + ";");
        if (childRef)
        {
            code.AppendLine("if (__childLive is null)");
            code.OpenBlock();
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
        }
        else if (childNullableValue)
        {
            code.AppendLine("if (!__childLive.HasValue)");
            code.OpenBlock();
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("var __childValue = __childLive.Value;");
        }
        // Plain struct children flow through directly.
        string childLive = childNullableValue ? "__childValue" : "__childLive";
        code.AppendLine("var __childPending = pending?." + prop + ";");
        code.AppendLine("var __childSub = submittedAfter?." + prop + ";");
        code.AppendLine("var __childPer = persisted?." + prop + ";");
        code.AppendLine("if (__childPer is null)");
        code.OpenBlock();
        code.AppendLine(
            "if (__childPending is not null && !__childPending.IsEmpty) return __Fail(\"The server dropped a value with pending local edits.\");"
        );
        if (modelIsReferenceType && settable)
        {
            code.AppendLine("live." + prop + " = " + defaultExpr + ";");
        }
        else
        {
            code.AppendLine(
                "var __dropError = "
                    + childType
                    + ".__SparseRetargetTemps("
                    + childLive
                    + ", __childSub, __childPer, map, __childPending, __childScope"
                    + member.Id
                    + ");"
            );
            code.AppendLine("if (__dropError is not null) return __Fail(__dropError);");
        }
        code.CloseBlock();
        code.AppendLine("else if (__childSub is null)");
        code.OpenBlock();
        code.AppendLine(
            "var __addError = "
                + childType
                + ".__SparseRetargetTemps("
                + childLive
                + ", __childSub, __childPer, map, __childPending, __childScope"
                + member.Id
                + ");"
        );
        code.AppendLine("if (__addError is not null) return __Fail(__addError);");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        if (modelIsReferenceType && settable)
        {
            code.AppendLine("var __mergedChildBase = " + childFrag + ".From(" + perAccess + ");");
            // Pending edits sit on the submitted state while the base moved to
            // the persisted state.
            code.AppendLine("var __mergedChildAdvanced = " + childOptionalFrag + ".Present(__mergedChildBase);");
            code.AppendLine("if (__childPending is not null && !__childPending.IsEmpty)");
            code.OpenBlock();
            code.AppendLine("var __mergedChildApplied = __childPending.ToPatch().Apply(__mergedChildBase);");
            code.AppendLine(
                "if (!__mergedChildApplied.IsPresent || __mergedChildApplied.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
            );
            code.AppendLine("__mergedChildAdvanced = __mergedChildApplied;");
            code.CloseBlock();
            code.AppendLine(
                "if (!__mergedChildAdvanced.IsPresent || __mergedChildAdvanced.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
            );
            code.AppendLine("live." + prop + " = __mergedChildAdvanced.Value.ToModel();");
        }
        else
        {
            code.AppendLine(
                "var __childError = "
                    + childType
                    + ".__SparseRetargetTemps("
                    + childLive
                    + ", __childSub, __childPer, map, __childPending, __childScope"
                    + member.Id
                    + ");"
            );
            code.AppendLine("if (__childError is not null) return __Fail(__childError);");
        }
        code.CloseBlock();
        if (childRef || childNullableValue)
        {
            code.CloseBlock();
        }
        code.CloseBlock();
    }
}
