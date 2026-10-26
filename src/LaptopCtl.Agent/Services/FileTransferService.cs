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

    // Allowed base directories for file operations. In a real deployment
    // this would be configurable and restricted to user-owned paths.
    private static readonly string[] AllowedRoots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
    };

    public FileTransferService(ILogger<FileTransferService> log) => _log = log;

    public Task<List<FileEntry>> ListDirectoryAsync(string remotePath, CancellationToken ct)
    {
        var safePath = ValidateAndResolvePath(remotePath);
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(safePath))
            throw new DirectoryNotFoundException($"Directory not found: {safePath}");

        var entries = new List<FileEntry>();

        foreach (var dir in Directory.GetDirectories(safePath))
        {
            ct.ThrowIfCancellationRequested();
            var di = new DirectoryInfo(dir);
            entries.Add(new FileEntry
            {
                Name        = di.Name,
                FullPath    = di.FullName,
                IsDirectory = true,
                LastModified = di.LastWriteTimeUtc
            });
        }

        foreach (var file in Directory.GetFiles(safePath))
        {
            ct.ThrowIfCancellationRequested();
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

        return Task.FromResult(entries);
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

        // Verify the path is within an allowed root
        bool allowed = AllowedRoots.Any(root =>
            !string.IsNullOrEmpty(root) &&
            fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            throw new UnauthorizedAccessException(
                $"Access to path '{fullPath}' is not permitted. " +
                "Only paths within user home directories are accessible.");

        return fullPath;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.Create().ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
