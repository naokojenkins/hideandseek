using TorchSharp;

namespace HideAndSeek.Core.RL
{
    public enum DevicePreference
    {
        Auto,
        Cpu,
        Cuda
    }

    public class DeviceSettings
    {
        public DevicePreference Preference { get; set; } = DevicePreference.Auto;
    }

    public interface IDeviceProvider
    {
        torch.Device GetDevice();
    }
}
