using System.IO.Compression;
using System.Text;

namespace NovelM.Tests.TestSupport;

internal static class GzipJson
{
    public static byte[] Compress(string json)
    {
        return Compress(Encoding.UTF8.GetBytes(json));
    }

    public static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
