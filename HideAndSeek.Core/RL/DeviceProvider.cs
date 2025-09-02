using TorchSharp;
using static TorchSharp.torch;

namespace HideAndSeek.Core.RL
{
    public class DeviceProvider : IDeviceProvider
    {
        private readonly DeviceSettings _settings;
        private torch.Device? _cached;

        public DeviceProvider(DeviceSettings settings)
        {
            _settings = settings ?? new DeviceSettings();
        }

        public torch.Device GetDevice()
        {
            if (_cached != null) return _cached;

            switch (_settings.Preference)
            {
                case DevicePreference.Cpu:
                    _cached = torch.CPU;
                    break;
                case DevicePreference.Cuda:
                    _cached = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
                    break;
                case DevicePreference.Auto:
                default:
                    _cached = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
                    break;
            }
            return _cached;
        }
    }
}
