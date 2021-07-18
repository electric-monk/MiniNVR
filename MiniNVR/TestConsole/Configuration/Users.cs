using System;
using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    public class Users
    {
        public static class Identifier
        {
            public static readonly string AdminGroup = "$Admins";
            public static readonly string CameraGroup = "$Cameras";
        }

        public class Allowance
        {
            public string[] Members { get; set;  }    // GUIDs of groups or Usernames
        }

        public class Group : Allowance
        {
            [XmlAttribute]
            public Guid Identifier;

            [XmlAttribute]
            public string Name;

            [XmlIgnore]
            public virtual bool Editable { get { return true; } }
        }

        public class User : IEquatable<User>
        {
            [XmlAttribute]
            public string Username;
            [XmlAttribute]
            public string Password;

            public bool Equals(User other)
            {
                return Username == other.Username;
            }
        }

        [XmlArray("Users")]
        public User[] AllUsers;

        [XmlArray("Groups")]
        public Group[] AllGroups { get; set; }

        public bool CanAccess(Allowance asset, User user)
        {
            foreach (string id in asset.Members) {
                Group group = FindGroup(id);
                if (group != null) {
                    if (CanAccess(group, user))
                        return true;
                } else {
                    User foundUser = FindUser(id);
                    if ((foundUser != null) && foundUser.Equals(user))
                        return true;
                }
            }
            return false;
        }
        private Group FindGroup(string id)
        {
            foreach (Group g in AllGroups)
                if (g.Identifier.ToString() == id)
                    return g;
            return null;
        }
        private User FindUser(string id)
        {
            foreach (User u in AllUsers)
                if (u.Username == id)
                    return u;
            return null;
        }
    }
}
