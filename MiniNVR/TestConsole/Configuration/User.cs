using System;
using System.Linq;
using TestConsole.Configuration.Users;

namespace TestConsole.Configuration
{
    public class User
    {
        public interface IAccessibleDevice
        {
            string[] Groups { get; }
        }

        private static User activeInstance;
        public static User Instance { get { return activeInstance; } }

        private SessionManager manager;

        private User(WebServer server)
        {
            manager = new SessionManager("MiniNVR", server);
            Users.Configuration.Load(manager, Database.GetSettingsPath("authentication.xml"));
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

        public bool IsAdmin(ISession session)
        {
            if (session == null)
                return false;
            return Array.Exists(session.Groups, g => g.Equals(manager.AdminGroup, StringComparison.InvariantCultureIgnoreCase));
        }

        public bool HasAccess(ISession session, IAccessibleDevice device)
        {
            var groups = device.Groups;
            if (groups == null)
                return true;
            var user = session.Groups;
            if (user == null)
                return false;
            return groups.Intersect(user).Any();
        }
    }
}
