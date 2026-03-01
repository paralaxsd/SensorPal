using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Extensions;
using SensorPal.Mobile.Logging;

namespace SensorPal.Mobile.Pages;

public partial class LogsPage : ContentPage
{
    readonly InMemoryLogStore _store;

    public LogsPage(InMemoryLogStore store)
    {
        _store = store;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _store.Changed += OnStoreChanged;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _store.Changed -= OnStoreChanged;
    }

    void OnStoreChanged() => MainThread.BeginInvokeOnMainThread(Refresh);

    void Refresh()
    {
        var entries = _store.Entries;
        LogsView.ItemsSource = entries;
        if (entries.Count > 0)
            LogsView.ScrollTo(entries[^1], animate: false);
    }

    async void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LogEntry entry) return;

        // Reset immediately so the same entry can be tapped again.
        LogsView.SelectedItem = null;

        var text = FormatEntry(entry);
        if (await this.ConfirmAsync("Log Entry", text, "Copy", "Close"))
            await Clipboard.SetTextAsync(text);
    }

    async void OnCopyAllClicked(object? sender, EventArgs e)
    {
        var entries = _store.Entries;
        if (entries.Count == 0) return;

        var text = string.Join('\n', entries.Select(FormatEntry));
        await Clipboard.SetTextAsync(text);
    }

    void OnClearClicked(object? sender, EventArgs e) => _store.Clear();

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    static string FormatEntry(LogEntry entry)
    {
        var level = entry.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
        return $"{entry.Timestamp:HH:mm:ss} {level} [{entry.Category}] {entry.Message}";
    }
}
