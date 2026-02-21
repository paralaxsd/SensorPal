using Microsoft.Extensions.Logging;
using SensorPal.Mobile.Logging;

#if ANDROID
using Android.App;
using Microsoft.Maui.ApplicationModel;
#endif

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

    void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LogEntry entry) return;

        // Reset immediately so the same entry can be tapped again.
        LogsView.SelectedItem = null;

        ShowCopyDialog(FormatEntry(entry));
    }

    void OnClearClicked(object? sender, EventArgs e) => _store.Clear();

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    // DisplayAlertAsync is broken in Android Release/AOT builds (TCS never
    // resolves). Use a native AlertDialog on Android; fall back to MAUI API
    // elsewhere.
    void ShowCopyDialog(string text)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity is null) return;

        var dialog = new AlertDialog.Builder(activity)
            .SetTitle("Log Entry")!
            .SetMessage(text)!
            .SetPositiveButton("Copy", (_, _) => _ = Clipboard.SetTextAsync(text))!
            .SetNegativeButton("Close", (_, _) => { })!
            .Create()!;
        dialog.Show();
        dialog.GetButton(-1) // -1 = positive button
            ?.SetTextColor(Android.Graphics.Color.Rgb(0x19, 0x76, 0xD2)); // Material Blue 700
#else
        _ = ShowCopyDialogAsync(text);
#endif
    }

#if !ANDROID
    async Task ShowCopyDialogAsync(string text)
    {
        var copy = await DisplayAlertAsync("Log Entry", text, "Copy", "Close");
        if (copy)
            await Clipboard.SetTextAsync(text);
    }
#endif

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
