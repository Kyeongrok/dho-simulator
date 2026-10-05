using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 캐릭터 만들기와 이어 하기.
/// 나라·직업의 이름은 클라이언트 표의 것이고, 계열별 시작 소지금·배·스킬은 <c>start.json</c> 의 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    private const double AutoSaveSeconds = 5;

    /// <summary>캐릭터가 있는가. 없으면 만들기 화면이 뜬다.</summary>
    public bool Created { get; private set; }
    public int NationId { get; private set; }
    public int JobId { get; private set; }
    public bool Male { get; private set; } = true;

    private bool _developer;
    private double _sinceSave;

    public string NationName => Data.Nations.Find(n => n.Id == NationId)?.Name ?? "";
    public string JobName => Data.Jobs.Find(j => j.Id == JobId)?.Name ?? "";

    private void Begin(bool developer)
    {
        _developer = developer;
        if (developer)
        {
            // 설정의 값으로 바로 시작
            PlayerName = Settings.PlayerName;
            Money = Settings.Money;
            StartShip(Settings.StartShip);
            StartSupplies();
            foreach (var start in Settings.StartSkills)
                Skills[start.SkillId] = new SkillState { Rank = Math.Clamp(start.Rank, 1, Settings.MaxSkillRank) };
            Enter();
        }
        else if (Data.LoadSave() is { } save) Restore(save);
        else StartShip(Settings.StartShip);      // 만들기 화면 뒤에 떠 있을 배
    }

    private void Enter()
    {
        Created = true;
        MoorAt(City);
        Say($"{City.Name} 항구에 정박해 있다.");
        if (QuestsHere().Any()) Say("모험가 조합에서 의뢰를 받을 수 있다.");
    }

    /// <summary>만들기 화면에서 고를 수 있는 나라.</summary>
    public List<NamedData> NationChoices() =>
        Data.Start.Nations.Select(n => Data.Nations.Find(x => x.Id == n.NationId)).OfType<NamedData>().ToList();

    /// <summary>나라의 시작 도시 — 설정에서 덮어쓰면 그 도시.</summary>
    public CityData StartCityOf(int nationId)
    {
        int cityId = Data.Start.CityOverride != 0
            ? Data.Start.CityOverride
            : Data.Start.Nations.Find(n => n.NationId == nationId)?.CityId ?? Settings.StartCity;
        return _cities.TryGetValue(cityId, out var city) ? city : City;
    }

    public void Create(string name, int nationId, StartLineData line, bool male)
    {
        PlayerName = name.Trim().Length > 0 ? name.Trim() : Settings.PlayerName;
        NationId = nationId;
        JobId = line.JobId;
        Male = male;
        Money = line.Money;
        City = StartCityOf(nationId);

        StartShip(line.ShipId);
        StartSupplies();
        Skills.Clear();
        foreach (string id in line.Skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(id, out int skill)) Skills[skill] = new SkillState();

        Enter();
        Say($"{NationName}의 {JobName} {PlayerName}, 항해를 시작한다.");
        Save();
    }

    // ── 이어 하기 ────────────────────────────────────────────────────────────

    private void AutoSave(double dt)
    {
        _sinceSave += dt;
        if (_sinceSave < AutoSaveSeconds) return;
        _sinceSave = 0;
        Save();
    }

    /// <summary>항구에 있을 때만 적는다 — 바다에서 끄면 마지막 항구로 돌아간다.</summary>
    public void Save()
    {
        if (_developer || !Created || Mode != Mode.Port) return;
        Data.WriteSave(new SaveData
        {
            Name = PlayerName, Male = Male, NationId = NationId, JobId = JobId, Money = Money,
            CityId = City.Id, ShipId = Ship.Id, Durability = Durability, Crew = Crew, Water = Water, Food = Food,
            Clock = Clock, SkyPhase = SkyPhase,
            AdventureExp = AdventureExp, AdventureFame = AdventureFame, TradeExp = TradeExp,
            Skills = Skills.ToDictionary(s => s.Key, s => new[] { s.Value.Rank, s.Value.Exp }),
            Supplies = new Dictionary<int, int>(Supplies),
            Cargo = Cargo.ToDictionary(c => c.Key, c => new[] { c.Value.Count, c.Value.Cost }),
            DoneQuests = _done.ToList(),
            Items = new Dictionary<int, int>(Items),
            Parts = Parts.Select(p => p.Id).ToList(),
            Court = [Title, Merit, Order?.Id ?? 0, OrderProgress],
            Aides = Aides.Select(a => new[] { a.Who.Id, a.Duty, a.Level, a.Exp }).ToList(),
            Dock = Dock.Select(d => new double[] { d.Ship.Id, d.Durability }.Concat(d.Parts.Select(p => (double)p.Id)).ToArray()).ToList(),
            QuestId = Quest?.Id ?? 0, QuestStage = (int)QuestStage,
        });
    }

    private void Restore(SaveData save)
    {
        PlayerName = save.Name;
        Male = save.Male;
        NationId = save.NationId;
        JobId = save.JobId;
        Money = save.Money;
        if (_cities.TryGetValue(save.CityId, out var city)) City = city;
        StartShip(save.ShipId);
        Durability = Math.Clamp(save.Durability, 1, Stats.Durability);
        Crew = Math.Clamp(save.Crew, 1, Stats.MaxCrew);
        Water = save.Water;
        Food = save.Food;
        Clock = save.Clock;
        SkyPhase = save.SkyPhase;
        AdventureExp = save.AdventureExp;
        AdventureFame = save.AdventureFame;
        TradeExp = save.TradeExp;
        foreach (var (id, state) in save.Skills) Skills[id] = new SkillState { Rank = (int)state[0], Exp = state[1] };
        foreach (var (id, count) in save.Supplies) Supplies[id] = count;
        foreach (var (id, item) in save.Cargo) Cargo[id] = new CargoItem { Count = (int)item[0], Cost = item[1] };
        foreach (int id in save.DoneQuests) _done.Add(id);
        foreach (var (id, count) in save.Items) Items[id] = count;
        foreach (var docked in save.Dock)
            if (Data.Ships.Find(s => s.Id == (int)docked[0]) is { } stored)
                Dock.Add(new DockedShip
                {
                    Ship = stored, Durability = docked[1],
                    Parts = docked.Skip(2).Select(id => Data.ShipParts.Find(p => p.Id == (int)id)).OfType<ShipPart>().ToList(),
                });
        foreach (var saved in save.Aides)
            if (saved.Length >= 4 && Data.Aides.Find(a => a.Id == (int)saved[0]) is { } who)
                Aides.Add(new Aide { Who = who, Duty = (int)saved[1], Level = (int)saved[2], Exp = saved[3] });
        if (save.Court.Length >= 4)
            (Title, Merit, Order, OrderProgress) = (save.Court[0], save.Court[1], Data.Orders.Orders.Find(o => o.Id == save.Court[2]), save.Court[3]);
        Parts = save.Parts.Select(id => Data.ShipParts.Find(p => p.Id == id)).OfType<ShipPart>().ToList();
        Quest = Data.Quests.Find(q => q.Id == save.QuestId);
        QuestStage = Quest == null ? QuestStage.None : (QuestStage)save.QuestStage;
        Enter();
    }
}
