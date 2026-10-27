namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity correlation builders for fragment models.</summary>
/// <remarks>Builds path-scoped maps from submitted to persisted keys.</remarks>
internal static class SparseTemporaryKeyCollectEmitter
{
    internal static void AppendBuildTempMap(
        SharedIndentedBuilder code,
        string modelType,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        _ = members;
        code.AppendLine("/// <summary>Correlates submitted temporary identities with persisted assigned keys.</summary>");
        code.AppendLine(
            "internal static (global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?>? Map, string? Error) __SparseBuildTempMap("
                + modelType
                + " submitted, "
                + modelType
                + " persisted)"
        );
        code.OpenBlock();
        code.AppendLine("if ((object?)submitted is null) throw new global::System.ArgumentNullException(nameof(submitted));");
        code.AppendLine("if ((object?)persisted is null) throw new global::System.ArgumentNullException(nameof(persisted));");
        code.AppendLine("var __map = new global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?>();");
        code.AppendLine("var __error = __SparseCollectTempMap(submitted, persisted, __map, \"\");");
        code.AppendLine("if (__error is not null) return (null, __error);");
        code.AppendLine("return (__map, null);");
        code.CloseBlock();
    }

    internal static void AppendCollectTempMap(
        SharedIndentedBuilder code,
        string modelType,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        System.Func<string, string> reconcilerTypeName
    )
    {
        code.AppendLine("/// <summary>Collects temporary correlations for this model into a shared map.</summary>");
        code.AppendLine(
            "internal static string? __SparseCollectTempMap("
                + modelType
                + " submitted, "
                + modelType
                + " persisted, global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?> map, string __scope)"
        );
        code.OpenBlock();
        foreach (var member in members)
        {
            if (SparseChangeSetBasicsEmitter.IsKeyed(member) && member.Collection.ElementType.IsFragmentModel)
            {
                AppendKeyedCollect(code, member, reconcilerTypeName);
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
            )
            {
                AppendNestedCollect(code, member, reconcilerTypeName);
            }
        }
        code.AppendLine("return null;");
        code.CloseBlock();
    }

    private static void AppendKeyedCollect(SharedIndentedBuilder code, SparseMemberModel member, System.Func<string, string> reconcilerTypeName)
    {
        var elementType = member.Collection.ElementType.NonNullableName;
        var keyType = member.Collection.KeyTypeName ?? "object?";
        var keyCanBeNull = member.Collection.KeyIsReferenceType || keyType.EndsWith("?") || keyType.Contains("Nullable<");
        // Element shape drives null handling.
        var elementRef = member.Collection.ElementType.IsReferenceType;
        var elementName = member.Collection.ElementType.Name.TrimEnd();
        var elementNullableStruct = !elementRef && (elementName.EndsWith("?") || elementName.Contains("Nullable<"));
        var keyProp = member.Collection.KeyPropertyNames.Length == 1 ? SparseNaming.EscapeIdentifier(member.Collection.KeyPropertyNames[0]) : null;
        var tempProp = member.Collection.TemporaryKeyPropertyName is null
            ? null
            : SparseNaming.EscapeIdentifier(member.Collection.TemporaryKeyPropertyName);
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var unassigned = member.Collection.UnassignedKeyExpression;
        var isUnassigned = unassigned is null
            ? "false"
            : "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default.Equals(__sk, " + unassigned + ")";
        code.AppendLine("if (submitted." + prop + " is not null && persisted." + prop + " is not null)");
        code.OpenBlock();
        // Sibling collections may reuse a Guid while duplicates within one
        // collection still fail.
        code.AppendLine("var __collectionScope" + member.Id + " = string.IsNullOrEmpty(__scope) ? \"" + prop + "\" : __scope + \"." + prop + "\";");
        if (keyProp is not null)
        {
            code.AppendLine("var __persistedIndexesBuilt" + member.Id + " = false;");
            code.AppendLine(
                "global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + "?> __persistedByKey" + member.Id + " = null!;"
            );
            if (keyCanBeNull)
            {
                code.AppendLine("var __hasNullPersistedKey" + member.Id + " = false;");
                code.AppendLine(elementType + "? __firstNullPersistedKey" + member.Id + " = default;");
            }
            if (tempProp is not null)
            {
                code.AppendLine(
                    "global::System.Collections.Generic.Dictionary<global::System.Guid, ("
                        + elementType
                        + "? Element, bool Duplicate)> __persistedByTemp"
                        + member.Id
                        + " = null!;"
                );
            }
        }
        if (tempProp is not null && keyProp is not null)
        {
            // Submitted temporary elements correlate with persisted assigned
            // elements by Guid; anything else fails instead of guessing.
            AppendElementLoopHead(code, "submitted." + prop, "__s", elementRef, elementNullableStruct);
            code.AppendLine("var __sk = __s." + keyProp + ";");
            code.AppendLine("if (!(" + isUnassigned + ")) continue;");
            code.AppendLine("var __st = __s." + tempProp + ";");
            code.AppendLine("if (!__st.HasValue) return \"The submitted element is missing its temporary identity.\";");
            code.AppendLine("if (__st.Value == global::System.Guid.Empty) return \"The submitted element has a reserved temporary identity.\";");
            code.AppendLine("if (!__persistedIndexesBuilt" + member.Id + ")");
            code.OpenBlock();
            AppendBuildPersistedIndexes(
                code,
                member,
                prop,
                elementType,
                keyType,
                keyProp,
                tempProp,
                true,
                keyCanBeNull,
                elementRef,
                elementNullableStruct
            );
            code.AppendLine("__persistedIndexesBuilt" + member.Id + " = true;");
            code.CloseBlock();
            code.AppendLine(
                "if (!__persistedByTemp"
                    + member.Id
                    + ".TryGetValue(__st.Value, out var __tempMatch"
                    + member.Id
                    + ")) return \"The server response omits a temporary identity.\";"
            );
            code.AppendLine("if (__tempMatch" + member.Id + ".Duplicate) return \"The server response duplicates a temporary identity.\";");
            code.AppendLine("var __found" + member.Id + " = __tempMatch" + member.Id + ".Element;");
            var foundUse = elementRef ? "__found" + member.Id + "!" : "__found" + member.Id + ".Value";
            code.AppendLine("var __pk" + member.Id + " = " + foundUse + "." + keyProp + ";");
            code.AppendLine(
                "if ("
                    + (
                        unassigned is null
                            ? "false"
                            : "global::System.Collections.Generic.EqualityComparer<"
                                + keyType
                                + ">.Default.Equals(__pk"
                                + member.Id
                                + ", "
                                + unassigned
                                + ")"
                    )
                    + ") return \"The server response leaves a temporary identity unassigned.\";"
            );
            code.AppendLine(
                "if (map.ContainsKey((__collectionScope" + member.Id + ", __st.Value))) return \"Duplicate temporary key in keyed collection.\";"
            );
            code.AppendLine("map[(__collectionScope" + member.Id + ", __st.Value)] = (object?)" + foundUse + ";");
            code.AppendLine("var __elemScope" + member.Id + " = __collectionScope" + member.Id + " + \"[\" + __st.Value.ToString(\"D\") + \"]\";");
            code.AppendLine(
                "var __childError = "
                    + reconcilerTypeName(elementType)
                    + ".__SparseCollectTempMap(__s, "
                    + foundUse
                    + ", map, __elemScope"
                    + member.Id
                    + ");"
            );
            code.AppendLine("if (__childError is not null) return __childError;");
            code.CloseBlock();
        }
        if (keyProp is not null)
        {
            // Assigned pairs recurse so nested temporary identities under
            // stable parents still correlate; drops absorb silently.
            AppendElementLoopHead(code, "submitted." + prop, "__s", elementRef, elementNullableStruct);
            code.AppendLine("var __sk = __s." + keyProp + ";");
            if (unassigned is not null)
            {
                code.AppendLine("if (" + isUnassigned + ") continue;");
            }
            code.AppendLine("if (!__persistedIndexesBuilt" + member.Id + ")");
            code.OpenBlock();
            AppendBuildPersistedIndexes(
                code,
                member,
                prop,
                elementType,
                keyType,
                keyProp,
                tempProp,
                false,
                keyCanBeNull,
                elementRef,
                elementNullableStruct
            );
            code.AppendLine("__persistedIndexesBuilt" + member.Id + " = true;");
            code.CloseBlock();
            code.AppendLine(elementType + "? __match" + member.Id + " = default;");
            if (keyCanBeNull)
            {
                code.AppendLine("if (__sk is null)");
                code.OpenBlock();
                code.AppendLine("if (__hasNullPersistedKey" + member.Id + ") __match" + member.Id + " = __firstNullPersistedKey" + member.Id + ";");
                code.CloseBlock();
                code.AppendLine("else");
                code.OpenBlock();
                code.AppendLine("__persistedByKey" + member.Id + ".TryGetValue(__sk, out __match" + member.Id + ");");
                code.CloseBlock();
            }
            else
            {
                code.AppendLine("__persistedByKey" + member.Id + ".TryGetValue(__sk, out __match" + member.Id + ");");
            }
            var matchUse = elementRef ? "__match" + member.Id : "__match" + member.Id + ".Value";
            var matchMissing = elementRef ? "if (__match" + member.Id + " is null)" : "if (!__match" + member.Id + ".HasValue)";
            code.AppendLine(matchMissing + " continue;");
            code.AppendLine(
                "var __parentKey"
                    + member.Id
                    + " = global::System.Convert.ToString((object?)__sk, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;"
            );
            code.AppendLine(
                "var __assignedScope"
                    + member.Id
                    + " = __collectionScope"
                    + member.Id
                    + " + \"[\" + __parentKey"
                    + member.Id
                    + ".Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"]\", \"\\\\]\") + \"]\";"
            );
            code.AppendLine(
                "var __nestedError = "
                    + reconcilerTypeName(elementType)
                    + ".__SparseCollectTempMap(__s, "
                    + matchUse
                    + ", map, __assignedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLine("if (__nestedError is not null) return __nestedError;");
            code.CloseBlock();
        }
        code.CloseBlock();
    }

    private static void AppendBuildPersistedIndexes(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string elementType,
        string keyType,
        string keyProp,
        string? tempProp,
        bool includeTemporaryIndex,
        bool keyCanBeNull,
        bool elementRef,
        bool elementNullableStruct
    )
    {
        code.AppendLine(
            "__persistedByKey" + member.Id + " = new global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + "?>();"
        );
        if (tempProp is not null && includeTemporaryIndex)
        {
            code.AppendLine(
                "__persistedByTemp"
                    + member.Id
                    + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, ("
                    + elementType
                    + "? Element, bool Duplicate)>();"
            );
        }
        AppendElementLoopHead(code, "persisted." + prop, "__p", elementRef, elementNullableStruct);
        code.AppendLine("var __persistedKey = __p." + keyProp + ";");
        if (keyCanBeNull)
        {
            code.AppendLine("if (__persistedKey is null)");
            code.OpenBlock();
            code.AppendLine("if (!__hasNullPersistedKey" + member.Id + ")");
            code.OpenBlock();
            code.AppendLine("__hasNullPersistedKey" + member.Id + " = true;");
            code.AppendLine("__firstNullPersistedKey" + member.Id + " = __p;");
            code.CloseBlock();
            code.CloseBlock();
            code.AppendLine("else if (!__persistedByKey" + member.Id + ".ContainsKey(__persistedKey))");
        }
        else
        {
            code.AppendLine("if (!__persistedByKey" + member.Id + ".ContainsKey(__persistedKey))");
        }
        code.AppendLine("__persistedByKey" + member.Id + ".Add(__persistedKey, __p);");
        if (tempProp is not null && includeTemporaryIndex)
        {
            code.AppendLine("var __persistedTemp = __p." + tempProp + ";");
            code.AppendLine("if (__persistedTemp.HasValue)");
            code.OpenBlock();
            code.AppendLine("if (__persistedByTemp" + member.Id + ".TryGetValue(__persistedTemp.Value, out var __existingTemp))");
            code.OpenBlock();
            code.AppendLine("__persistedByTemp" + member.Id + "[__persistedTemp.Value] = (__existingTemp.Element, true);");
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("__persistedByTemp" + member.Id + ".Add(__persistedTemp.Value, (__p, false));");
            code.CloseBlock();
            code.CloseBlock();
        }
        code.CloseBlock();
    }

    /// <summary>Emits a null-safe element loop head with a normalized non-null variable.</summary>
    /// <remarks>References narrow with <c>is null</c>.</remarks>
    private static void AppendElementLoopHead(SharedIndentedBuilder code, string list, string normalized, bool elementRef, bool elementNullableStruct)
    {
        if (elementRef)
        {
            code.AppendLine("foreach (var " + normalized + " in " + list + ")");
            code.OpenBlock();
            code.AppendLine("if (" + normalized + " is null) continue;");
        }
        else if (elementNullableStruct)
        {
            var raw = normalized + "Raw";
            code.AppendLine("foreach (var " + raw + " in " + list + ")");
            code.OpenBlock();
            code.AppendLine("if (!" + raw + ".HasValue) continue;");
            code.AppendLine("var " + normalized + " = " + raw + ".Value;");
        }
        else
        {
            code.AppendLine("foreach (var " + normalized + " in " + list + ")");
            code.OpenBlock();
        }
    }

    private static void AppendNestedCollect(SharedIndentedBuilder code, SparseMemberModel member, System.Func<string, string> reconcilerTypeName)
    {
        var childType = member.ChildModel!.Value.NonNullableName;
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var nullableValue = !member.ChildModel.Value.IsReferenceType && member.ChildModel.Value.Name.TrimEnd().EndsWith("?");
        if (member.ChildModel.Value.IsReferenceType)
        {
            // Locals promote nullability; property chains do not.
            code.OpenBlock();
            code.AppendLine("var __sub = submitted." + prop + ";");
            code.AppendLine("var __per = persisted." + prop + ";");
            code.AppendLine("if (__sub is not null && __per is not null)");
            code.OpenBlock();
            code.AppendLine("var __nestedScope" + member.Id + " = string.IsNullOrEmpty(__scope) ? \"" + prop + "\" : __scope + \"." + prop + "\";");
            code.AppendLine(
                "var __nestedError = " + reconcilerTypeName(childType) + ".__SparseCollectTempMap(__sub, __per, map, __nestedScope" + member.Id + ");"
            );
            code.AppendLine("if (__nestedError is not null) return __nestedError;");
            code.CloseBlock();
            code.CloseBlock();
        }
        else if (nullableValue)
        {
            code.OpenBlock();
            code.AppendLine("var __sub = submitted." + prop + ";");
            code.AppendLine("var __per = persisted." + prop + ";");
            code.AppendLine("if (__sub.HasValue && __per.HasValue)");
            code.OpenBlock();
            code.AppendLine("var __nestedScope" + member.Id + " = string.IsNullOrEmpty(__scope) ? \"" + prop + "\" : __scope + \"." + prop + "\";");
            code.AppendLine(
                "var __nestedError = "
                    + reconcilerTypeName(childType)
                    + ".__SparseCollectTempMap(__sub.Value, __per.Value, map, __nestedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLine("if (__nestedError is not null) return __nestedError;");
            code.CloseBlock();
            code.CloseBlock();
        }
        else
        {
            code.OpenBlock();
            code.AppendLine("var __nestedScope" + member.Id + " = string.IsNullOrEmpty(__scope) ? \"" + prop + "\" : __scope + \"." + prop + "\";");
            code.AppendLine(
                "var __nestedError = "
                    + reconcilerTypeName(childType)
                    + ".__SparseCollectTempMap(submitted."
                    + prop
                    + ", persisted."
                    + prop
                    + ", map, __nestedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLine("if (__nestedError is not null) return __nestedError;");
            code.CloseBlock();
        }
    }
}
