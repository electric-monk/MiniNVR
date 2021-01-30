
namespace TestConsole.Onvif
{
    public class DeviceLink : BaseService<Device.DeviceClient, Device.Device>
    {
        private Device.Capabilities capabilities;

        public DeviceLink(Configuration.Cameras.Camera camera) : base(camera, camera.Endpoint)
        {
        }

        public override void Start()
        {
            base.Start();
            capabilities = connection.GetCapabilities(new Device.CapabilityCategory[] { Device.CapabilityCategory.All });
        }

        public override void Stop()
        {
            base.Stop();
            capabilities = null;
        }

        public MediaLink GetMedia()
        {
            return new MediaLink(camera, capabilities.Media.XAddr);
        }
    }
}
