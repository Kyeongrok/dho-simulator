using System.Runtime.InteropServices;

namespace Dho.Audio;

/// <summary>
/// 끊김 없이 되풀이되는 소리 하나(빗소리) — 효과음(<see cref="SoundEffects"/>)은 한 번에 하나만 나고 새 소리가 앞의 것을 끊으니,
/// 이어져야 하는 소리는 winmm 의 waveOut 장치를 따로 열어 버퍼 하나를 되돌이표(WHDR_BEGINLOOP | WHDR_ENDLOOP)로 건다.
/// 넘겨받는 것은 WAV 파일 바이트(<c>GameSounds.Wave</c> — 머리 60바이트: fmt 덩이가 20 에서 20바이트, data 길이가 56, 몸이 60 부터).
/// </summary>
internal sealed class LoopSound : IDisposable
{
    private IntPtr _device, _header;
    private GCHandle _wave;
    private string _key = "";

    /// <summary>그 소리를 되풀이해 튼다 — 이미 같은 소리가 나고 있으면 그대로 둔다.</summary>
    public void Play(string key, Func<byte[]?> load)
    {
        if (key == _key && _device != IntPtr.Zero) return;
        Stop();
        if (load() is not { Length: > 64 } wave) return;
        _wave = GCHandle.Alloc(wave, GCHandleType.Pinned);
        IntPtr start = _wave.AddrOfPinnedObject();
        int length = Math.Min(BitConverter.ToInt32(wave, 56), wave.Length - 60);
        if (length <= 0 || waveOutOpen(out _device, -1, start + 20, IntPtr.Zero, IntPtr.Zero, 0) != 0) { _device = IntPtr.Zero; _wave.Free(); return; }
        _header = Marshal.AllocHGlobal(HeaderSize);
        for (int i = 0; i < HeaderSize; i++) Marshal.WriteByte(_header, i, 0);
        Marshal.WriteIntPtr(_header, 0, start + 60);                    // lpData
        Marshal.WriteInt32(_header, IntPtr.Size, length);               // dwBufferLength
        Marshal.WriteInt32(_header, IntPtr.Size * 2 + 8, 4 | 8);        // dwFlags: 되돌이표의 처음이자 끝
        Marshal.WriteInt32(_header, IntPtr.Size * 2 + 12, int.MaxValue); // dwLoops
        waveOutPrepareHeader(_device, _header, HeaderSize);
        waveOutWrite(_device, _header, HeaderSize);
        _key = key;
    }

    public void Stop()
    {
        if (_device == IntPtr.Zero) return;
        waveOutReset(_device);
        waveOutUnprepareHeader(_device, _header, HeaderSize);
        waveOutClose(_device);
        Marshal.FreeHGlobal(_header);
        _wave.Free();
        (_device, _header, _key) = (IntPtr.Zero, IntPtr.Zero, "");
    }

    public void Dispose() => Stop();

    // WAVEHDR: 포인터 · u32 · u32 · 포인터 · u32 · u32 · 포인터 · 포인터 (64비트에서 48바이트)
    private static readonly int HeaderSize = IntPtr.Size * 4 + 16;

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr device, int id, IntPtr format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr device);
}
