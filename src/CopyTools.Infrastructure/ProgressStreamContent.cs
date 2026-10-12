using System.Net;

namespace CopyTools.Infrastructure;

internal sealed class ProgressStreamContent(
    Stream source,
    long? length,
    IProgress<long>? progress,
    int bufferSize = 1024 * 1024) : HttpContent
{
    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length ?? 0;
        return length is not null;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[bufferSize];
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read));
            progress?.Report(read);
        }
    }
}
