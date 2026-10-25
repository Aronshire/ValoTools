using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ValoTools.Services;

namespace ValoTools;

public partial class Dashboard : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    public static readonly DependencyProperty AnimatedScrollOffsetProperty = DependencyProperty.Register(
        nameof(AnimatedScrollOffset), typeof(double), typeof(Dashboard),
        new PropertyMetadata(0d, (owner, args) =>
        {
            var dashboard = (Dashboard)owner;
            dashboard.AgentScroll?.ScrollToVerticalOffset((double)args.NewValue);
        }));

    public double AnimatedScrollOffset
    {
        get => (double)GetValue(AnimatedScrollOffsetProperty);
        set => SetValue(AnimatedScrollOffsetProperty, value);
    }

    private readonly AgentCatalog _catalog = new();
    private readonly RiotClient _riot = new();
    private readonly ObservableCollection<Agent> _visibleAgents = [];
    private List<Agent> _allAgents = [];
    private Agent? _selectedAgent;
    private CancellationTokenSource? _monitorCancellation;
    private string? _lastMessage;
    private double _scrollTarget;
    private bool _activityActive;
    private string _selectedRole = "";
    private readonly CancellationTokenSource _presenceCancellation = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly UpdateService _updates = new();
    private AvailableUpdate? _availableUpdate;
    private string? _installerPath;
    private bool _updateCheckRunning;

    public Dashboard()
    {
        InitializeComponent();
        AgentItems.ItemsSource = _visibleAgents;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateEntrance();
        VersionText.Text = $"v{UpdateService.CurrentVersion.ToString(3)}";
        if (UpdateService.IsInstalled()) _ = WatchForUpdatesAsync();
        _ = WatchValorantPresenceAsync(_presenceCancellation.Token);
        await LoadAgentsAsync();
    }

    private async Task WatchForUpdatesAsync()
    {
        while (!_updateCancellation.IsCancellationRequested && _installerPath is null)
        {
            await CheckForUpdatesAsync();
            if (_installerPath is not null) break;
            try { await Task.Delay(TimeSpan.FromMinutes(30), _updateCancellation.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCheckRunning) return;
        _updateCheckRunning = true;
        UpdateText.Text = "Yeni sürüm denetleniyor...";
        UpdateButton.Content = "KONTROL EDİLİYOR...";
        UpdateButton.IsEnabled = false;
        try
        {
            _availableUpdate = await _updates.CheckAsync(_updateCancellation.Token);
            if (_availableUpdate is null)
            {
                UpdateText.Text = "En güncel sürümü kullanıyorsun.";
                UpdateButton.Content = "GÜNCELLEMEYİ KONTROL ET";
                UpdateButton.IsEnabled = true;
                return;
            }

            var version = _availableUpdate.Version.ToString(3);
            UpdateText.Text = $"v{version} indiriliyor • %0";
            UpdateButton.Content = "İNDİRİLİYOR...";
            var progress = new Progress<int>(percent =>
            {
                if (!_updateCancellation.IsCancellationRequested)
                    UpdateText.Text = $"v{version} indiriliyor • %{percent}";
            });
            _installerPath = await _updates.DownloadAsync(_availableUpdate, progress, _updateCancellation.Token);
            UpdateText.Text = $"v{version} indirildi. Kuruluma hazır.";
            UpdateButton.Content = "GÜNCELLEMEYİ KUR  ↗";
            UpdateButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_updateCancellation.IsCancellationRequested) return;
            UpdateText.Text = "Güncelleme denetlenemedi. Tekrar deneyebilirsin.";
            UpdateButton.Content = "TEKRAR DENE  ↻";
            UpdateButton.IsEnabled = true;
        }
        finally { _updateCheckRunning = false; }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null || _installerPath is null)
        {
            await CheckForUpdatesAsync();
            return;
        }

        if (_monitorCancellation is not null && MessageBox.Show(this,
                "Güncelleme kurulurken ajan izleme duracak. Devam edilsin mi?", "ValoTools güncellemesi",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            UpdateButton.IsEnabled = false;
            UpdateText.Text = "Kurulum dosyası doğrulanıyor...";
            var verifiedPath = await _updates.DownloadAsync(_availableUpdate, null, _updateCancellation.Token);
            UpdateService.StartInstaller(verifiedPath);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"Kurulum başlatılamadı: {ex.Message}";
            UpdateButton.IsEnabled = true;
        }
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAgentsAsync();

    private async Task LoadAgentsAsync()
    {
        try
        {
            _allAgents = await _catalog.GetAgentsAsync();
            AgentCount.Text = $"{_allAgents.Count} AJAN";
            RefreshAgents();
            var saved = _selectedAgent?.Id ?? SettingsStore.LoadAgentId();
            SelectAgent(_allAgents.FirstOrDefault(a => a.Id == saved));
            StartButton.IsEnabled = _selectedAgent is not null;
            DetailText.Text = "Ajanını seç ve izlemeyi başlat. Bölge otomatik algılanır.";
            SetStatus("HAZIR", "#36B7C1");
        }
        catch (Exception ex)
        {
            DetailText.Text = $"Ajan listesi yüklenemedi: {ex.Message}";
            SetStatus("BAĞLANTI HATASI", "#E46B75");
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshAgents();
    }

    private void RoleFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton role)
            _selectedRole = role.Tag as string ?? "";
        RefreshAgents();
    }

    private void RefreshAgents()
    {
        if (SearchBox is null) return;
        var query = SearchBox.Text.Trim();
        _visibleAgents.Clear();
        foreach (var agent in _allAgents.Where(a =>
                     (string.IsNullOrEmpty(_selectedRole) || string.Equals(a.Role, _selectedRole, StringComparison.OrdinalIgnoreCase))
                     && a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            _visibleAgents.Add(agent);
        AgentCount.Text = $"{_visibleAgents.Count} / {_allAgents.Count} AJAN";
        if (EmptyAgentsText is not null)
            EmptyAgentsText.Visibility = _allAgents.Count > 0 && _visibleAgents.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        if (AgentScroll is not null)
        {
            BeginAnimation(AnimatedScrollOffsetProperty, null);
            AnimatedScrollOffset = 0;
            _scrollTarget = 0;
        }
    }

    private void Agent_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorCancellation is null && sender is Button { Tag: Agent agent }) SelectAgent(agent);
    }

    private void SelectAgent(Agent? agent)
    {
        if (agent is null) return;
        foreach (var item in _allAgents) item.IsSelected = item == agent;
        _selectedAgent = agent;
        SelectedName.Text = agent.Name.ToUpperInvariant();
        SelectedRole.Text = agent.Role.ToUpperInvariant();
        SelectedRoleIcon.Source = Uri.TryCreate(agent.RoleIconUrl, UriKind.Absolute, out var roleUri)
            ? new BitmapImage(roleUri) : null;
        PreviousPortrait.Source = SelectedPortrait.Source;
        PreviousPortrait.Opacity = PreviousPortrait.Source is null ? 0 : 1;
        SelectedPortrait.Source = Uri.TryCreate(agent.PortraitUrl, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri) : null;
        AnimateSelection();
        SettingsStore.SaveAgentId(agent.Id);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorCancellation is not null)
        {
            _monitorCancellation.Cancel();
            _monitorCancellation = null;
            AgentItems.IsEnabled = true;
            StartButton.Content = "İZLEMEYİ BAŞLAT  →";
            SetActivity(false);
            SetStatus("DURDURULDU", "#D9B979");
            DetailText.Text = "İzleme durduruldu.";
            return;
        }
        if (_selectedAgent is null) return;
        var cancellation = new CancellationTokenSource();
        _monitorCancellation = cancellation;
        AgentItems.IsEnabled = false;
        StartButton.Content = "İZLEMEYİ DURDUR  ■";
        SetActivity(true);
        SetStatus("İZLENİYOR", "#36B7C1");
        var monitor = new MatchMonitor(_riot, _selectedAgent, Report);
        try { await monitor.RunAsync(cancellation.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            if (_monitorCancellation == cancellation)
            {
                _monitorCancellation = null;
                AgentItems.IsEnabled = true;
                StartButton.Content = "İZLEMEYİ BAŞLAT  →";
                SetActivity(false);
                SetStatus("DURDURULDU", "#D9B979");
            }
            cancellation.Dispose();
        }
    }

    private void Report(MonitorUpdate update)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.Invoke(() =>
        {
            if (update.Message != _lastMessage)
            {
                DetailText.Text = update.Message;
                _lastMessage = update.Message;
            }
            RegionText.Text = update.Region is null
                ? update.IsError ? "BÖLGE  •  BAĞLANTI HATASI" : "BÖLGE  •  VALORANT BEKLENİYOR"
                : $"BÖLGE  •  {update.Region}";
            SetStatus(update.Status, update.IsError ? "#E46B75" : "#36B7C1");
        });
    }

    private async Task WatchValorantPresenceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var presence = await _riot.GetValorantPresenceAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                    ShowValorantPresence(presence);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                    ShowValorantPresence(null);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ShowValorantPresence(ValorantPresence? presence)
    {
        SidebarStatusText.Text = presence is null ? "OYUN BEKLENİYOR" : "VALORANT ALGILANDI";
        SidebarStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            presence is null ? "#A8ADB3" : "#83D8DE"));
        ConnectionDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            presence is null ? "#777A82" : "#36B7C1"));
        PlayerIdentityText.Text = presence?.DisplayName ??
            (presence is null ? "VALORANT oturumu aranıyor" : "Oyuncu adı yükleniyor");
        PlayerIdentityText.ToolTip = presence?.DisplayName;
        PlayerIdentityText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            presence?.DisplayName is null ? "#6E737B" : "#F4F5F6"));
    }

    private void SetStatus(string text, string hex)
    {
        if (StatusText.Text != text)
        {
            StatusText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(240))
            { EasingFunction = EaseOut });
        }
        StatusText.Text = text;
        StatusDot.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        StatusHalo.Stroke = StatusDot.Fill;
    }

    private void AnimateEntrance()
    {
        MainStage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
        { EasingFunction = EaseOut });
        ((TranslateTransform)MainStage.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = EaseOut });
    }

    private void AnimateSelection()
    {
        PreviousPortrait.BeginAnimation(OpacityProperty,
            new DoubleAnimation(PreviousPortrait.Opacity, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });
        SelectedPortrait.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(440)) { EasingFunction = EaseOut });
        ((TranslateTransform)SelectedPortrait.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(500)) { EasingFunction = EaseOut });
        SelectedName.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = EaseOut });
        ((TranslateTransform)SelectedName.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = EaseOut });
    }

    private void SetActivity(bool active)
    {
        if (_activityActive == active) return;
        _activityActive = active;
        var lineTransform = (TranslateTransform)ActivityLine.RenderTransform;
        var haloTransform = (ScaleTransform)StatusHalo.RenderTransform;
        if (active)
        {
            ActivityLine.Opacity = 0.9;
            lineTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-240, Math.Max(ActualWidth, 1000), TimeSpan.FromSeconds(2.1))
                { RepeatBehavior = RepeatBehavior.Forever });
            StatusHalo.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.65, 0, TimeSpan.FromSeconds(1.4))
                { RepeatBehavior = RepeatBehavior.Forever });
            haloTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.7, 1.75, TimeSpan.FromSeconds(1.4))
                { RepeatBehavior = RepeatBehavior.Forever });
            haloTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.7, 1.75, TimeSpan.FromSeconds(1.4))
                { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            lineTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ActivityLine.Opacity = 0;
            StatusHalo.BeginAnimation(OpacityProperty, null);
            StatusHalo.Opacity = 0;
            haloTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            haloTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    private void AgentScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (AgentScroll.ScrollableHeight <= 0) return;
        if (Math.Abs(_scrollTarget - AgentScroll.VerticalOffset) > 250)
            _scrollTarget = AgentScroll.VerticalOffset;
        _scrollTarget = Math.Clamp(_scrollTarget - e.Delta * 0.8, 0, AgentScroll.ScrollableHeight);
        BeginAnimation(AnimatedScrollOffsetProperty,
            new DoubleAnimation(AgentScroll.VerticalOffset, _scrollTarget, TimeSpan.FromMilliseconds(300))
            { EasingFunction = EaseOut });
        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _monitorCancellation?.Cancel();
        _presenceCancellation.Cancel();
        _updateCancellation.Cancel();
    }
}
