using TestConsole.Configuration.Users;

namespace TestConsole.Configuration
{
    public class User
    {
        private static User activeInstance;
        public static User Instance { get { return activeInstance; } }

        private SessionManager manager;

        private User(WebServer server)
        {
            manager = new SessionManager("MiniNVR", server);
            Users.Configuration.Load(manager, "authentication.xml");
        }

        public static void Initialise(WebServer server)
        {
            if (activeInstance != null)
                return;
            activeInstance = new User(server);
        }

        public SessionManager Manager
        {
            get
            {
                return manager;
            }
        }
    }
}
