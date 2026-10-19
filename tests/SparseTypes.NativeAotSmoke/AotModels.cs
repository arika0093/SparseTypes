using System.Text.Json.Serialization;
using SparseTypes.NativeAotFixtures;

namespace SparseTypes.NativeAotSmoke;

// sample: payload-native-aot-models
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(AotOrderStatus))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(AotServer))]
[JsonSerializable(typeof(List<AotServer>))]
[JsonSerializable(typeof(PayloadItem))]
[JsonSerializable(typeof(List<PayloadItem>))]
internal sealed partial class AotSerializerContext : JsonSerializerContext { }
// /sample
