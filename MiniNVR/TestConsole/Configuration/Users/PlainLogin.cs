using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using System.Linq;
using System.Collections.Generic;

namespace TestConsole.Configuration.Users
{
    public class PlainLogin : IProvider
    {
        private static readonly HashAlgorithm HASH = SHA256.Create();
        private string DoHash(string password)
        {
            return string.Concat(HASH.ComputeHash(Encoding.UTF8.GetBytes(_salt + password)).Select(b => b.ToString("x2")));
        }

        private class Session : ISession
        {
            private readonly PlainLogin _owner;
            private readonly string _identifier;
            private User _user;

            public Session(PlainLogin owner, User user)
            {
                _owner = owner;
                _identifier = Guid.NewGuid().ToString();
                _user = user;
            }

            public string Identifier
            {
                get
                {
                    return _identifier;
                }
            }

            public string Username
            {
                get
                {
                    return _user.Username;
                }
            }

            public string[] Groups
            {
                get
                {
                    return _user.Groups;
                }
            }

            public bool IsValid
            {
                get
                {
                    _user = _owner.Find(_user.Username);
                    return _user != null;
                }
            }

            public IProvider AuthenticationProvider
            {
                get
                {
                    return _owner;
                }
            }
        }

        private class Complete : WebServer.IEndpoint
        {
            private readonly PlainLogin _owner;

            public Complete(PlainLogin owner)
            {
                _owner = owner;
            }

            public void Handle(HttpListenerContext request)
            {
                var response = request.Response;
                var data = WebServer.GetForm(request.Request.InputStream);
                bool good = false;
                if (data.TryGetValue(KEY_USERNAME, out string username) && data.TryGetValue(KEY_PASSWORD, out string password)) {
                    var user = _owner.Find(username);
                    if ((user != null) && user.PasswordHash.Equals(_owner.DoHash(password), StringComparison.InvariantCultureIgnoreCase)) {
                        _owner._manager.RegisterSession(response, new Session(_owner, user));
                        response.Redirect(_owner._manager.HomeLink);
                        good = true;
                    }
                }
                if (!good) {
                    var s = $"<html><head><title>Error</title></head><body>Invalid credentials!<br/><a href=\"{_owner._manager.HomeLink}\">Return to application</a></body></html>";
                    response.ContentEncoding = Encoding.UTF8;
                    var b = response.ContentEncoding.GetBytes(s);
                    response.ContentLength64 = b.Length;
                    response.ContentType = "text/html";
                    response.OutputStream.Write(b, 0, b.Length);
                }
                response.Close();
            }
        }

        private class Manager : WebServer.IEndpoint
        {
            private static readonly string TEXT_PASSWORD_PREFIX = "password_";
            private static readonly string TEXT_GROUPS_PREFIX = "groups_";
            private static readonly string CHECK_DELETE_PREFIX = "delete_";
            private static readonly string TEXT_NEW_USERNAME = "new_username";
            private static readonly string TEXT_NEW_PASSWORD = "new_password";
            private static readonly string TEXT_NEW_GROUPS = "new_groups";
            private static readonly string BUTTON_UPDATE = "update";
            private static readonly string TEXT_PASSWORD = "new_password";
            private static readonly string TEXT_PASSWORD_AGAIN = "confirm_password";
            private static readonly string BUTTON_CHANGE_PASSWORD = "change";
            private static readonly string BUTTON_LOGOUT = "logout";

            private PlainLogin _owner;

            public Manager(PlainLogin owner)
            {
                _owner = owner;
            }

            private static string[] ParseArray(string data)
            {
                return data.Split(',').Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)).ToArray();
            }

            private void DoUser(User user, string username, string password, string groups, bool delete)
            {
                if (delete) {
                    if (user != null) {
                        _owner._storage.AllUsers.Remove(user);
                        _owner.Save();
                    }
                    return;
                }
                if (user != null) {
                    if (username != null)
                        throw new ArgumentException("Username can't be specified for update");
                    if (password.Length != 0) {
                        user.PasswordHash = _owner.DoHash(password);
                        _owner.Save();
                    }
                    var newGroups = ParseArray(groups);
                    if (!Enumerable.SequenceEqual(newGroups, user.Groups)) {
                        user.Groups = newGroups;
                        _owner.Save();
                    }
                } else {
                    if (username == null)
                        throw new ArgumentException("Username required for add");
                    if ((username.Length == 0) || (password.Length == 0))
                        return;
                    _owner._storage.AllUsers.Add(new User(){
                        Username = username,
                        PasswordHash = _owner.DoHash(password),
                        Groups = ParseArray(groups),
                    });
                    _owner.Save();
                }
            }

            public void Handle(HttpListenerContext request)
            {
                var response = request.Response;
                var session = _owner._manager.GetSession(request.Request);
                string s = "<html><head><title>User Management</title></head><body>";
                if (session != null) {
                    var editUsersAllowed = session.Groups.Contains(_owner._manager.AdminGroup);
                    var data = WebServer.GetForm(request.Request.InputStream);
                    if (data.ContainsKey(BUTTON_LOGOUT)) {
                        _owner._manager.ClearCookie(response, session);
                        s += $"Logged out!<br/><br/><a href=\"{_owner._manager.HomeLink}\">Return home</a>";
                    } else if (data.ContainsKey(BUTTON_CHANGE_PASSWORD) && data[TEXT_PASSWORD].Equals(data[TEXT_PASSWORD_AGAIN], StringComparison.InvariantCultureIgnoreCase)) {
                        _owner.Changing();
                        _owner.Find(session.Username).PasswordHash = _owner.DoHash(data[TEXT_PASSWORD]);
                        _owner.Save();
                        _owner.Done();
                        s += $"Password updated!<br/><br/><a href=\"{_owner._manager.HomeLink}\">Return home</a>";
                    } else {
                        if (editUsersAllowed && data.ContainsKey(BUTTON_UPDATE)) {
                            _owner.Changing();
                            foreach (var user in _owner._storage.AllUsers.ToArray())
                                DoUser(user, null, data[TEXT_PASSWORD_PREFIX + user.Username], data[TEXT_GROUPS_PREFIX + user.Username], data.ContainsKey(CHECK_DELETE_PREFIX + user.Username));
                            DoUser(null, data[TEXT_NEW_USERNAME], data[TEXT_NEW_PASSWORD], data[TEXT_NEW_GROUPS], false);
                            _owner.Done();
                        }
                        s += "<h1>User Management</h1>";
                        s += $"<a href=\"{_owner._manager.HomeLink}\">Return to application</a><br/>";
                        s += $"<form action=\"{MANAGEMENT_URL}\" method=\"POST\"><input type=\"submit\" name=\"{BUTTON_LOGOUT}\" value=\"Log out\"></form>";
                        s += $"<h2>Change password</h2><form action=\"{MANAGEMENT_URL}\" method=\"POST\">";
                        s += $"<label for=\"pass1\">New password</label>: <input id=\"pass1\" type=\"password\" name=\"{TEXT_PASSWORD}\"><br/>";
                        s += $"<label for=\"pass2\">Verify password</label>: <input id=\"pass2\" type=\"password\" name=\"{TEXT_PASSWORD_AGAIN}\"><br/>";
                        s += $"<input type=\"submit\" name=\"{BUTTON_CHANGE_PASSWORD}\" value=\"Update\"><br/></form>";
                        if (editUsersAllowed) {
                            s += "<h2>Manage user accounts</h2>";
                            s += $"<form action=\"{MANAGEMENT_URL}\" method=\"POST\"><table>";
                            s += "<tr><td>Username</td><td>Password</td><td>Groups</td><td>Delete</td></tr>";
                            foreach (var user in _owner._storage.AllUsers) {
                                s += $"<tr><td>{user.Username}</td><td>";
                                if (user.Username.Equals(session.Username, StringComparison.InvariantCultureIgnoreCase))
                                    s += $"see above<input name=\"{TEXT_PASSWORD_PREFIX}{user.Username}\" type=\"hidden\" value=\"\">";
                                else
                                    s += $"<input name=\"{TEXT_PASSWORD_PREFIX}{user.Username}\">";
                                s += $"</td><td><input type=\"text\" name=\"{TEXT_GROUPS_PREFIX}{user.Username}\" value=\"{string.Join(", ", user.Groups)}\"></td>";
                                s += $"<td><input type=\"checkbox\" name=\"{CHECK_DELETE_PREFIX}{user.Username}\"></td>";
                            }
                            s += $"<tr><td><input name=\"{TEXT_NEW_USERNAME}\" type=\"text\"></td><td><input name=\"{TEXT_NEW_PASSWORD}\" type=\"text\"></td><td><input type=\"text\" name=\"{TEXT_NEW_GROUPS}\"></td><td/></tr>";
                            s += $"<tr><td/><td/><td><input type=\"submit\" name=\"{BUTTON_UPDATE}\" value=\"Update\"></td><td/></tr>";
                            s += "</table></form>";
                        }
                    }
                } else {
                    request.Response.StatusCode = 500;
                    s = "<h1>500 Internal Server Error</h1>No session";
                }
                s += "</body></html>";
                response.ContentEncoding = Encoding.UTF8;
                var b = response.ContentEncoding.GetBytes(s);
                response.ContentLength64 = b.Length;
                response.ContentType = "text/html";
                response.OutputStream.Write(b, 0, b.Length);
                response.Close();
            }
        }

        public class User
        {
            [XmlAttribute]
            public string Username;
            [XmlAttribute]
            public string PasswordHash;
            public string[] Groups;
        }

        [XmlRoot]
        public class Storage
        {
            public List<User> AllUsers;
        }

        private static readonly string DEFAULT_USERNAME = "admin";
        private static readonly string DEFAULT_PASSWORD = "password";

        private static readonly string LOGIN_URL = "/plain_login";
        private static readonly string MANAGEMENT_URL = "/plain_change";
        private static readonly string KEY_USERNAME = "username";
        private static readonly string KEY_PASSWORD = "password";

        private readonly SessionManager _manager;
        private readonly string _filename;
        private readonly string _salt;
        private Storage _storage;
        private int _changing, _dirty;

        public class PLConfig
        {
            public string UserFilename;
            public string Salt;
        }

        public PlainLogin(SessionManager sessionManager, PLConfig config)
        {
            _filename = config.UserFilename;
            _salt = config.Salt;
            _manager = sessionManager;
            _manager.Server.AddContent(LOGIN_URL, new Complete(this));
            _manager.Server.AddContent(MANAGEMENT_URL, new Manager(this));
            _manager.RegisterProvider(this);
            Load();
        }

        private void Load()
        {
            try {
                XmlSerializer serialiser = new XmlSerializer(typeof(Storage));
                using (TextReader reader = new StreamReader(_filename))
                    _storage = (Storage)serialiser.Deserialize(reader);
            }
            catch (FileNotFoundException) {
                _storage = new Storage();
                InitUsers();
            }
            catch (InvalidOperationException) {
                File.Move(_filename, _filename + ".bak");
                _storage = new Storage();
                InitUsers();
            }
        }

        private void InitUsers()
        {
            _storage.AllUsers = new List<User>();
            _storage.AllUsers.Add(
                new User
                {
                    Username = DEFAULT_USERNAME,
                    PasswordHash = DoHash(DEFAULT_PASSWORD),
                    Groups = new string[1] { _manager.AdminGroup },
                }
            );
        }

        public void Changing()
        {
            lock (this) {
                if (_changing == 0)
                    _dirty = 0;
                _changing++;
            }
        }

        public void Done()
        {
            lock (this) {
                _changing--;
                if ((_changing == 0) && (_dirty != 0))
                    Save();
            }
        }

        public void Save()
        {
            lock(this) {
                if (_changing != 0) {
                    _dirty++;
                    return;
                }
                XmlSerializer serialiser = new XmlSerializer(typeof(Storage));
                using (TextWriter writer = new StreamWriter(_filename))
                    serialiser.Serialize(writer, _storage);
            }
        }

        public string Name
        {
            get
            {
                return "Simple login";
            }
        }

        public string DirectLink
        {
            get
            {
                return null;
            }
        }

        public string HTML
        {
            get
            {
                var s = $"<form action=\"{LOGIN_URL}\" method=\"post\"><table>";
                s += $"<tr><td><label for=\"userid\">Username</label>:</td><td><input id=\"userid\" type=\"text\" name=\"{KEY_USERNAME}\"/></td></tr>";
                s += $"<tr><td><label for=\"passid\">Password</label>:</td><td><input id=\"passid\" type=\"password\" name=\"{KEY_PASSWORD}\"/></td></tr>";
                s += "<tr><td></td><td><input type=\"submit\" value=\"Log in\"></td></tr>";
                s += "</table></form>";
                return s;
            }
        }

        public string ManagementLink
        {
            get
            {
                return MANAGEMENT_URL;
            }
        }

        private User Find(string username)
        {
            foreach (var user in _storage.AllUsers) {
                if (user.Username.Equals(username, StringComparison.InvariantCultureIgnoreCase))
                    return user;
            }
            return null;
        }
    }
}
