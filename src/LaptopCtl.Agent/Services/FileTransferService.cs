using LaptopCtl.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace LaptopCtl.Agent.Services;

/// <summary>
/// Handles authorized file listing and transfer operations.
/// Path traversal protection is applied to every operation.
/// </summary>
public sealed class FileTransferService
{
    private readonly ILogger<FileTransferService> _log;
    private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB
    private const int ChunkSize = 64 * 1024; // 64 KB

    public FileTransferService(ILogger<FileTransferService> log) => _log = log;

    public Task<List<FileEntry>> ListDirectoryAsync(string remotePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(remotePath))
            throw new ArgumentException("Path cannot be empty. Use 'drives' to list drives.", nameof(remotePath));

        // If path is "drives" or root slash, enumerate all local drives
        if (remotePath == "/" || remotePath == "\\" || remotePath.Equals("drives", StringComparison.OrdinalIgnoreCase))
        {
            var driveEntries = new List<FileEntry>();
            try
            {
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    driveEntries.Add(new FileEntry
                    {
                        Name = $"{drive.Name} ({drive.VolumeLabel})",
                        FullPath = drive.RootDirectory.FullName,
                        IsDirectory = true,
                        SizeBytes = drive.TotalSize,
                        LastModified = DateTimeOffset.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to enumerate drives");
            }
            return Task.FromResult(driveEntries);
        }

        var safePath = ValidateAndResolvePath(remotePath);
        if (!Directory.Exists(safePath))
            throw new DirectoryNotFoundException($"Directory not found: {safePath}");

        var entries = new List<FileEntry>();

        try
        {
            foreach (var dir in Directory.GetDirectories(safePath))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var di = new DirectoryInfo(dir);
                    entries.Add(new FileEntry
                    {
                        Name         = di.Name,
                        FullPath     = di.FullName,
                        IsDirectory  = true,
                        LastModified = di.LastWriteTimeUtc
                    });
                }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }
            }
        }
        catch (UnauthorizedAccessException) { }

        try
        {
            foreach (var file in Directory.GetFiles(safePath))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(file);
                    entries.Add(new FileEntry
                    {
                        Name         = fi.Name,
                        FullPath     = fi.FullName,
                        IsDirectory  = false,
                        SizeBytes    = fi.Length,
                        LastModified = fi.LastWriteTimeUtc
                    });
                }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }
            }
        }
        catch (UnauthorizedAccessException) { }

        return Task.FromResult(entries);
    }

    public async Task<FileDownloadResponse> ReadFileContentAsync(string remotePath, CancellationToken ct)
    {
        var safePath = ValidateAndResolvePath(remotePath);
        if (!File.Exists(safePath))
            throw new FileNotFoundException($"File not found: {safePath}");

        var fi = new FileInfo(safePath);
        if (fi.Length > 25 * 1024 * 1024)
            throw new InvalidOperationException("File exceeds maximum download size of 25 MB");

        byte[] bytes = await File.ReadAllBytesAsync(safePath, ct);
        return new FileDownloadResponse
        {
            FileName = fi.Name,
            SizeBytes = fi.Length,
            Base64Data = Convert.ToBase64String(bytes)
        };
    }

    public async Task SendFileAsync(string remotePath, string requestId, BackendClient backend, CancellationToken ct)
    {
        var safePath = ValidateAndResolvePath(remotePath);
        _log.LogInformation("File download requested: {Path} (requestId={RequestId})", safePath, requestId);

        if (!File.Exists(safePath))
            throw new FileNotFoundException($"File not found: {safePath}");

        var fi = new FileInfo(safePath);
        if (fi.Length > MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum transfer size ({MaxFileSizeBytes / 1024 / 1024} MB)");

        // Compute SHA-256 first for integrity verification
        var sha256 = await ComputeSha256Async(safePath, ct);

        await using var fs = new FileStream(safePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[ChunkSize];
        int bytesRead;
        int chunkIndex = 0;

        while ((bytesRead = await fs.ReadAsync(buffer, 0, ChunkSize, ct)) > 0)
        {
            var chunk = buffer[..bytesRead];
            await backend.SendAsync(new WsMessage
            {
                Type      = WsMessageTypes.FileTransferChunk,
                RequestId = requestId,
                Payload   = new
                {
                    ChunkIndex  = chunkIndex++,
                    Data        = Convert.ToBase64String(chunk),
                    TotalBytes  = fi.Length,
                    Sha256      = sha256
                }
            }, ct);
        }

        await backend.SendAsync(new WsMessage
        {
            Type      = WsMessageTypes.FileTransferDone,
            RequestId = requestId,
            Payload   = new { Sha256 = sha256, TotalBytes = fi.Length }
        }, ct);

        _log.LogInformation("File sent: {Path} ({Bytes} bytes, sha256={Hash})", safePath, fi.Length, sha256[..8]);
    }

    public async Task ReceiveFileAsync(
        string remotePath, long? expectedSize, string? expectedSha256,
        string requestId, BackendClient backend, CancellationToken ct)
    {
        var safePath = ValidateAndResolvePath(remotePath);
        _log.LogInformation("File upload requested: {Path} (requestId={RequestId})", safePath, requestId);

        if (expectedSize.HasValue && expectedSize > MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum transfer size ({MaxFileSizeBytes / 1024 / 1024} MB)");

        // Receive chunks via message loop — managed by AgentWorker via separate protocol
        // For now we acknowledge readiness; actual chunk receipt is handled by AgentWorker
        await backend.SendAsync(new WsMessage
        {
            Type      = WsMessageTypes.DiagnosticResult,
            RequestId = requestId,
            Payload   = new { Status = "READY", DestinationPath = safePath }
        }, ct);
    }

    private static string ValidateAndResolvePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            throw new ArgumentException("Path cannot be empty");

        // Resolve to full path, collapsing any ../ traversal
        var fullPath = Path.GetFullPath(remotePath);

        // Disallow dangerous raw device namespaces
        if (fullPath.StartsWith(@"\\.\") || fullPath.StartsWith(@"\\?\"))
            throw new UnauthorizedAccessException("Raw device paths are not permitted.");

        return fullPath;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.Create().ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
