using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDModLoader.Core;

/// <summary>Единые настройки JSON + безопасная загрузка/сохранение файлов состояния.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Читает файл; если он повреждён — переименовывает в *.bad и возвращает новый объект.</summary>
    public static T LoadOrNew<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (JsonException)
        {
            try { File.Move(path, path + ".bad", true); } catch { /* ignore */ }
            return new T();
        }
    }

    /// <summary>Атомарная запись: сначала во временный файл, затем замена.</summary>
    public static void Save(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, value.GetType(), Options));
        File.Move(tmp, path, true);
    }
}
