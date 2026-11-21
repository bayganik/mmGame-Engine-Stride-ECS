namespace mmPiano;

// Which MIDI channels a .mid file actually uses, with a note
// count each — MeltySynth keeps its parsed messages internal,
// so this is a small Standard MIDI File scan of our own (header
// + track events, running status included). A file that fails to
// scan reports no channels; playback itself does not depend on it.
public static class MidiChannels
{
    public sealed record ChannelInfo(int Channel, int Notes);

    public static ChannelInfo[] Scan(string path)
    {
        var counts = new Dictionary<int, int>();
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 14 || bytes[0] != (byte)'M' || bytes[1] != (byte)'T' || bytes[2] != (byte)'h' || bytes[3] != (byte)'d')
            return Array.Empty<ChannelInfo>();
        int trackCount = (bytes[10] << 8) | bytes[11];
        int pos = 14;
        for (int t = 0; t < trackCount && pos + 8 <= bytes.Length; t++)
        {
            if (bytes[pos] != (byte)'M' || bytes[pos + 1] != (byte)'T' || bytes[pos + 2] != (byte)'r' || bytes[pos + 3] != (byte)'k')
                break;
            int trackLen = (bytes[pos + 4] << 24) | (bytes[pos + 5] << 16) | (bytes[pos + 6] << 8) | bytes[pos + 7];
            int end = Math.Min(bytes.Length, pos + 8 + trackLen);
            pos += 8;
            int status = 0;
            while (pos < end)
            {
                ReadVarLen(bytes, ref pos, end); // delta time
                if (pos >= end)
                    break;
                int b = bytes[pos];
                if (b == 0xFF) // meta event: type, length, data
                {
                    pos += 2;
                    int dataLen = ReadVarLen(bytes, ref pos, end);
                    pos += dataLen;
                }
                else if (b is 0xF0 or 0xF7) // SysEx: length, data
                {
                    pos++;
                    int dataLen = ReadVarLen(bytes, ref pos, end);
                    pos += dataLen;
                }
                else
                {
                    if (b >= 0x80)
                    {
                        status = b;
                        pos++;
                    }
                    int cmd = status & 0xF0;
                    int dataBytes = cmd is 0xC0 or 0xD0 ? 1 : 2;
                    if (cmd == 0x90 && pos + 1 < end && bytes[pos + 1] > 0)
                    {
                        int ch = status & 0x0F;
                        counts.TryGetValue(ch, out int n);
                        counts[ch] = n + 1;
                    }
                    pos += dataBytes;
                }
            }
            pos = end;
        }
        return counts.OrderBy(kv => kv.Key)
            .Select(kv => new ChannelInfo(kv.Key, kv.Value))
            .ToArray();
    }

    // Reads a variable-length quantity, advancing past it.
    private static int ReadVarLen(byte[] bytes, ref int pos, int end)
    {
        int value = 0;
        while (pos < end)
        {
            int b = bytes[pos++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
                break;
        }
        return value;
    }
}
