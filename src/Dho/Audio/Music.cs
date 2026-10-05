using System.Runtime.InteropServices;
using Dho.Data;
using NVorbis;

namespace Dho.Audio;

/// <summary>
/// 배경음 — 원본 음악 <c>0006\0000NN.bin</c> 을 틀어 둔다. 번호가 바뀌면 갈아 끼운다.
/// </summary>
/// <remarks>
/// 파일은 <c>KOVS</c>: "KOVS", u32 Ogg 바이트 수, u32 되돌이 시작(샘플), 0 으로 채운 32바이트 머리, 그 뒤 Ogg Vorbis.
/// Ogg 의 처음 256바이트는 차례 번호(0 ~ 255)로 XOR 돼 있다.
/// 풀기는 NVorbis, 내보내기는 winmm 의 waveOut — 뒤에서 도는 실 하나가 버퍼 넷을 돌려 가며 채운다.
/// </remarks>
internal sealed class Music : IDisposable
{
    private const int Buffers = 4, BufferMilliseconds = 120;

    private readonly Thread _thread;
    private volatile int _wanted;
    private volatile bool _quit;
    private volatile float _volume = 0.6f;

    public Music()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "music" };
        _thread.Start();
    }

    /// <summary>0 ~ 1.</summary>
    public float Volume { get => _volume; set => _volume = Math.Clamp(value, 0, 1); }

    /// <summary>이 번호의 음악을 튼다(같은 것이면 그대로). 0 이면 끈다.</summary>
    public void Play(int number) => _wanted = number;

    private void Run()
    {
        int playing = 0;
        IntPtr device = IntPtr.Zero;
        VorbisReader? reader = null;
        long loopStart = 0;
        var headers = new IntPtr[Buffers];
        float[] samples = [];
        float gain = 0;

        void Close()
        {
            if (device != IntPtr.Zero)
            {
                waveOutReset(device);
                foreach (var header in headers)
                    if (header != IntPtr.Zero)
                    {
                        waveOutUnprepareHeader(device, header, HeaderSize);
                        Marshal.FreeHGlobal(Marshal.ReadIntPtr(header));
                        Marshal.FreeHGlobal(header);
                    }
                waveOutClose(device);
            }
            Array.Clear(headers);
            device = IntPtr.Zero;
            reader?.Dispose();
            reader = null;
        }

        while (!_quit)
        {
            int wanted = _wanted;
            if (wanted != playing)
            {
                // 갈아 끼우기 전에 잦아들게 한다
                if (reader != null && gain > 0.02f) { gain -= 0.08f; }
                else
                {
                    Close();
                    playing = wanted;
                    gain = 0;
                    try
                    {
                        if (wanted > 0 && Open(wanted, out reader, out loopStart))
                        {
                            var format = new WaveFormat
                            {
                                Tag = 1, Channels = (short)reader!.Channels, SamplesPerSecond = reader.SampleRate, BitsPerSample = 16,
                                BlockAlign = (short)(reader.Channels * 2), BytesPerSecond = reader.SampleRate * reader.Channels * 2,
                            };
                            if (waveOutOpen(out device, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0) { device = IntPtr.Zero; reader.Dispose(); reader = null; }
                            else
                            {
                                int count = reader.SampleRate * BufferMilliseconds / 1000 * reader.Channels;
                                samples = new float[count];
                                for (int i = 0; i < Buffers; i++)
                                {
                                    headers[i] = Marshal.AllocHGlobal(HeaderSize);
                                    for (int k = 0; k < HeaderSize; k += 4) Marshal.WriteInt32(headers[i], k, 0);
                                    Marshal.WriteIntPtr(headers[i], Marshal.AllocHGlobal(count * 2));
                                    Marshal.WriteInt32(headers[i], IntPtr.Size, count * 2);
                                    Marshal.WriteInt32(headers[i], FlagsAt, Done);
                                }
                            }
                        }
                    }
                    catch (Exception) { Close(); }            // 음악 파일이 없거나 깨졌으면 조용히 넘어간다
                }
            }
            else if (gain < 1) gain = Math.Min(1, gain + 0.05f);

            if (reader != null && device != IntPtr.Zero)
            {
                foreach (var header in headers)
                {
                    int flags = Marshal.ReadInt32(header, FlagsAt);
                    if ((flags & Done) == 0 && (flags & Prepared) != 0) continue;       // 아직 나가는 중
                    if ((flags & Prepared) != 0) waveOutUnprepareHeader(device, header, HeaderSize);

                    int filled = 0;
                    while (filled < samples.Length)
                    {
                        int got = reader.ReadSamples(samples, filled, samples.Length - filled);
                        if (got > 0) { filled += got; continue; }
                        // 끝까지 갔다 — 되돌이 자리로
                        reader.SamplePosition = Math.Clamp(loopStart, 0, Math.Max(0, reader.TotalSamples - 1));
                    }
                    var data = Marshal.ReadIntPtr(header);
                    float level = _volume * Math.Max(0, gain);
                    for (int i = 0; i < samples.Length; i++)
                        Marshal.WriteInt16(data, i * 2, (short)Math.Clamp(samples[i] * level * 32767f, -32768f, 32767f));
                    Marshal.WriteInt32(header, FlagsAt, 0);
                    waveOutPrepareHeader(device, header, HeaderSize);
                    waveOutWrite(device, header, HeaderSize);
                }
            }
            Thread.Sleep(20);
        }
        Close();
    }

    /// <summary>KOVS 를 벗겨 Vorbis 읽개를 연다.</summary>
    private static bool Open(int number, out VorbisReader? reader, out long loopStart)
    {
        reader = null;
        loopStart = 0;
        string path = GvoFiles.PathOf($@"0006\{number:D6}.bin");
        if (!File.Exists(path)) return false;
        var file = File.ReadAllBytes(path);
        if (file.Length < 64 || !file.AsSpan(0, 4).SequenceEqual("KOVS"u8)) return false;
        loopStart = BitConverter.ToUInt32(file, 8);
        var ogg = file.AsSpan(32).ToArray();
        for (int i = 0; i < Math.Min(256, ogg.Length); i++) ogg[i] ^= (byte)i;
        reader = new VorbisReader(new MemoryStream(ogg), true);
        return true;
    }

    public void Dispose()
    {
        _quit = true;
        _thread.Join(500);
    }

    // ── winmm ────────────────────────────────────────────────────────────────

    // WAVEHDR: 자료 포인터, u32 길이, u32 녹음된 바이트, 포인터 사용자, u32 플래그, u32 되풀이, 포인터 둘
    private static readonly int HeaderSize = IntPtr.Size * 4 + 16, FlagsAt = IntPtr.Size * 2 + 8;
    private const int Done = 1, Prepared = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormat
    {
        public short Tag, Channels;
        public int SamplesPerSecond, BytesPerSecond;
        public short BlockAlign, BitsPerSample, ExtraSize;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr device, int id, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr device);
}
