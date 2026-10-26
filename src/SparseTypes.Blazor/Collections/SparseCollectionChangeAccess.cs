using System.Reflection;
using System.Runtime.CompilerServices;

namespace SparseTypes.Blazor;

/// <summary>Closes typed collection queries once per model and key type.</summary>
internal abstract class SparseCollectionChangeAccess
{
    private static readonly MethodInfo KeyedFactory = typeof(SparseCollectionChangeAccess).GetMethod(
        nameof(CreateKeyed),
        BindingFlags.Public | BindingFlags.Static
    )!;
    private static readonly MethodInfo DictionaryFactory = typeof(SparseCollectionChangeAccess).GetMethod(
        nameof(CreateDictionary),
        BindingFlags.Public | BindingFlags.Static
    )!;
    private static readonly ConditionalWeakTable<Type, ModelAccess> Models = new();

    private sealed class ModelAccess
    {
        internal ModelAccess(Type modelType)
        {
            ChangeSetType = FindChangeSetType(modelType);
        }

        internal Type? ChangeSetType { get; }
        internal ConditionalWeakTable<Type, SparseCollectionChangeAccess> Keyed { get; } = new();
        internal ConditionalWeakTable<Type, SparseCollectionChangeAccess> StructuralDictionary { get; } = new();
        internal ConditionalWeakTable<Type, SparseCollectionChangeAccess> ScalarDictionary { get; } = new();

        private static Type? FindChangeSetType(Type modelType)
        {
            foreach (var nested in modelType.GetNestedTypes(BindingFlags.Public))
            {
                foreach (var contract in nested.GetInterfaces())
                {
                    if (
                        contract.IsGenericType
                        && contract.GetGenericTypeDefinition() == typeof(ISparseChangeSet<>)
                        && contract.GetGenericArguments()[0] == modelType
                    )
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }

    internal static SparseCollectionChangeAccess? ForKeyed(Type keyType, Type elementType)
    {
        var model = Models.GetValue(elementType, static type => new ModelAccess(type));
        return model.ChangeSetType is { } changeType ? GetAccess(model.Keyed, KeyedFactory, keyType, elementType, changeType) : null;
    }

    internal static SparseCollectionChangeAccess? ForDictionary(Type keyType, Type valueType, bool hasChild)
    {
        var model = Models.GetValue(valueType, static type => new ModelAccess(type));
        var changeType = hasChild ? model.ChangeSetType : valueType;
        if (changeType is null)
        {
            return null;
        }

        var cache = hasChild ? model.StructuralDictionary : model.ScalarDictionary;
        return GetAccess(cache, DictionaryFactory, keyType, valueType, changeType);
    }

    internal abstract object? GetChange(ISparseChangeSet changes, string memberName);

    internal abstract SparseCollectionState GetState(object change, SparsePath path);

    internal abstract ISparseChangeSet? GetItemChange(object change, SparsePathSegment segment);

    private static SparseCollectionChangeAccess GetAccess(
        ConditionalWeakTable<Type, SparseCollectionChangeAccess> cache,
        MethodInfo factory,
        Type keyType,
        Type valueType,
        Type changeType
    ) => cache.TryGetValue(keyType, out var access) ? access : AddAccess(cache, factory, keyType, valueType, changeType);

    private static SparseCollectionChangeAccess AddAccess(
        ConditionalWeakTable<Type, SparseCollectionChangeAccess> cache,
        MethodInfo factory,
        Type keyType,
        Type valueType,
        Type changeType
    ) => cache.GetValue(keyType, key => Create(factory, key, valueType, changeType));

    private static SparseCollectionChangeAccess Create(MethodInfo factory, Type key, Type value, Type change) =>
        factory.MakeGenericMethod(key, value, change).CreateDelegate<Func<SparseCollectionChangeAccess>>()();

    /// <summary>Creates typed keyed access during cache initialisation.</summary>
    public static SparseCollectionChangeAccess CreateKeyed<TKey, TElement, TChange>() => new KeyedAccess<TKey, TElement, TChange>();

    /// <summary>Creates typed dictionary access during cache initialisation.</summary>
    public static SparseCollectionChangeAccess CreateDictionary<TKey, TValue, TChange>() => new DictionaryAccess<TKey, TValue, TChange>();

    private sealed class KeyedAccess<TKey, TElement, TChange> : SparseCollectionChangeAccess
    {
        internal override object? GetChange(ISparseChangeSet changes, string memberName) =>
            changes.TryGetKeyedChange<TKey, TElement, TChange>(memberName, out var change) ? change : null;

        internal override SparseCollectionState GetState(object change, SparsePath path) =>
            ((ISparseKeyedChange<TKey, TElement, TChange>)change).KeyedState(path);

        internal override ISparseChangeSet? GetItemChange(object change, SparsePathSegment segment)
        {
            if (segment.Kind == SparsePathSegmentKind.TemporaryKey)
            {
                return change is ISparseTemporaryKeyedChange<TKey, TElement, TChange> temporary
                    ? temporary.GetTemporaryChange(segment.TemporaryKey!.Value).Edit as ISparseChangeSet
                    : null;
            }

            return segment.Kind == SparsePathSegmentKind.Key && segment.Key is TKey key
                ? ((ISparseKeyedChange<TKey, TElement, TChange>)change).GetChange(key).Edit as ISparseChangeSet
                : null;
        }
    }

    private sealed class DictionaryAccess<TKey, TValue, TChange> : SparseCollectionChangeAccess
    {
        internal override object? GetChange(ISparseChangeSet changes, string memberName) =>
            changes.TryGetDictionaryChange<TKey, TValue, TChange>(memberName, out var change) ? change : null;

        internal override SparseCollectionState GetState(object change, SparsePath path) =>
            ((ISparseDictionaryChange<TKey, TValue, TChange>)change).DictionaryState(path);

        internal override ISparseChangeSet? GetItemChange(object change, SparsePathSegment segment) =>
            segment.Kind == SparsePathSegmentKind.Key
            && segment.Key is TKey key
            && ((ISparseDictionaryChange<TKey, TValue, TChange>)change).GetChange(key)
                is ISparseStructuralDictionaryItemChange<TKey, TValue, TChange> item
                ? item.Edit as ISparseChangeSet
                : null;
    }
}
