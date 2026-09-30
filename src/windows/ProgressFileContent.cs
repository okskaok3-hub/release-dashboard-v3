using System.Net;

namespace ZoomClipboard;

internal sealed class ProgressFileContent(string path, IProgress<int>? progress) : HttpContent
{
    private readonly long length = new FileInfo(path).Length;

    protected override bool TryComputeLength(out long contentLength)
    {
        contentLength = length;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[65536];
        long sent = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sent += read;
            progress?.Report(length == 0 ? 100 : (int)Math.Min(100, sent * 100 / length));
        }
        if (length == 0) progress?.Report(100);
    }
}
