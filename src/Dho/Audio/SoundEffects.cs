using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Dho.Data;

namespace Dho.Audio;

/// <summary>
/// 원본의 효과음 — <c>0006\001000.bin</c>(머리)과 <c>0006\002000.bin</c>(몸).
/// </summary>
/// <remarks>
/// 두 파일 모두 u32 0, u32 크기, u32 묶음 수(48), u32 첫 자리, (자리, 크기) × 묶음 수.
/// 머리의 묶음 안에 소리 설명이 있다: u16 스테레오 여부, u16 ?, u16 표본율, u16 ?, u16 블록 크기(512 · 1024), u16 블록당 표본 수(1017),
/// u32 0, u32 표본 수, u32 몸의 묶음 안 자리, u32 바이트 수. 몸은 윈도 WAV 의 IMA ADPCM 그대로(블록마다 머리 4바이트 × 채널)라서,
/// WAV 머리만 붙여 윈도에 맡긴다. 설명 레코드의 자리표는 못 풀어서, 그 꼴에 맞는 데를 훑어 차례대로 센다.
/// </remarks>
internal sealed class SoundEffects : IDisposable
{
    private byte[]? _head, _body;
    private bool _missing;
    private readonly Dictionary<(int Bank, int Index), GCHandle> _sounds = new();

    /// <summary>"묶음:차례" 꼴의 소리를 튼다(한 번에 하나 — 새 소리가 앞의 것을 끊는다).</summary>
    public void Play(string which)
    {
        var parts = which.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int bank) || !int.TryParse(parts[1], out int index)) return;
        if (!_sounds.TryGetValue((bank, index), out var sound))
        {
            if (Wave(bank, index) is not { } wave) return;
            _sounds[(bank, index)] = sound = GCHandle.Alloc(wave, GCHandleType.Pinned);
        }
        PlaySoundW(sound.AddrOfPinnedObject(), IntPtr.Zero, 0x0004 | 0x0001 | 0x0002);      // 메모리 · 기다리지 않고 · 없으면 조용히
    }

    public const int Banks = 48;

    /// <summary>그 묶음에 든 소리의 수.</summary>
    public int Count(int bank)
    {
        int count = 0;
        while (count < 400 && Wave(bank, count, probe: true) != null) count++;
        return count;
    }

    private byte[]? Wave(int bank, int index, bool probe = false)
    {
        if (_missing) return null;
        try
        {
            _head ??= GvoFiles.Read(@"0006\001000.bin");
            _body ??= GvoFiles.Read(@"0006\002000.bin");
        }
        catch (Exception) { _missing = true; return null; }
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_head.AsSpan(at));
        int I32(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        if (bank < 0 || bank >= I32(_head, 8)) return null;
        int from = I32(_head, 0x10 + bank * 8), size = I32(_head, 0x14 + bank * 8), baseAt = I32(_body, 0x10 + bank * 8);
        for (int at = from, seen = 0; at + 28 <= from + size; at += 2)
        {
            int rate = U16(at + 4), block = U16(at + 8), perBlock = U16(at + 10);
            if (U16(at) > 1 || rate is not (11025 or 22050 or 44100) || block is not (0x100 or 0x200 or 0x400 or 0x800) || perBlock is not (0x1F9 or 0x3F9 or 0x7F9)) continue;
            if (seen++ != index) continue;
            int samples = I32(_head, at + 16), where = baseAt + I32(_head, at + 20), length = I32(_head, at + 24);
            if (where < 0 || length <= 0 || where + (long)length > _body.Length) return null;
            if (probe) return [];
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

    public void Dispose()
    {
        PlaySoundW(IntPtr.Zero, IntPtr.Zero, 0);
        foreach (var sound in _sounds.Values) sound.Free();
        _sounds.Clear();
    }

    [DllImport("winmm.dll")]
    private static extern bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);
}
