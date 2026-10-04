using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Dho.Data;

namespace Dho.Tools;

/// <summary>고르기 상자의 한 줄 — 번호와 보이는 글.</summary>
public sealed record Choice(int Id, string Label);

/// <summary>
/// 개발도구 — 게임이 읽는 자료(<c>data\*.json</c>)를 고친다.
/// 의뢰를 짓고, 지도에서 상륙지 자리를 찍고, 도시·발견물·해역 표와 설정을 손본다.
/// </summary>
public partial class MainWindow : Window
{
    private GameData _data = null!;
    private WorldMap? _map;
    private ObservableCollection<QuestData> _quests = [];

    public MainWindow()
    {
        InitializeComponent();
        InputBindings.Add(new KeyBinding(new Relay(Save), Key.S, ModifierKeys.Control));
        Loaded += (_, _) =>
        {
            LoadData();
            RunArguments();
        };
    }

    // ── 읽기 · 쓰기 ──────────────────────────────────────────────────────────

    private void LoadData()
    {
        try
        {
            _data = GameData.Load();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"자료를 읽지 못했습니다.\n{error.Message}\n\n게임 폴더: {GvoFiles.Root}", "개발도구");
            _data = new GameData();
        }
        Bind();
        Status($"자료 폴더: {_data.Directory}");
    }

    private void Bind()
    {
        _quests = new ObservableCollection<QuestData>(_data.Quests);
        QuestList.ItemsSource = _quests;
        QuestList.DisplayMemberPath = nameof(QuestData.Title);

        var cities = _data.Cities.Select(c => new Choice(c.Id, $"{c.Id}  {c.Name}")).ToList();
        var landings = _data.Landings.Select(l => new Choice(l.Id, LandingLabel(l))).ToList();
        QuestCity.ItemsSource = cities;
        MapCity.ItemsSource = cities;
        QuestDiscovery.ItemsSource = _data.Discoveries.Select(d => new Choice(d.Id, $"{d.Id}  {d.Name}")).ToList();
        QuestLanding.ItemsSource = landings;
        MapLanding.ItemsSource = landings;

        LandingGrid.ItemsSource = _data.Landings;
        CityGrid.ItemsSource = _data.Cities;
        DiscoveryGrid.ItemsSource = _data.Discoveries;
        SeaGrid.ItemsSource = _data.Seas;
        BerthGrid.ItemsSource = _data.Settings.Berths;
        BuildSettingsForm();

        QuestList.SelectedIndex = _quests.Count > 0 ? 0 : -1;
        QuestForm.IsEnabled = _quests.Count > 0;
        if (MapImage.Source == null) BuildMap();
        DrawMarks();
    }

    private static string LandingLabel(LandingData l) => $"{l.Id}  {l.Name}" + (l.X != 0 ? $"  ({l.X}, {l.Y})" : "  (자리 없음)");

    private void Save()
    {
        // 글상자에 치던 값을 마저 넣는다
        if (Keyboard.FocusedElement is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        foreach (var grid in new[] { LandingGrid, CityGrid, DiscoveryGrid, SeaGrid, BerthGrid }) grid.CommitEdit(DataGridEditingUnit.Row, true);

        _data.Quests = _quests.ToList();
        _data.Save();
        DrawMarks();
        Status($"저장했습니다 — {DateTime.Now:HH:mm:ss}  ({_data.Directory})");
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "도시·해역·상륙지·발견물 표를 게임 클라이언트에서 다시 뽑습니다.\n이 표들을 고친 것은 사라집니다(의뢰·설정·찍어 둔 상륙지 자리는 남습니다).",
                "다시 뽑기", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try
        {
            _data.Quests = _quests.ToList();
            _data.ExtractFromClient();
            _data.Save();
            Bind();
            Status("게임 자료에서 다시 뽑았습니다.");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"뽑지 못했습니다.\n{error.Message}\n\n게임 폴더: {GvoFiles.Root}", "다시 뽑기");
        }
    }

    private void RunGame_Click(object sender, RoutedEventArgs e)
    {
        Save();
        string? exe = FindGame();
        if (exe == null)
        {
            MessageBox.Show(this, "게임 실행 파일(Dho.exe)을 찾지 못했습니다. 먼저 src\\Dho 를 빌드하세요.", "게임 실행");
            return;
        }
        var start = new ProcessStartInfo(exe) { WorkingDirectory = System.IO.Path.GetDirectoryName(exe)! };
        start.Environment["DHO_DATA"] = _data.Directory;
        Process.Start(start);
    }

    private static string? FindGame()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string beside = System.IO.Path.Combine(dir.FullName, "Dho.exe");
            if (File.Exists(beside)) return beside;
            string project = System.IO.Path.Combine(dir.FullName, "src", "Dho", "bin");
            if (Directory.Exists(project))
                return Directory.EnumerateFiles(project, "Dho.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
        }
        return null;
    }

    private void Status(string text) => StatusText.Text = text;

    // ── 의뢰 ─────────────────────────────────────────────────────────────────

    private QuestData? SelectedQuest => QuestList.SelectedItem as QuestData;

    private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        QuestForm.DataContext = SelectedQuest;
        QuestForm.IsEnabled = SelectedQuest != null;
        ShowDiscovery();
    }

    private void AddQuest_Click(object sender, RoutedEventArgs e)
    {
        var quest = new QuestData
        {
            Id = _quests.Count == 0 ? 1 : _quests.Max(q => q.Id) + 1,
            Title = "새 의뢰",
            CityId = _data.Settings.StartCity,
            LandingText = "해안에 보트를 대고 뭍에 올랐다.",
        };
        _quests.Add(quest);
        QuestList.SelectedItem = quest;
    }

    private void RemoveQuest_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedQuest is { } quest) _quests.Remove(quest);
    }

    private void QuestTitle_TextChanged(object sender, TextChangedEventArgs e) => QuestList.Items.Refresh();

    private void QuestDiscovery_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDiscovery();

    private void ShowDiscovery()
    {
        int id = QuestDiscovery.SelectedValue is int value ? value : SelectedQuest?.DiscoveryId ?? 0;
        var found = _data.Discoveries.Find(d => d.Id == id);
        DiscoveryInfo.Text = found == null ? "" :
            $"{_data.DiscoveryKinds.Find(k => k.Id == found.Kind)?.Name}  {new string('★', found.Stars)}  경험 {found.Exp}  명성 {found.Fame}\n{found.Description}";
    }

    private void PickLanding_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedQuest == null) return;
        MapLanding.SelectedValuePath = nameof(Choice.Id);
        MapLanding.SelectedValue = QuestLanding.SelectedValue;
        Tabs.SelectedIndex = 1;
        CenterOnSelectedLanding();
    }

    // ── 지도 ─────────────────────────────────────────────────────────────────

    private void BuildMap()
    {
        try
        {
            _map = new WorldMap();
        }
        catch (Exception)
        {
            return;        // 게임 폴더가 없으면 지도 없이 쓴다
        }

        const int width = WorldMap.CellsX, height = WorldMap.CellsY;
        var pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            pixels[y * width + x] = _map.IsLandCell(x, y) ? 0xFF4C8C4Cu : 0xFF16286Eu;
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        MapImage.Source = bitmap;
    }

    /// <summary>지도 그림 한 화소가 세계 좌표 4 다.</summary>
    private const double CellSize = WorldMap.CellSize;

    private void DrawMarks()
    {
        MapMarks.Children.Clear();
        void Mark(double x, double y, Brush fill, double size, string? tip)
        {
            var dot = new Ellipse { Width = size, Height = size, Fill = fill, Stroke = Brushes.Black, StrokeThickness = 0.3, ToolTip = tip };
            Canvas.SetLeft(dot, x / CellSize - size / 2);
            Canvas.SetTop(dot, y / CellSize - size / 2);
            MapMarks.Children.Add(dot);
        }
        foreach (var city in _data.Cities.Where(c => c.SeaX != 0 || c.SeaY != 0)) Mark(city.SeaX, city.SeaY, Brushes.OrangeRed, 3, city.Name);
        foreach (var landing in _data.Landings.Where(l => l.X != 0)) Mark(landing.X, landing.Y, Brushes.Gold, 4, landing.Name);
    }

    private void Zoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MapScale == null) return;
        MapScale.ScaleX = MapScale.ScaleY = e.NewValue;
    }

    private void Map_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(MapGrid);
        int x = (int)(p.X * CellSize), y = (int)(p.Y * CellSize);
        string ground = _map == null ? "" : _map.IsLand(x, y) ? "뭍" : "바다";
        MapInfo.Text = $"{x}, {y}  {ground}";
    }

    private void Map_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MapLanding.SelectedItem is not Choice choice || _data.Landings.Find(l => l.Id == choice.Id) is not { } landing)
        {
            Status("먼저 「찍을 상륙지」를 고르세요.");
            return;
        }
        var p = e.GetPosition(MapGrid);
        landing.X = (int)(p.X * CellSize);
        landing.Y = (int)(p.Y * CellSize);
        if (_map != null && _map.IsLand(landing.X, landing.Y)) Status($"※ {landing.Name}: 찍은 자리가 뭍입니다. 배가 닿는 바다 쪽을 찍으세요.");
        else Status($"{landing.Name} → ({landing.X}, {landing.Y})  저장을 눌러야 남습니다.");

        DrawMarks();
        LandingGrid.Items.Refresh();
        RefreshLandingChoices(landing.Id);
    }

    private void RefreshLandingChoices(int keep)
    {
        var landings = _data.Landings.Select(l => new Choice(l.Id, LandingLabel(l))).ToList();
        object? quest = QuestLanding.SelectedValue;
        QuestLanding.ItemsSource = landings;
        QuestLanding.SelectedValue = quest;
        MapLanding.ItemsSource = landings;
        MapLanding.SelectedItem = landings.Find(c => c.Id == keep);
    }

    private void MapLanding_SelectionChanged(object sender, SelectionChangedEventArgs e) => CenterOnSelectedLanding();

    private void CenterOnSelectedLanding()
    {
        if (MapLanding.SelectedItem is not Choice choice || _data.Landings.Find(l => l.Id == choice.Id) is not { } landing) return;
        if (landing.X != 0) CenterOn(landing.X, landing.Y);
        else if (_data.Cities.Find(c => c.Id == landing.City) is { } city) CenterOn(city.SeaX, city.SeaY);
    }

    private void GoToCity_Click(object sender, RoutedEventArgs e)
    {
        if (MapCity.SelectedItem is Choice choice && _data.Cities.Find(c => c.Id == choice.Id) is { } city) CenterOn(city.SeaX, city.SeaY);
    }

    private void CenterOn(double worldX, double worldY)
    {
        if (ZoomSlider.Value < 4) ZoomSlider.Value = 6;
        UpdateLayout();
        double zoom = ZoomSlider.Value;
        MapScroll.ScrollToHorizontalOffset(worldX / CellSize * zoom - MapScroll.ViewportWidth / 2);
        MapScroll.ScrollToVerticalOffset(worldY / CellSize * zoom - MapScroll.ViewportHeight / 2);
    }

    // ── 표 ───────────────────────────────────────────────────────────────────

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        string text = box.Text.Trim();
        var grid = (string)box.Tag switch { "Landings" => LandingGrid, "Cities" => CityGrid, _ => DiscoveryGrid };
        if (grid.ItemsSource == null) return;
        ICollectionView view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        view.Filter = text.Length == 0 ? null : item => item switch
        {
            LandingData l => l.Name.Contains(text) || l.Id.ToString() == text,
            CityData c => c.Name.Contains(text) || c.Id.ToString() == text,
            DiscoveryData d => d.Name.Contains(text) || d.Description.Contains(text) || d.Id.ToString() == text,
            _ => true,
        };
    }

    // ── 설정 ─────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> SettingNames = new()
    {
        [nameof(SettingsData.PlayerName)] = "제독 이름",
        [nameof(SettingsData.Money)] = "소지금",
        [nameof(SettingsData.StartCity)] = "시작 도시(도시 번호)",
        [nameof(SettingsData.ShipEntry)] = "배 모형(sh0000 항목 번호)",
        [nameof(SettingsData.StartSkyPhase)] = "시작 시각(0 자정 ~ 0.5 한낮)",
        [nameof(SettingsData.SecondsPerDay)] = "항해 하루(초)",
        [nameof(SettingsData.SecondsPerSkyCycle)] = "낮밤 한 바퀴(초)",
        [nameof(SettingsData.MaxKnots)] = "최고 속도(노트)",
        [nameof(SettingsData.UnitsPerKnotSecond)] = "1노트로 1초에 가는 거리",
        [nameof(SettingsData.TurnRate)] = "키 돌리는 빠르기(초당 라디안)",
        [nameof(SettingsData.PortRange)] = "입항할 수 있는 거리",
        [nameof(SettingsData.LandingRange)] = "상륙할 수 있는 거리",
    };

    private void BuildSettingsForm()
    {
        SettingsForm.Children.Clear();
        SettingsForm.RowDefinitions.Clear();
        SettingsForm.ColumnDefinitions.Clear();
        SettingsForm.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        SettingsForm.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        SettingsForm.DataContext = _data.Settings;

        int row = 0;
        foreach (var (property, label) in SettingNames)
        {
            SettingsForm.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, Style = (Style)FindResource("Label") };
            var box = new TextBox();
            box.SetBinding(TextBox.TextProperty, new Binding(property));
            Grid.SetRow(name, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            SettingsForm.Children.Add(name);
            SettingsForm.Children.Add(box);
            row++;
        }
    }

    // ── 확인용: --tab n --shot 경로 ──────────────────────────────────────────

    private void RunArguments()
    {
        var args = Environment.GetCommandLineArgs();
        string? shot = null;
        for (int i = 1; i + 1 < args.Length; i++)
        {
            if (args[i] == "--tab") Tabs.SelectedIndex = int.Parse(args[i + 1]);
            if (args[i] == "--landing")
            {
                MapLanding.SelectedItem = ((List<Choice>)MapLanding.ItemsSource).Find(c => c.Id == int.Parse(args[i + 1]));
            }
            if (args[i] == "--shot") shot = args[i + 1];
        }
        if (shot == null) return;

        Dispatcher.BeginInvoke(() =>
        {
            CenterOnSelectedLanding();
            UpdateLayout();
            Dispatcher.BeginInvoke(() =>
            {
                var content = (FrameworkElement)Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(shot)) encoder.Save(file);
                Close();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private sealed class Relay(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
