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

    void OnClearClicked(object? sender, EventArgs e) => _store.Clear();
}
