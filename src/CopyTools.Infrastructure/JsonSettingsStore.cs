using System.Text.Json;
using CopyTools.Application;
using CopyTools.Domain;

namespace CopyTools.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;

    public JsonSettingsStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new ApplicationSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            return (await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, cancellationToken: cancellationToken)
                ?? new ApplicationSettings()).Normalize();
        }
        catch (JsonException)
        {
            var corrupt = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(_path, corrupt, false);
            return new ApplicationSettings();
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings.Normalize(),
                    new JsonSerializerOptions { WriteIndented = true },
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, _path, true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }
}
