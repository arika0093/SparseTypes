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
        code.AppendLine("if (" + self + "." + name + ".IsPresent)");
        code.OpenBlock();
        if (SparseFragmentOriginEmitter.IsFragmentDeep(member))
        {
            code.AppendLine("if ((object?)" + self + "." + name + ".Value is null)");
            code.OpenBlock();
            AppendYield(code, originEntryType, prefix, attribution);
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "foreach (var __sparse_child_"
                    + member.Id
                    + " in "
                    + self
                    + "."
                    + name
                    + ".Value!.EnumerateOrigins())"
            );
            code.OpenBlock();
            code.AppendLine(
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
            code.CloseBlock();
            code.CloseBlock();
        }
        else if (SparseFragmentOriginEmitter.HasElementOrigins(member))
        {
            code.AppendLine("if ((object?)" + self + "." + name + ".Value is null)");
            code.OpenBlock();
            AppendYield(code, originEntryType, prefix, attribution);
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "var __sparse_values_"
                    + member.Id
                    + " = global::System.Linq.Enumerable.ToArray("
                    + self
                    + "."
                    + name
                    + ".Value!);"
            );
            code.AppendLine(
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
            code.AppendLine("if (__sparse_elements_" + member.Id + " is null)");
            code.OpenBlock();
            AppendYield(code, originEntryType, prefix, attribution);
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("var __sparse_element_prefix_" + member.Id + " = " + prefix + ";");
            code.AppendLine(
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
            code.OpenBlock();
            code.AppendLine(
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
            code.AppendLine(
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
            code.CloseBlock();
            code.CloseBlock();
            code.CloseBlock();
        }
        else
        {
            AppendYield(code, originEntryType, prefix, attribution);
        }

        code.CloseBlock();
    }

    private static void AppendYield(
        SharedIndentedBuilder code,
        string originEntryType,
        string path,
        string attribution
    ) =>
        code.AppendLine(
            "yield return new "
                + originEntryType
                + " { Path = "
                + path
                + ", Origin = "
                + attribution
                + " };"
        );
}
