using System.Text.Json.Serialization;
using SparseTypes.NativeAotFixtures;

namespace SparseTypes.NativeAotSmoke;

// Verification-only parity context: the feature needs no registration, this
// context only proves the generated wire matches System.Text.Json defaults.
[JsonSerializable(typeof(PocoLeaf))]
[JsonSerializable(typeof(PocoLeafChild))]
[JsonSerializable(typeof(PocoItem))]
public sealed partial class PocoParityContext : JsonSerializerContext { }
