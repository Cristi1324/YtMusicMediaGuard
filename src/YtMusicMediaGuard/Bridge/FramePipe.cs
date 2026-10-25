namespace YtMusicMediaGuard;

internal static class FramePipe
{
    public const string Name = "YtMusicMediaGuard";
    public const int MaxBytes = 1_000_000;

    public static byte[]? Read(Stream stream)
    {
        var header = new byte[4];
        if (!ReadExact(stream, header)) return null;
        var length = BitConverter.ToInt32(header, 0);
        if (length < 0 || length > MaxBytes)
            throw new IOException($"Rejected a pipe message of {length} bytes.");
        var body = new byte[length];
        if (!ReadExact(stream, body)) return null;
        return body;
    }

    public static void Write(Stream stream, byte[] body)
    {
        if (body.Length > MaxBytes)
            throw new IOException($"Refusing to write a pipe message of {body.Length} bytes.");
        var header = BitConverter.GetBytes(body.Length);
        stream.Write(header, 0, header.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    public static void Copy(Stream input, Stream output)
    {
        while (Read(input) is byte[] body)
            Write(output, body);
    }

    private static bool ReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) return false;
            offset += read;
        }

        return true;
    }
}
