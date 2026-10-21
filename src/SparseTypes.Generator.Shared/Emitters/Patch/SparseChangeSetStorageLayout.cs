using System.Collections.Generic;
using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

internal sealed class SparseChangeSetStorageLayout
{
    private readonly string _runtime;
    private readonly HashSet<string> _usedNames;
    private readonly Dictionary<string, (string FieldName, string OptionalType)> _fields = new();

    internal SparseChangeSetStorageLayout(ImmutableArray<SparseMemberModel> members, string runtime)
    {
        _runtime = runtime;
        SparseChangeSetNaming.ComputePublicNames(members, out var properties, out var transitions);
        _usedNames = SparseChangeSetNaming.CreateReservedSet();
        _usedNames.UnionWith(properties.Values);
        _usedNames.UnionWith(transitions.Values);
        _usedNames.UnionWith(
            new[] { "__sparse_hasWhole", "__sparse_wholeBefore", "__sparse_wholeAfter" }
        );
        foreach (var member in members)
        {
            _usedNames.Add(BeforeField(member));
            _usedNames.Add(AfterField(member));
            _usedNames.Add(HasField(member));
            _usedNames.Add(NestedField(member));
            _usedNames.Add(KeyedWholeFlag(member));
            _usedNames.Add(KeyedWholeBefore(member));
            _usedNames.Add(KeyedWholeAfter(member));
            _usedNames.Add(KeyedItems(member));
            _usedNames.Add(KeyedBeforeOrder(member));
            _usedNames.Add(KeyedAfterOrder(member));
            _usedNames.Add(KeyedTempBeforeOrder(member));
            _usedNames.Add(KeyedTempAfterOrder(member));
        }
    }

    internal void AppendFields(SharedIndentedBuilder code, string field, string valueType)
    {
        var optional = _runtime + "Optional<" + valueType + ">";
        var fieldName = Reserve(field + "Optional");
        _fields.Add(field, (fieldName, optional));
        code.AppendLineAt(2, "private " + optional + " " + field + " => " + fieldName + ";");
    }

    internal void AppendPrivateFields(SharedIndentedBuilder code)
    {
        foreach (var field in _fields.Values)
        {
            code.AppendLineAt(
                2,
                "private readonly " + field.OptionalType + " " + field.FieldName + ";"
            );
        }
    }

    internal void AppendAssignment(SharedIndentedBuilder code, string field, string source)
    {
        code.AppendLineAt(3, _fields[field].FieldName + " = " + source + ";");
    }

    private string Reserve(string candidate)
    {
        if (_usedNames.Add(candidate))
        {
            return candidate;
        }

        var prefix = new System.Text.StringBuilder("_");
        while (_usedNames.Contains(prefix.ToString() + candidate))
        {
            prefix.Append('_');
        }

        var name = prefix.ToString() + candidate;
        _usedNames.Add(name);
        return name;
    }
}
