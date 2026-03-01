using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Infrastructure;
using SensorPal.Shared.Models;
using SkiaSharp;

namespace SensorPal.Mobile.Pages;

public partial class StatsPage : ContentPage
{
    static readonly Color ActiveColor   = Color.FromArgb("#1976D2");
    static readonly Color SecondaryActive = Color.FromArgb("#546E7A");
    static readonly Color InactiveColor = Color.FromArgb("#E0E0E0");

    readonly SensorPalClient _client;
    readonly ILogger<StatsPage> _logger;

    StatsDto? _data;
    int _rangeDays = 30;
    int _viewIndex = 0;  // 0 = Nightly, 1 = Hourly, 2 = Peak dB

    public StatsPage(SensorPalClient client, ILogger<StatsPage> logger)
    {
        _client = client;
        _logger = logger;
        InitializeComponent();
        UpdateButtonStates();
    }

    protected override void OnAppearing() => _ = LoadAsync();

    async Task LoadAsync()
    {
        LoadingIndicator.IsRunning = LoadingIndicator.IsVisible = true;
        Chart.IsVisible = NoDataLabel.IsVisible = false;

        try
        {
            var to   = DateOnly.FromDateTime(DateTime.Today);
            var from = to.AddDays(-(_rangeDays - 1));
            _data = await _client.GetStatsAsync(from, to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load stats");
            _data = null;
        }
        finally
        {
            LoadingIndicator.IsRunning = LoadingIndicator.IsVisible = false;
        }

        UpdateSummary();
        UpdateChart();
    }

    // ── View switcher ──────────────────────────────────────────────────────

    void OnNightlyClicked(object? sender, EventArgs e) => SetView(0);
    void OnHourlyClicked(object? sender, EventArgs e)  => SetView(1);
    void OnPeakClicked(object? sender, EventArgs e)    => SetView(2);

    void SetView(int index)
    {
        _viewIndex = index;
        UpdateButtonStates();
        UpdateChart();
    }

    // ── Range picker ───────────────────────────────────────────────────────

    void On7dClicked(object? sender, EventArgs e)  => SetRange(7);
    void On30dClicked(object? sender, EventArgs e) => SetRange(30);
    void On90dClicked(object? sender, EventArgs e) => SetRange(90);

    void SetRange(int days)
    {
        _rangeDays = days;
        UpdateButtonStates();
        _ = LoadAsync();
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    void UpdateSummary()
    {
        if (_data is null)
        {
            SummaryLabel.Text = "";
            return;
        }

        var s = _data.Summary;
        SummaryLabel.Text = s.TotalEvents == 0
            ? "No events in this period"
            : $"{s.TotalEvents} events · {s.ActiveDays} nights · Ø {s.AvgPerNight}/night · peak {s.PeakDb:F1} dBFS";
    }

    void UpdateChart()
    {
        if (_data is null || _data.Summary.TotalEvents == 0)
        {
            Chart.IsVisible    = false;
            NoDataLabel.IsVisible = true;
            return;
        }

        NoDataLabel.IsVisible = false;
        Chart.IsVisible       = true;

        switch (_viewIndex)
        {
            case 0:
                RenderNightly();
                break;
            case 1:
                RenderHourly();
                break;
            case 2:
                RenderPeak();
                break;
        }
    }

    void RenderNightly()
    {
        var nights = _data!.Nightly;
        Chart.Series = [new ColumnSeries<int>
        {
            Values           = nights.Select(n => n.EventCount).ToArray(),
            Fill             = new SolidColorPaint(new SKColor(0x19, 0x76, 0xD2)),
            Stroke           = null,
            MaxBarWidth      = 48,
        }];
        Chart.XAxes = [new Axis
        {
            Labels         = nights.Select(n => n.Date.ToString("dd.MM")).ToArray(),
            LabelsRotation = -45,
            TextSize       = 10,
        }];
        Chart.YAxes = [new Axis { MinLimit = 0 }];
    }

    void RenderHourly()
    {
        var hours = _data!.Hourly;
        Chart.Series = [new ColumnSeries<int>
        {
            Values      = hours.Select(h => h.EventCount).ToArray(),
            Fill        = new SolidColorPaint(new SKColor(0x00, 0x89, 0x7B)),
            Stroke      = null,
            MaxBarWidth = 36,
        }];
        Chart.XAxes = [new Axis
        {
            Labels         = hours.Select(h => $"{h.Hour:00}h").ToArray(),
            LabelsRotation = -45,
            TextSize       = 9,
        }];
        Chart.YAxes = [new Axis { MinLimit = 0 }];
    }

    void RenderPeak()
    {
        var nights = _data!.Nightly;
        Chart.Series = [new ColumnSeries<double>
        {
            Values      = nights.Select(n => n.PeakDb).ToArray(),
            Fill        = new SolidColorPaint(new SKColor(0xFB, 0x8C, 0x00)),
            Stroke      = null,
            MaxBarWidth = 48,
        }];
        Chart.XAxes = [new Axis
        {
            Labels         = nights.Select(n => n.Date.ToString("dd.MM")).ToArray(),
            LabelsRotation = -45,
            TextSize       = 10,
        }];
        // Auto Y axis — dBFS values are negative; LiveCharts renders correctly
        Chart.YAxes = [new Axis()];
    }

    void UpdateButtonStates()
    {
        SetButtonState(BtnNightly, _viewIndex == 0, ActiveColor);
        SetButtonState(BtnHourly,  _viewIndex == 1, ActiveColor);
        SetButtonState(BtnPeak,    _viewIndex == 2, ActiveColor);

        SetButtonState(Btn7d,  _rangeDays == 7,  SecondaryActive);
        SetButtonState(Btn30d, _rangeDays == 30, SecondaryActive);
        SetButtonState(Btn90d, _rangeDays == 90, SecondaryActive);
    }

    static void SetButtonState(Button btn, bool active, Color activeColor)
    {
        btn.BackgroundColor = active ? activeColor : InactiveColor;
        btn.TextColor       = active ? Colors.White : Colors.Black;
    }
}
