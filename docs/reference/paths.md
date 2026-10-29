# Paths

Package and namespace: `SparseTypes`. Paths identify a root model and an ordered sequence of member, key, temporary-key, or index segments. They do not retain an object instance.

## SparsePath

`SparsePath` is a readonly value type. Prefer generated typed paths when the model is known.

| Member | Contract |
| --- | --- |
| `SparsePath(Type rootType, params SparsePathSegment[] segments)` | Copies the supplied segments. |
| `SparsePath(Type rootType, IEnumerable<SparsePathSegment> segments)` | Same construction from an enumerable. Null root or segments throw. |
| `Root(Type rootType)`, `Root<TModel>()` | Static root factories. |
| `RootType`, `Segments`, `Depth`, `IsRoot`, `Parent` | Identity and structure; Parent is null at the root. |
| `Member(string name)` | Appends a CLR member name. |
| `Key<TKey>(TKey key)` | Appends a key, retaining its actual type. |
| `TemporaryKey(Guid temporaryKey)` | Appends temporary identity for an unassigned keyed row. |
| `At(int index)` | Appends a zero-based positional index. |
| `Append(SparsePathSegment segment)` | Appends one segment. |
| `Append(SparsePath suffix)` | Appends segments while retaining this root type. |
| `Prepend(SparsePath prefix)` | Prepends segments and adopts the prefix root type. |
| `PrependMember(string name)` | Prepends a member while retaining this root type. |
| `IsAncestorOf(other)`, `IsDescendantOf(other)` | Strict relationship: a path is not its own ancestor. |
| `StartsWith(other)` | Inclusive prefix relationship. |
| `Equals`, `GetHashCode`, `==`, `!=` | Compare root type and typed segments. |
| `ToString()` | Canonical text; whole root is `$root`. |
| `Parse(Type rootType, string text)`, `Parse<TModel>(string text)` | Parse textual paths; malformed input throws `FormatException`. |
| `TryParse(Type rootType, string? text, out SparsePath path)`, `TryParse<TModel>(string? text, out SparsePath path)` | Return false for malformed text. |

Member factories reject empty names, `$root`, path delimiters (`.`, `[`, `]`, `"`, `\`), control characters, and UTF-16 surrogates. `TryParse` returns false for invalid member text. Constructors and `Append(SparsePathSegment)` reject `default(SparsePathSegment)` with `ArgumentException`; create segments through their factories.

Null keys render as `[null]`; empty string keys render as `[""]`. Parsing `[null]` produces a null string key. Unquoted bracket text becomes an index only when it is a canonical non-negative `Int32` (for example, `[123]`). Other unquoted text, including `[0123]`, remains a string key. Control characters and UTF-16 surrogates in quoted keys use escaped text.

Text is not a lossless encoding of arbitrary CLR key types. Keys render as invariant text in quoted brackets, and parsing does not recover the original numeric or composite CLR key type. Use generated key paths for typed lookup.

## Typed paths and segments

`SparsePath<TModel,TValue>(SparsePath path)` adds root/value typing; a mismatched root throws ArgumentException. Members are `Path`, `UntypedPath`, `TypedPath`, `RootType`, `ValueType`, `CanWriteModelValueInPlace`, and `WriteModelValueInPlace(current, updated)`, plus equality, text rendering, and implicit conversion to untyped `SparsePath`. On the runtime wrapper CanWriteModelValueInPlace is false and WriteModelValueInPlace throws NotSupportedException; generated navigation builders supply model-specific writers.

`ISparseTypedPath<TModel,TValue>` exposes `TypedPath`, `UntypedPath`, `CanWriteModelValueInPlace`, and `WriteModelValueInPlace`. Generated navigation types implement it with model-specific capabilities. Use the capability flag before relying on in-place writes.

`SparsePathSegment` factories are `Member(name)`, `KeyOf<TKey>(key)`, `KeyOf(object? key, Type keyType)`, `At(index)`, and `TemporaryKeyOf(guid)`. Read-only properties are `Kind`, `Name`, `Key`, `KeyType`, `Index`, and `TemporaryKey`; inspect the property matching Kind. `SparsePathSegmentKind` contains `Member`, `Key`, `Index`, and `TemporaryKey`. Segment equality and hashing retain typed identity.

`SparsePathSegments` is an `IReadOnlyList<SparsePathSegment>` with `Count`, an indexer, and an allocation-free concrete enumerator.

`SparsePathQueries.Find(conflicts, path)` is an extension on `IReadOnlyList<SparseConflict>`, with typed and untyped overloads; it returns the exact conflict or null.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-paths -->
```csharp
var session = new ReferenceSettings().CreateEditSession();
SparsePath<ReferenceSettings, string?> theme = session.Paths.Theme;
var itemTitle = session.Paths.Items.Key("a").Title;
var untyped = SparsePath.Root<ReferenceSettings>().Member("Items").Key("a").Member("Title");
var samePath = itemTitle.UntypedPath == untyped;
```
<!-- /sample -->

`samePath` is true. The item path renders as `Items["a"].Title`. A key path keeps identifying the same row after reordering; an index path addresses the current position.

See [generated paths](generated-paths-and-transitions.md) for model-specific navigation.


## Parsing resource limits

`Parse` and `TryParse` process segments iteratively, without a recursive call for each segment. They impose no maximum text length or segment count. Memory use grows with the input text and parsed segments; JSON depth limits do not restrict the contents of a path string.

Applications accepting untrusted path text must enforce their own text-length budget before parsing and a segment-count budget before navigation or other processing. Select those budgets for the model and endpoint. The parser's acceptance of a path does not establish that the path is authorized or affordable to resolve.
