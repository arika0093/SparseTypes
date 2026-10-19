using System.Text.Json.Serialization;

namespace SparseTypes.NativeAotFixtures;

// sample: payload-native-aot-models
[JsonSerializable(typeof(AotServer))]
[JsonSerializable(typeof(PayloadItem))]
public sealed partial class PayloadLeafContext : JsonSerializerContext { }
// /sample
