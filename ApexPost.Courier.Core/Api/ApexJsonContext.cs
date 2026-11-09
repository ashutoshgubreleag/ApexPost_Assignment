using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Courier.Core.Api;

/// <summary>
/// System.Text.Json source generation: the serializers for every request and response are produced at compile time.
/// That removes reflection (faster start-up on a phone, and safe with trimming / AOT, which MAUI release builds use).
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScanUploadWire))]
[JsonSerializable(typeof(ScanUploadResultWire))]
[JsonSerializable(typeof(TokenRequestWire))]
[JsonSerializable(typeof(TokenResponseWire))]
[JsonSerializable(typeof(ProblemWire))]
public sealed partial class ApexJsonContext : JsonSerializerContext;
