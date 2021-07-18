using System;
using System.ServiceModel;

namespace TestConsole.Onvif
{
    public class BaseService<GClient, GClass> where GClient : ClientBase<GClass>, new() where GClass : class
    {
        protected readonly Configuration.Cameras.Camera camera;
        protected readonly string endpoint;

        protected GClient connection;

        public Configuration.Cameras.Camera LinkedCamera { get { return camera; } }

        protected BaseService(Configuration.Cameras.Camera camera, string endpoint)
        {
            this.camera = camera;
            this.endpoint = endpoint;
        }

        public virtual void Start()
        {
            Stop();
            var args = new object[] { new WSHttpBinding(SecurityMode.None), new EndpointAddress(endpoint) };
            connection = (GClient)Activator.CreateInstance(typeof(GClient), args);
            if (camera.Credentials != null)
                connection.Endpoint.EndpointBehaviors.Add(new Soap.UsernameTokenEndpointBehaviour(camera.Credentials.Username, camera.Credentials.Password));
        }

        public virtual void Stop()
        {
            if (connection != null) {
                connection.Close();
                connection = null;
            }
        }
    }
}
