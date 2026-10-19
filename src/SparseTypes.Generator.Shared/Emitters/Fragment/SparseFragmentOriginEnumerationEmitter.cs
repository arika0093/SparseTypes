namespace SparseTypes.Generator.Shared;

internal static class SparseFragmentOriginEnumerationEmitter
{
    internal static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int position,
        string self,
        string originEntryType
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var prefix = "__SparseOriginMemberPath" + member.Id;
        var attribution = SparseFragmentOriginEmitter.MemberAttribution(self + ".", position, null);
        code.AppendLineAt(3, "if (" + self + "." + name + ".IsPresent)");
        code.AppendLineAt(3, "{");
        if (SparseFragmentOriginEmitter.IsFragmentDeep(member))
        {
            code.AppendLineAt(4, "if ((object?)" + self + "." + name + ".Value is null)");
            code.AppendLineAt(4, "{");
            AppendYield(code, 5, originEntryType, prefix, attribution);
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "foreach (var __sparse_child_"
                    + member.Id
                    + " in "
                    + self
                    + "."
                    + name
                    + ".Value!.EnumerateOrigins())"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "yield return new "
                    + originEntryType
                    + " { Path = "
                    + prefix
                    + ".Append(__sparse_child_"
                    + member.Id
                    + ".Path), Origin = __sparse_child_"
                    + member.Id
                    + ".Origin };"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
        }
        else if (SparseFragmentOriginEmitter.HasElementOrigins(member))
        {
            code.AppendLineAt(4, "if ((object?)" + self + "." + name + ".Value is null)");
            code.AppendLineAt(4, "{");
            AppendYield(code, 5, originEntryType, prefix, attribution);
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var __sparse_values_"
                    + member.Id
                    + " = global::System.Linq.Enumerable.ToArray("
                    + self
                    + "."
                    + name
                    + ".Value!);"
            );
            code.AppendLineAt(
                5,
                "var __sparse_elements_"
                    + member.Id
                    + " = "
                    + self
                    + ".__SparseElementOrigins is null ? null : "
                    + self
                    + ".__SparseElementOrigins["
                    + position
                    + "];"
            );
            code.AppendLineAt(5, "if (__sparse_elements_" + member.Id + " is null)");
            code.AppendLineAt(5, "{");
            AppendYield(code, 6, originEntryType, prefix, attribution);
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "var __sparse_element_prefix_" + member.Id + " = " + prefix + ";");
            code.AppendLineAt(
                6,
                "for (var __sparse_position_"
                    + member.Id
                    + " = 0; __sparse_position_"
                    + member.Id
                    + " < __sparse_values_"
                    + member.Id
                    + ".Length; __sparse_position_"
                    + member.Id
                    + "++)"
            );
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "var __sparse_element_origin_"
                    + member.Id
                    + " = (__sparse_position_"
                    + member.Id
                    + " >= __sparse_elements_"
                    + member.Id
                    + ".Length) ? "
                    + attribution
                    + " : (__sparse_elements_"
                    + member.Id
                    + "[__sparse_position_"
                    + member.Id
                    + "] ?? "
                    + attribution
                    + ");"
            );
            code.AppendLineAt(
                7,
                "yield return new "
                    + originEntryType
                    + " { Path = __sparse_element_prefix_"
                    + member.Id
                    + ".At(__sparse_position_"
                    + member.Id
                    + "), Origin = __sparse_element_origin_"
                    + member.Id
                    + " };"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
        }
        else
        {
            AppendYield(code, 4, originEntryType, prefix, attribution);
        }

        code.AppendLineAt(3, "}");
    }

    private static void AppendYield(
        SharedIndentedBuilder code,
        int indent,
        string originEntryType,
        string path,
        string attribution
    ) =>
        code.AppendLineAt(
            indent,
            "yield return new "
                + originEntryType
                + " { Path = "
                + path
                + ", Origin = "
                + attribution
                + " };"
        );
}
