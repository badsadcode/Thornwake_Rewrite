using System.IO.Compression;

/// <summary>Tiny dependency-free PNG writer (8-bit RGB, or RGBA) for map previews and generated textures.</summary>
internal static class Png
{
    public static void Write(string path, int width, int height, byte[] rgb, bool alpha = false)
    {
        int bpp = alpha ? 4 : 3;
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        WriteInt(header, 0, width); WriteInt(header, 4, height);
        header[8] = 8; header[9] = (byte)(alpha ? 6 : 2); // 8-bit, RGB or RGBA
        Chunk(file, "IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
            for (int y = 0; y < height; y++) { z.WriteByte(0); z.Write(rgb, y * width * bpp, width * bpp); }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteInt(len, 0, data.Length); s.Write(len);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        s.Write(body);
        var crc = new byte[4]; WriteInt(crc, 0, (int)Crc(body)); s.Write(crc);
    }

    private static void WriteInt(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
