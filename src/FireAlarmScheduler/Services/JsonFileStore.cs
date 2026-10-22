// Purpose: Loads and saves a list of records as one JSON file on local disk.
// Writes go through a temporary file and an atomic replace; a lock serializes access within the process.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FireAlarmScheduler.Services;

public class JsonFileStore<T>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonFileStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<List<T>> ReadAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await LoadAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Loads the records, applies <paramref name="change"/>, and saves the result, all under one lock
    /// so concurrent requests cannot overwrite each other's changes. Nothing is saved if the change throws.
    /// </summary>
    public async Task<TResult> UpdateAsync<TResult>(Func<List<T>, TResult> change)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await LoadAsync();
            var result = change(items);
            await SaveAsync(items);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reads the file, treating a missing file as an empty list. A corrupted file throws instead of
    /// returning an empty list, so a later save cannot silently wipe the existing data.
    /// </summary>
    private async Task<List<T>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        try
        {
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, SerializerOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"The data file '{_filePath}' is not valid JSON. Fix or restore it before continuing; it has not been changed.", ex);
        }
    }

    private async Task SaveAsync(List<T> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
        var tempPath = _filePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, items, SerializerOptions);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
