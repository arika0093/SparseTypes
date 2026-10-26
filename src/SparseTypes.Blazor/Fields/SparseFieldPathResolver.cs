using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;
using SparseTypes.Generated;

namespace SparseTypes.Blazor;

/// <summary>Outcome of one non-throwing field-path walk.</summary>
/// <remarks>Resolved reports the complete path; otherwise Field holds the nearest surviving member (or the model root).</remarks>
internal readonly record struct SparseFieldResolution
{
    /// <summary>Gets whether the complete path resolved to a member field.</summary>
    public bool Resolved { get; init; }

    /// <summary>Gets whether no member survived and the model root is the nearest surface.</summary>
    public bool AtRoot { get; init; }

    /// <summary>Gets the resolved field, or the nearest surviving member field.</summary>
    public FieldIdentifier Field { get; init; }
}

/// <summary>
/// Resolves typed paths against a session model instance.
/// </summary>
/// <remarks>
/// The walk happens once per resolution and never relies on thrown exceptions
/// for expected missing-target states, so suffix retries do not repeat model
/// traversal (<see href="https://github.com/arika0093/SparseTypes/issues/333"/>'333').
/// </remarks>
internal static class SparseFieldPathResolver
{
    /// <summary>Resolves a canonical typed path, throwing when the path does not resolve.</summary>
    internal static FieldIdentifier Resolve(object model, SparsePath path, IDescriptorSet? descriptors = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (descriptors is not null)
        {
            return ResolveWithDescriptors(model, path, descriptors);
        }

        var resolution = ResolveNearest(model, path);
        if (!resolution.Resolved)
        {
            throw InvalidFieldPath(path.ToString());
        }

        return resolution.Field;
    }

    private static FieldIdentifier ResolveWithDescriptors(object model, SparsePath path, IDescriptorSet descriptors)
    {
        try
        {
            _ = descriptors.ResolveModel(path);
        }
        catch (ArgumentException)
        {
            throw InvalidFieldPath(path.ToString());
        }

        object? current = model;
        object? fieldOwner = null;
        string? fieldName = null;
        IDescriptorSet? scope = descriptors;
        IDescriptor? descriptor = null;
        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case SparsePathSegmentKind.Member:
                    if (current is null || scope is null || !scope.TryGet(segment.Name, out descriptor))
                    {
                        throw InvalidFieldPath(path.ToString());
                    }

                    fieldOwner = current;
                    fieldName = segment.Name;
                    current = descriptor.GetModelValue();
                    scope = descriptor.Child;
                    break;
                case SparsePathSegmentKind.Key:
                    if (descriptor?.Dictionary is { } dictionary)
                    {
                        if (
                            dictionary.KeyType is not { } keyType
                            || segment.Key is null
                            || !TryConvertDictionaryKeyText(keyType, segment.Key, out var key)
                            || !dictionary.TryGetValue(key, out _)
                        )
                        {
                            throw InvalidFieldPath(path.ToString());
                        }

                        current = dictionary.GetValueModel(key);
                        scope = dictionary.GetValueDescriptors(key);
                    }
                    else if (descriptor?.Array is { IsKeyed: true } array)
                    {
                        if (
                            array.KeyType is not { } keyType
                            || segment.Key is null
                            || !TryConvertDictionaryKeyText(keyType, segment.Key, out var key)
                        )
                        {
                            throw InvalidFieldPath(path.ToString());
                        }

                        var index = array.IndexOfKey(key);
                        if (index < 0)
                        {
                            throw InvalidFieldPath(path.ToString());
                        }

                        current = array.GetItemModel(index);
                        scope = array.GetItemDescriptors(index);
                    }
                    else
                    {
                        throw InvalidFieldPath(path.ToString());
                    }
                    descriptor = null;
                    break;
                case SparsePathSegmentKind.TemporaryKey:
                    var temporaryArray = descriptor?.Array;
                    if (temporaryArray is null)
                    {
                        throw InvalidFieldPath(path.ToString());
                    }

                    var temporaryIndex = temporaryArray.IndexOfTemporaryKey(segment.TemporaryKey!.Value);
                    if (temporaryIndex < 0)
                    {
                        throw InvalidFieldPath(path.ToString());
                    }

                    current = temporaryArray.GetItemModel(temporaryIndex);
                    scope = temporaryArray.GetItemDescriptors(temporaryIndex);
                    descriptor = null;
                    break;
                case SparsePathSegmentKind.Index:
                    if (descriptor?.Dictionary is { } indexedDictionary)
                    {
                        if (
                            !TryConvertKeyTextOrInvalidPath(
                                indexedDictionary.KeyType,
                                segment.Index.ToString(CultureInfo.InvariantCulture),
                                out var key
                            ) || !indexedDictionary.TryGetValue(key, out _)
                        )
                        {
                            throw InvalidFieldPath(path.ToString());
                        }

                        current = indexedDictionary.GetValueModel(key);
                        scope = indexedDictionary.GetValueDescriptors(key);
                    }
                    else if (descriptor?.Array is { } indexedArray)
                    {
                        if ((uint)segment.Index >= (uint)indexedArray.Count)
                        {
                            throw InvalidFieldPath(path.ToString());
                        }

                        current = indexedArray.GetItemModel(segment.Index);
                        scope = indexedArray.GetItemDescriptors(segment.Index);
                    }
                    else
                    {
                        throw InvalidFieldPath(path.ToString());
                    }
                    descriptor = null;
                    break;
                default:
                    throw InvalidFieldPath(path.ToString());
            }
        }

        if (fieldOwner is null || fieldName is null)
        {
            throw InvalidFieldPath(path.ToString());
        }

        return new FieldIdentifier(fieldOwner, fieldName);
    }

    /// <summary>Walks once and returns the exact field or the nearest surviving parent field.</summary>
    /// <remarks>
    /// Expected missing members/keys/indexes/null parents do not throw: the walk
    /// keeps the last member that actually resolved as the nearest candidate and
    /// reports the model root when nothing survived.
    /// </remarks>
    internal static SparseFieldResolution ResolveNearest(object model, SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(model);
        var root = new FieldIdentifier(model, string.Empty);
        if (path.IsRoot)
        {
            return RootResolution(root);
        }

        object? current = model;
        object? fieldOwner = null;
        string? fieldNamePart = null;
        foreach (var segment in path.Segments)
        {
            if (current is null)
            {
                return NearestField(fieldOwner, fieldNamePart, root);
            }

            switch (segment.Kind)
            {
                case SparsePathSegmentKind.Member:
                    if (
                        !SparseTypeMetadataCache.TryGetProperty(current.GetType(), segment.Name, out var property)
                        || property is null
                        || !property.CanRead
                    )
                    {
                        return NearestField(fieldOwner, fieldNamePart, root);
                    }

                    // The candidate is recorded only once the member is valid:
                    // trailing key/index segments validate existence while the
                    // reported field stays the owning member.
                    fieldOwner = current;
                    fieldNamePart = segment.Name;
                    current = property.GetValue(current);
                    break;
                case SparsePathSegmentKind.Key:
                    if (!TryResolveKeyedValue(current, segment.Key, out current))
                    {
                        return NearestField(fieldOwner, fieldNamePart, root);
                    }
                    break;
                case SparsePathSegmentKind.TemporaryKey:
                    if (!TryResolveTemporaryValue(current, segment.TemporaryKey, out current))
                    {
                        return NearestField(fieldOwner, fieldNamePart, root);
                    }
                    break;
                default:
                    if (!TryResolvePositionalValue(current, segment.Index, out current))
                    {
                        return NearestField(fieldOwner, fieldNamePart, root);
                    }
                    break;
            }
        }

        if (fieldOwner is null || fieldNamePart is null)
        {
            return RootResolution(root);
        }

        return new SparseFieldResolution
        {
            Resolved = true,
            AtRoot = false,
            Field = new FieldIdentifier(fieldOwner, fieldNamePart),
        };
    }

    private static SparseFieldResolution RootResolution(FieldIdentifier root) =>
        new()
        {
            Resolved = false,
            AtRoot = true,
            Field = root,
        };

    private static SparseFieldResolution NearestField(object? fieldOwner, string? fieldNamePart, FieldIdentifier root) =>
        fieldOwner is null || fieldNamePart is null
            ? RootResolution(root)
            : new SparseFieldResolution
            {
                Resolved = false,
                AtRoot = false,
                Field = new FieldIdentifier(fieldOwner, fieldNamePart),
            };

    private static bool TryResolveKeyedValue(object collection, object? key, out object? value)
    {
        value = null;
        if (collection is string || key is null)
        {
            return false;
        }

        // Key segments name stable entry identity (the EnumerateChanges
        // spelling), never positions.
        if (collection is IList list)
        {
            return TryResolveKeyedElement(collection, list, SparseTypeMetadataCache.GetSequenceElementType(collection.GetType()), key, out value);
        }

        if (collection is IDictionary dictionary)
        {
            if (!TryConvertDictionaryKey(collection, key, out var dictionaryKey))
            {
                return false;
            }

            if (!dictionary.Contains(dictionaryKey!))
            {
                return false;
            }

            value = dictionary[dictionaryKey!];
            return true;
        }

        var collectionType = collection.GetType();
        if (SparseTypeMetadataCache.TryGetReadOnlyDictionary(collectionType, out var dictionaryInterface, out var containsKey, out var indexer))
        {
            var keyType = dictionaryInterface.GetGenericArguments()[0];
            if (
                !TryConvertDictionaryKeyText(keyType, key, out var dictionaryKey)
                || containsKey is null
                || indexer is null
                || containsKey.Invoke(collection, [dictionaryKey]) is not true
            )
            {
                return false;
            }

            value = indexer.GetValue(collection, [dictionaryKey]);
            return true;
        }

        if (SparseTypeMetadataCache.TryGetReadOnlyList(collectionType, out var listInterface, out _))
        {
            return TryResolveKeyedElement(collection, (IEnumerable)collection, listInterface.GetGenericArguments()[0], key, out value);
        }

        return false;
    }

    private static bool TryResolvePositionalValue(object collection, int index, out object? value)
    {
        value = null;
        if (collection is string || index < 0)
        {
            return false;
        }

        if (collection is IList list)
        {
            if ((uint)index >= (uint)list.Count)
            {
                return false;
            }

            value = list[index];
            return true;
        }

        var collectionType = collection.GetType();
        if (SparseTypeMetadataCache.TryGetReadOnlyList(collectionType, out var listInterface, out var indexer))
        {
            // Index lookup, not linear enumeration.
            var elementType = listInterface.GetGenericArguments()[0];
            if (indexer is null || !SparseTypeMetadataCache.TryGetReadOnlyCollectionCount(collectionType, elementType, collection, out var count))
            {
                return false;
            }

            if ((uint)index >= (uint)count)
            {
                return false;
            }

            value = indexer.GetValue(collection, [index]);
            return true;
        }

        if (collection is IDictionary || TryGetReadOnlyDictionaryInterface(collection, out _))
        {
            // Historical spelling accepts unquoted numerics as dictionary keys,
            // so index segments fall back to invariant key text here.
            return TryResolveKeyedValue(collection, index.ToString(CultureInfo.InvariantCulture), out value);
        }

        return false;
    }

    private static bool TryConvertDictionaryKey(object collection, object key, out object? dictionaryKey)
    {
        dictionaryKey = key;
        if (collection is not IDictionary || key is not string text)
        {
            return true;
        }

        var keyType = SparseTypeMetadataCache.GetDictionaryKeyType(collection.GetType());
        if (keyType is null || keyType == typeof(string))
        {
            return true;
        }

        return TryConvertKeyTextOrInvalidPath(keyType, text, out dictionaryKey);
    }

    private static bool TryConvertDictionaryKeyText(Type keyType, object key, out object? dictionaryKey)
    {
        dictionaryKey = key;
        if (key is not string text)
        {
            return true;
        }

        return TryConvertKeyTextOrInvalidPath(keyType, text, out dictionaryKey);
    }

    private static bool TryConvertKeyTextOrInvalidPath(Type keyType, string key, out object? converted)
    {
        converted = null;
        try
        {
            return TryConvertKeyText(keyType, key, out converted);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException)
        {
            return false;
        }
    }

    private static bool TryConvertKeyText(Type keyType, string key, out object? converted)
    {
        // Guid is not IConvertible, so Convert.ChangeType cannot parse it.
        // Guid.TryParse is culture-independent.
        converted = null;
        var targetType = Nullable.GetUnderlyingType(keyType) ?? keyType;
        if (targetType == typeof(Guid))
        {
            if (Guid.TryParse(key, out var guid))
            {
                converted = guid;
                return true;
            }

            return false;
        }

        if (keyType.IsEnum)
        {
            // Case-sensitive parse keeps distinct display spellings apart.
            converted = Enum.Parse(keyType, key, ignoreCase: false);
            return true;
        }

        converted = Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryResolveTemporaryValue(object collection, Guid? temporaryKey, out object? value)
    {
        value = null;
        // The element is located through its SparseTemporaryKey member, so the
        // lookup follows row replacement exactly like the keyed lookup does.
        if (collection is string || temporaryKey is null || collection is not IEnumerable elements)
        {
            return false;
        }

        var elementType = SparseTypeMetadataCache.GetSequenceElementType(collection.GetType()) ?? FirstElementType(elements);
        if (elementType is null || !SparseTypeMetadataCache.TryGetTemporaryReader(elementType, out var reader) || reader is null)
        {
            return false;
        }

        var access = KeyedCollectionAccess.Create(collection, elements);
        if (SparseKeyedLookupIndex.TryGetByTemporaryKey(collection, access, reader, temporaryKey.Value, out var indexed))
        {
            value = indexed!;
            return true;
        }

        foreach (var element in elements)
        {
            if (element is not null && Equals(reader(element), temporaryKey.Value))
            {
                SparseKeyedLookupIndex.RebuildTemporaryKeys(collection, access, elements, reader);
                value = element;
                return true;
            }
        }

        // Removed or assigned entries no longer resolve against the live model.
        return false;
    }

    private static bool TryResolveKeyedElement(object collection, IEnumerable elements, Type? elementType, object? key, out object? value)
    {
        value = null;
        // Key segments name stable keys (the EnumerateChanges spelling),
        // never positions: even a numeric string key looks up key identity.
        elementType ??= FirstElementType(elements);
        if (elementType is null || !SparseTypeMetadataCache.TryGetKeyReader(elementType, out var reader) || reader is null)
        {
            return false;
        }

        // Parsed string keys convert to the key type; typed path keys compare
        // directly so equal display text never merges distinct keys.
        var convertedKey = key;
        if (key is string text)
        {
            if (!TryConvertKeyTextOrInvalidPath(reader.KeyType, text, out var parsed))
            {
                return false;
            }

            convertedKey = parsed;
        }

        var access = KeyedCollectionAccess.Create(collection, elements);
        if (convertedKey is not null && SparseKeyedLookupIndex.TryGetByKey(collection, access, reader, convertedKey, out var indexed))
        {
            value = indexed!;
            return true;
        }

        foreach (var element in elements)
        {
            if (element is not null && Equals(reader.GetKey(element), convertedKey))
            {
                if (convertedKey is not null)
                {
                    SparseKeyedLookupIndex.RebuildKeys(collection, access, elements, reader);
                }

                value = element;
                return true;
            }
        }

        // Removed keys no longer resolve against the live model.
        return false;
    }

    private static Type? FirstElementType(IEnumerable elements)
    {
        foreach (var element in elements)
        {
            if (element is not null)
            {
                return element.GetType();
            }
        }

        return null;
    }

    internal static bool TryGetReadOnlyDictionaryInterface(object collection, out Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (SparseTypeMetadataCache.TryGetReadOnlyDictionary(collection.GetType(), out interfaceType, out _, out _))
        {
            return true;
        }

        interfaceType = null!;
        return false;
    }

    private static ArgumentException InvalidFieldPath(string path) =>
        new($"The field path '{path}' does not resolve to a public model member.", nameof(path));
}
