using FlowDesk.Web.Application;

namespace FlowDesk.Web.Infrastructure;

public class LocalFileStorage : IFileStorage
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv", ".zip"
    };

    private readonly string _root;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IWebHostEnvironment environment, IConfiguration configuration, ILogger<LocalFileStorage> logger)
    {
        var configuredRoot = configuration["FileStorage:Root"] ?? "App_Data/uploads";
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRoot));
        Directory.CreateDirectory(_root);
        _logger = logger;
    }

    public async Task<(string StoredName, long Size)> SaveAsync(
        Stream stream,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Недопустимый тип файла. Разрешены изображения, PDF, Office-документы, TXT/CSV и ZIP.");
        }

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(_root, storedName);

        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            size += read;
            if (size > MaxFileSize)
            {
                await target.DisposeAsync();
                File.Delete(path);
                throw new InvalidOperationException("Файл больше 10 МБ.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        _logger.LogInformation("Сохранён файл {FileName} ({Size} байт) как {StoredName}", fileName, size, storedName);
        return (storedName, size);
    }

    public Stream OpenRead(string storedName)
    {
        var path = GetPath(storedName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл не найден.", path);
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public string GetPath(string storedName)
    {
        var path = Path.GetFullPath(Path.Combine(_root, storedName));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некорректный путь к файлу.");
        }
        return path;
    }

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        var path = GetPath(storedName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
