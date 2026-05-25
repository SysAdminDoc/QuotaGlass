using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace QuotaGlass.Shared;

public static class AtomicJsonFile
{
    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, typeInfo);
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(path))
        {
            File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
