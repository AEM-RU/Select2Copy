using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Select2Copy.Services;

internal static class JsonFile
{
    public static T? Deserialize<T>(string json) where T : class
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var serializer = new DataContractJsonSerializer(typeof(T));
        return serializer.ReadObject(stream) as T;
    }

    public static string Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(typeof(T));
        serializer.WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
