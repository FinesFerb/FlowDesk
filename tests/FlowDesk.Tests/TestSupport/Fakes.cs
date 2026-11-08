using FlowDesk.Web.Application;

namespace FlowDesk.Tests.TestSupport;

/// <summary>Фиктивный отправитель email: письма накапливаются в памяти для проверок.</summary>
public sealed class RecordingEmailSender : IAppEmailSender
{
    public sealed record EmailMessage(string To, string Subject, string Body);

    public List<EmailMessage> Messages { get; } = [];

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        Messages.Add(new EmailMessage(to, subject, htmlBody));
        return Task.CompletedTask;
    }
}

/// <summary>Фиктивное файловое хранилище: файлы хранятся в памяти.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public Task<(string StoredName, long Size)> SaveAsync(
        Stream stream, string fileName, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();
        var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        Files[storedName] = data;
        return Task.FromResult((storedName, (long)data.Length));
    }

    public Stream OpenRead(string storedName) => new MemoryStream(Files[storedName], writable: false);

    public string GetPath(string storedName) => storedName;

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        Files.Remove(storedName);
        return Task.CompletedTask;
    }
}
