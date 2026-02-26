using Microsoft.UI.Xaml.Input;

namespace SensorPal.Mobile;

public partial class AppShell
{
    partial void HookMouseBack()
    {
        // Window.Handler is not yet set during OnAppearing — defer to next UI tick.
        Dispatcher.Dispatch(() =>
        {
            if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                nativeWindow.Content.PointerPressed += OnWindowPointerPressed;
        });
    }

    void OnWindowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsXButton1Pressed) return;
        if (Navigation.ModalStack.Count > 0)
            _ = Navigation.PopModalAsync();
    }
}
