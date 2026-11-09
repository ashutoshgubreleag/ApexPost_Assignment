using System.Text.Json.Serialization;

namespace ApexPost.Client;

// Hand-written half of the generated client (survives regeneration). The API writes enums as strings
// ("Created", "PickedUp"), so the client must read them as strings too.
public partial class ApexPostClient
{
    static partial void UpdateJsonSerializerSettings(System.Text.Json.JsonSerializerOptions settings)
    {
        settings.Converters.Add(new JsonStringEnumConverter());
    }
}
