using System.Buffers.Binary;
using System.Text;

namespace ErgoProxy.Core.Tunnel.Dns;

public sealed class DnsQuestion
{
    public string Name { get; set; } = string.Empty;
    public ushort Type { get; set; }
    public ushort Class { get; set; } = 1; // IN
}

public sealed class DnsRecord
{
    public string Name { get; set; } = string.Empty;
    public ushort Type { get; set; }
    public ushort Class { get; set; } = 1;
    public uint Ttl { get; set; } = 60;
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public sealed class DnsMessage
{
    public ushort Id { get; set; }
    public bool IsResponse { get; set; }
    public byte OpCode { get; set; }
    public bool AuthoritativeAnswer { get; set; }
    public bool Truncation { get; set; }
    public bool RecursionDesired { get; set; }
    public bool RecursionAvailable { get; set; }
    public byte ResponseCode { get; set; } // 0 = NoError, 3 = NXDomain, 2 = ServerFailure

    public List<DnsQuestion> Questions { get; set; } = new();
    public List<DnsRecord> Answers { get; set; } = new();
    public List<DnsRecord> Authorities { get; set; } = new();
    public List<DnsRecord> Additionals { get; set; } = new();

    public static bool TryParse(ReadOnlySpan<byte> data, out DnsMessage message)
    {
        message = new DnsMessage();
        if (data.Length < 12) return false;

        message.Id = BinaryPrimitives.ReadUInt16BigEndian(data[0..2]);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..4]);
        message.IsResponse = (flags & 0x8000) != 0;
        message.OpCode = (byte)((flags >> 11) & 0x0F);
        message.AuthoritativeAnswer = (flags & 0x0400) != 0;
        message.Truncation = (flags & 0x0200) != 0;
        message.RecursionDesired = (flags & 0x0100) != 0;
        message.RecursionAvailable = (flags & 0x0080) != 0;
        message.ResponseCode = (byte)(flags & 0x000F);

        var qdCount = BinaryPrimitives.ReadUInt16BigEndian(data[4..6]);
        var anCount = BinaryPrimitives.ReadUInt16BigEndian(data[6..8]);
        var nsCount = BinaryPrimitives.ReadUInt16BigEndian(data[8..10]);
        var arCount = BinaryPrimitives.ReadUInt16BigEndian(data[10..12]);

        var offset = 12;

        for (var i = 0; i < qdCount; i++)
        {
            if (!ReadName(data, ref offset, out var name)) return false;
            if (offset + 4 > data.Length) return false;
            var qType = BinaryPrimitives.ReadUInt16BigEndian(data[offset..(offset + 2)]);
            var qClass = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..(offset + 4)]);
            offset += 4;
            message.Questions.Add(new DnsQuestion { Name = name, Type = qType, Class = qClass });
        }

        for (var i = 0; i < anCount; i++)
        {
            if (!ReadRecord(data, ref offset, out var rec)) return false;
            message.Answers.Add(rec);
        }

        return true;
    }

    private static bool ReadRecord(ReadOnlySpan<byte> data, ref int offset, out DnsRecord record)
    {
        record = new DnsRecord();
        if (!ReadName(data, ref offset, out var name)) return false;
        if (offset + 10 > data.Length) return false;

        var type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..(offset + 2)]);
        var cls = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..(offset + 4)]);
        var ttl = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..(offset + 8)]);
        var rdLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..(offset + 10)]);
        offset += 10;

        if (offset + rdLength > data.Length) return false;
        var rdata = data.Slice(offset, rdLength).ToArray();
        offset += rdLength;

        record.Name = name;
        record.Type = type;
        record.Class = cls;
        record.Ttl = ttl;
        record.Data = rdata;
        return true;
    }

    public static bool ReadName(ReadOnlySpan<byte> data, ref int offset, out string name)
    {
        var sb = new StringBuilder();
        var current = offset;
        var jumped = false;
        var jumpCount = 0;

        while (true)
        {
            if (current >= data.Length) { name = string.Empty; return false; }
            var len = data[current];
            if (len == 0)
            {
                if (!jumped) offset = current + 1;
                break;
            }

            if ((len & 0xC0) == 0xC0)
            {
                // Pointer compression
                if (current + 1 >= data.Length) { name = string.Empty; return false; }
                var ptr = ((len & 0x3F) << 8) | data[current + 1];
                if (!jumped) offset = current + 2;
                jumped = true;
                current = ptr;
                if (++jumpCount > 20) { name = string.Empty; return false; } // loop detection
                continue;
            }

            current++;
            if (current + len > data.Length) { name = string.Empty; return false; }
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(data.Slice(current, len)));
            current += len;
        }

        name = sb.ToString();
        return true;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BinaryPrimitives.ReverseEndianness(Id));

        ushort flags = 0;
        if (IsResponse) flags |= 0x8000;
        flags |= (ushort)((OpCode & 0x0F) << 11);
        if (AuthoritativeAnswer) flags |= 0x0400;
        if (Truncation) flags |= 0x0200;
        if (RecursionDesired) flags |= 0x0100;
        if (RecursionAvailable) flags |= 0x0080;
        flags |= (ushort)(ResponseCode & 0x0F);

        writer.Write(BinaryPrimitives.ReverseEndianness(flags));
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)Questions.Count));
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)Answers.Count));
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)Authorities.Count));
        writer.Write(BinaryPrimitives.ReverseEndianness((ushort)Additionals.Count));

        foreach (var q in Questions)
        {
            WriteName(writer, q.Name);
            writer.Write(BinaryPrimitives.ReverseEndianness(q.Type));
            writer.Write(BinaryPrimitives.ReverseEndianness(q.Class));
        }

        foreach (var a in Answers)
        {
            WriteName(writer, a.Name);
            writer.Write(BinaryPrimitives.ReverseEndianness(a.Type));
            writer.Write(BinaryPrimitives.ReverseEndianness(a.Class));
            writer.Write(BinaryPrimitives.ReverseEndianness(a.Ttl));
            writer.Write(BinaryPrimitives.ReverseEndianness((ushort)a.Data.Length));
            writer.Write(a.Data);
        }

        return ms.ToArray();
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            writer.Write((byte)0);
            return;
        }

        var parts = name.Split('.');
        foreach (var p in parts)
        {
            var bytes = Encoding.ASCII.GetBytes(p);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0);
    }
}
