using NAudio.CoreAudioApi;

namespace SensorPal.Server.Endpoints;

static class AudioDeviceEndpoints
{
    public static void MapAudioDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audio/devices", () =>
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => new AudioDeviceDto { Name = d.FriendlyName, IsDefault = d.ID == defaultDevice.ID })
                .ToList();
        });
    }
}
