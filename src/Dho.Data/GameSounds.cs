using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>원본 효과음 하나 — 묶음과 그 안의 차례, 길이와 꼴.</summary>
public sealed record GameSound(int Bank, int Index, double Seconds, int Rate, int Channels)
{
    /// <summary>설정에 적는 이름("묶음:차례").</summary>
    public string Key => $"{Bank}:{Index}";
}

/// <summary>
/// 원본의 효과음 묶음(<c>0006\001000.bin</c> 머리 · <c>0006\002000.bin</c> 몸)을 읽는다 — 개발도구의 목록과 듣기용.
/// 짜임은 게임의 <c>SoundEffects</c> 와 같다: 블록 머리가 있는 IMA ADPCM 이라 WAV 머리만 붙이면 윈도가 튼다.
/// </summary>
public static class GameSounds
{
    private static byte[]? _head, _body;

    private static bool Load()
    {
        try
        {
            _head ??= GvoFiles.Read(@"0006\001000.bin");
            _body ??= GvoFiles.Read(@"0006\002000.bin");
            return true;
        }
        catch (Exception) { return false; }
    }

    private static IEnumerable<(int Bank, int Index, int At)> Records()
    {
        int banks = BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(8));
        for (int bank = 0; bank < banks; bank++)
        {
            int from = BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(0x10 + bank * 8)), size = BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(0x14 + bank * 8));
            for (int at = from, seen = 0; at + 28 <= from + size; at += 2)
            {
                int U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + o));
                if (U16(0) > 1 || U16(4) is not (11025 or 22050 or 44100) || U16(8) is not (0x100 or 0x200 or 0x400 or 0x800) || U16(10) is not (0x1F9 or 0x3F9 or 0x7F9)) continue;
                yield return (bank, seen++, at);
            }
        }
    }

    /// <summary>모든 효과음(게임 폴더가 없으면 빈 목록).</summary>
    public static List<GameSound> All()
    {
        var all = new List<GameSound>();
        if (!Load()) return all;
        foreach (var (bank, index, at) in Records())
        {
            int rate = BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + 4)), block = BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + 8));
            int perBlock = BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + 10)), samples = BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(at + 16));
            all.Add(new GameSound(bank, index, Math.Round(samples / (double)rate, 2), rate, 4 + (perBlock - 1) / 2 == block ? 1 : 2));
        }
        return all;
    }

    /// <summary>그 소리의 WAV 파일 내용. 없으면 null.</summary>
    public static byte[]? Wave(int bank, int index)
    {
        if (!Load()) return null;
        foreach (var (b, i, at) in Records())
        {
            if (b != bank || i != index) continue;
            int rate = BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + 4)), block = BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + 8));
            int perBlock = BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at + 10)), samples = BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(at + 16));
            int where = BinaryPrimitives.ReadInt32LittleEndian(_body.AsSpan(0x10 + bank * 8)) + BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(at + 20));
            int length = BinaryPrimitives.ReadInt32LittleEndian(_head.AsSpan(at + 24));
            if (where < 0 || length <= 0 || where + (long)length > _body!.Length) return null;
            int channels = 4 + (perBlock - 1) / 2 == block ? 1 : 2;
            var wave = new byte[60 + length];
            using var writer = new BinaryWriter(new MemoryStream(wave));
            writer.Write("RIFF"u8); writer.Write(52 + length); writer.Write("WAVEfmt "u8); writer.Write(20);
            writer.Write((ushort)0x11); writer.Write((ushort)channels); writer.Write(rate); writer.Write(rate * block / perBlock);
            writer.Write((ushort)block); writer.Write((ushort)4); writer.Write((ushort)2); writer.Write((ushort)perBlock);
            writer.Write("fact"u8); writer.Write(4); writer.Write(samples);
            writer.Write("data"u8); writer.Write(length); writer.Write(_body, where, length);
            return wave;
        }
        return null;
    }
}
