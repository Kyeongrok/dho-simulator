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
/// <summary>게임의 일 하나에 매는 소리 — 이름(설정의 열쇠), 화면에 보이는 말, 기본 소리, 어디서 나는가.</summary>
public sealed record SoundCue(string Cue, string Label, string Default, string Where);

public static class GameSounds
{
    /// <summary>
    /// 소리가 나는 일의 목록 — 게임의 기본값과 개발도구의 「기능별 소리」 표가 함께 쓴다.
    /// 기본 소리는 사용자가 들어 보고 알려 준 원본의 번호(묶음:차례)다. 재해마다의 소리(Disaster번호)는 재해 표에서 따로 붙는다.
    /// </summary>
    public static readonly SoundCue[] Cues =
    [
        new("Click", "단추 누름", "0:0", "모든 단추 · 쪽지(「날짜 +1」 따위)"),
        new("Quest", "퀘스트", "0:2", "의뢰를 받을 때"),
        new("Error", "오류", "0:3", "할 수 없는 일을 했을 때"),
        new("Mastery", "숙련도", "0:4", "스킬 숙련도가 오를 때, 조타 숙련도가 가득 찼을 때"),
        new("Warn", "경고", "0:5", "재해 · 폭풍(따로 안 매었을 때)"),
        new("Alarm", "적 출현", "0:5", "해적이 다가올 때, 해전 · 육상전이 벌어질 때"),
        new("Skill", "스킬", "0:6", "스킬 · 부스트 아이템을 쓸 때"),
        new("SkillUp", "레벨업", "0:7", "레벨 · 스킬 랭크 · 작위가 오를 때, 건조가 끝났을 때"),
        new("Done", "완료", "0:8", "의뢰 보고, 옵션 스킬 부여, 싸움에서 이겼을 때"),
        new("Discover", "발견", "0:8", "도시를 처음 찾았을 때 · 발견물을 찾았을 때(원본의 발견 소리를 「효과음 고르기」에서 맨다 — 기본은 완료 소리)"),
        new("JobChange", "전직", "0:9", "전직증을 써서 직업을 바꿨을 때"),
        new("StudyDone", "연구 완료", "0:10", "대학의 연구를 마쳤을 때"),
        new("Eat", "음식", "0:11", "음식을 먹을 때"),
        new("Turn", "선회", "0:12", "바다에서 배를 돌릴 때"),
        new("Bank", "저금", "0:14", "은행에 맡기고 찾을 때, 맡긴 배를 받을 때"),
        new("Door", "입구", "0:15", "건물에 드나들 때"),
        new("Part", "부품", "0:20", "선박부품 · 데코를 달고 뗄 때"),
        new("Buy", "구매", "0:23", "물건을 살 때, 투자할 때"),
        new("Drunk", "술", "0:24", "주점에서 마실 때"),
        new("University", "대학", "0:30", "대학에 들어설 때"),
        new("Sail", "돛 조종", "14:15", "돛을 펴고 접을 때"),
        new("Open", "문 열기", "24:5", "건물 입구에 닿았을 때"),
        new("Cannon", "포격", "", "해전에서 포를 쏠 때 · 충각으로 들이받을 때(아직 맨 소리가 없다)"),
        new("Storm", "폭풍", "", "폭풍이 몰아칠 때(비우면 경고 소리)"),
    ];

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
