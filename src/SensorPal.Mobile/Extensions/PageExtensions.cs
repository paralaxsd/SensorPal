#if ANDROID
using Android.App;
using Microsoft.Maui.ApplicationModel;
#endif

namespace SensorPal.Mobile.Extensions;

/// <summary>
/// Extension methods on <see cref="Page"/> for native confirmation dialogs.
/// </summary>
/// <remarks>
/// <see cref="Page.DisplayAlertAsync(string,string,string)"/> is broken in Android Release/AOT builds
/// (the returned <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/> never resolves).
/// This helper centralises the native <c>AlertDialog</c> workaround so that call sites
/// remain platform-agnostic.
/// </remarks>
static class PageExtensions
{
    extension(Page page)
    {
        /// <summary>
        /// Shows a two-button (accept / cancel) dialog and returns <see langword="true"/>
        /// when the user taps the accept button.
        /// </summary>
        public Task<bool> ConfirmAsync(
            string title, string message, string accept = "OK", string cancel = "Cancel")
        {
#if ANDROID
            var tcs = new TaskCompletionSource<bool>();
            var activity = Platform.CurrentActivity;
            if (activity is null) return Task.FromResult(false);

            activity.RunOnUiThread(() =>
            {
                var dialog = new AlertDialog.Builder(activity)
                    .SetTitle(title)!
                    .SetMessage(message)!
                    .SetPositiveButton(accept, (_, _) => tcs.TrySetResult(true))!
                    .SetNegativeButton(cancel, (_, _) => tcs.TrySetResult(false))!
                    .Create()!;
                dialog.Show();

                var buttonColor = Android.Graphics.Color.Rgb(25, 118, 210);
                dialog.GetButton(-1)?.SetTextColor(buttonColor);
                dialog.GetButton(-2)?.SetTextColor(buttonColor);
            });

            return tcs.Task;
#else
            return page.DisplayAlertAsync(title, message, accept, cancel);
#endif
        }
    }
}
