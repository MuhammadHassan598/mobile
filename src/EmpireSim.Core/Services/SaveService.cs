using System.Text.Json;
using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// Saves / loads the game as JSON in a platform folder supplied by the
/// host app (FileSystem.AppDataDirectory on device, any folder in tests).
/// SQLite can replace this later without touching game logic.
/// </summary>
public sealed class SaveService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _folder;
    private const string FileName = "savegame.json";

    public SaveService(string folder)
    {
        _folder = folder;
    }

    private string Path => System.IO.Path.Combine(_folder, FileName);

    public bool HasSave => File.Exists(Path);

    public async Task SaveAsync(GameState state)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(Path, json);
    }

    public async Task<GameState?> LoadAsync()
    {
        if (!HasSave) return null;
        try
        {
            var json = await File.ReadAllTextAsync(Path);
            return JsonSerializer.Deserialize<GameState>(json, JsonOptions);
        }
        catch
        {
            return null; // Corrupt save -> start fresh rather than crash.
        }
    }

    public void DeleteSave()
    {
        if (HasSave) File.Delete(Path);
    }
}
