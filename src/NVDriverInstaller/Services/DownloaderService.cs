using System.IO;
using System.Net.Http;

namespace NVDriverInstaller.Services;

public static class DownloaderService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    static DownloaderService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NVDriverInstaller/1.0");
    }

    public static async Task<bool> DownloadFileAsync(
        string url,
        string destinationFilePath,
        IProgress<(long bytesRead, long totalBytes, double percentage)>? progress = null,
        CancellationToken ct = default)
    {
        var tempFile = destinationFilePath + ".tmp";
        var destDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    if (progress != null && totalBytes > 0)
                    {
                        var pct = (double)totalRead / totalBytes * 100.0;
                        progress.Report((totalRead, totalBytes, pct));
                    }
                }
            }

            if (File.Exists(destinationFilePath))
                File.Delete(destinationFilePath);

            File.Move(tempFile, destinationFilePath);
            return true;
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            return false;
        }
    }
}
