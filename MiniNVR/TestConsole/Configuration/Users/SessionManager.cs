using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace TestConsole.Configuration.Users
{
    public class SessionManager
    {
        private class LoginEndpoint : WebServer.IEndpoint
        {
            private SessionManager _owner;
            public LoginEndpoint(SessionManager owner)
            {
                _owner = owner;
            }
            public void Handle(HttpListenerContext request)
            {
                HttpListenerResponse response = request.Response;
                string s = null;
                if (_owner._providers.Count == 0) {
                    response.StatusCode = 500;
                    s = "<html><body><h1>Error</h1>No authentication providers configured</body></html>";
                } else if (_owner._providers.Count == 1) {
                    IProvider provider = _owner._providers[0];
                    var redirect = provider.DirectLink;
                    if (redirect != null) {
                        response.Redirect(redirect);
                    } else {
                        s = $"<html><head>Log in - {provider.Name}</head><body><h1>{provider.Name}</h1>{provider.HTML}</body></html>";
                    }
                } else {
                    s = "<html><head><title>Log in</title></head><body>";
                    foreach (var provider in _owner._providers) {
                        var redirect = provider.DirectLink;
                        if (redirect != null) {
                            s += $"<a href=\"{redirect}\">Log in via {provider.Name}</a><br/>";
                        } else {
                            s += $"<b>{provider.Name}</b><br/>{provider.HTML}<hr/>";
                        }
                    }
                    s += "</body></html>";
                }
                if (s != null) {
                    response.ContentEncoding = Encoding.UTF8;
                    var b = response.ContentEncoding.GetBytes(s);
                    response.ContentLength64 = b.Length;
                    response.ContentType = "text/html";
                    response.OutputStream.Write(b, 0, b.Length);
                }
                response.Close();
            }
        }

        private static readonly string SESSION_COOKIE = ".session";

        private string _appname;
        private string _home;
        private string _adminGroup;
        private WebServer _server;
        private List<IProvider> _providers;
        private Dictionary<string, ISession> _sessions;

        public SessionManager(string appname, WebServer server)
        {
            _home = "/";
            _adminGroup = "admin";
            _appname = appname;
            _server = server;
            _providers = new List<IProvider>();
            _sessions = new Dictionary<string, ISession>();
            _server.AddContent(LoginURL, new LoginEndpoint(this));
        }

        private string CookieName
        {
            get
            {
                return _appname + SESSION_COOKIE;
            }
        }

        internal string AppName
        {
            get
            {
                return _appname;
            }
        }

        internal WebServer Server
        {
            get
            {
                return _server;
            }
        }

        internal void RegisterProvider(IProvider provider)
        {
            _providers.Add(provider);
        }

        private Cookie ConstructCookie(ISession session)
        {
            return new Cookie(CookieName, session.Identifier) {
                Secure = true
            };
        }

        internal void RegisterSession(HttpListenerResponse response, ISession session)
        {
            response.SetCookie(ConstructCookie(session));
            _sessions.Add(session.Identifier, session);
        }

        public void ClearCookie(HttpListenerResponse response, ISession session)
        {
            _sessions.Remove(session.Identifier);
            var cookie = ConstructCookie(session);
            cookie.Expires = DateTime.Now.AddDays(-365);
            response.SetCookie(cookie);
        }

        public string LoginURL
        {
            get
            {
                return "/login";
            }
        }

        public string AdminGroup
        {
            get
            {
                return _adminGroup;
            }
            set
            {
                _adminGroup = value;
            }
        }

        public string HomeLink
        {
            get
            {
                return _home;
            }
            set
            {
                _home = value;
            }
        }

        public ISession GetSession(HttpListenerRequest request)
        {
            var cookie = request.Cookies[CookieName];
            if (cookie == null)
                return null;
            ISession session;
            if (_sessions.TryGetValue(cookie.Value, out session)) {
                if (!session.IsValid) {
                    session = null;
                    _sessions.Remove(cookie.Value);
                }
            }
            return session;
        }
    }
}
