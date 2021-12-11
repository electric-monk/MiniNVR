using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using IdentityModel.OidcClient;

namespace TestConsole.Configuration.Users
{
    public class OpenID : IProvider
    {
        private class Session : ISession
        {
            private OpenID _owner;
            private string _sessionIdentifier;
            private string _friendlyName;
            private string[] _groups;
            private string _refreshToken;
            private DateTimeOffset _expiryTime;

            public Session(OpenID openID, LoginResult login)
            {
                _owner = openID;
                _refreshToken = login.RefreshToken;
                _expiryTime = login.AccessTokenExpiration;

                var claimsPrincipal = login.User;
                _sessionIdentifier = claimsPrincipal.FindFirst("session_state").Value;
                _friendlyName = claimsPrincipal.FindFirst("preferred_username").Value;
                _groups = Enumerable.ToArray(claimsPrincipal.FindAll("roles").Select(claim => claim.Value));
            }

            public string Identifier
            {
                get
                {
                    return _sessionIdentifier;
                }
            }

            public string Username
            {
                get
                {
                    return _friendlyName;
                }
            }

            public string[] Groups
            {
                get
                {
                    return _groups;
                }
            }

            public bool IsValid
            {
                get
                {
                    if (DateTimeOffset.Now >= _expiryTime) {
                        var result = _owner._client.RefreshTokenAsync(_refreshToken).Result;
                        if (result.IsError)
                            return false;
                        _expiryTime = result.AccessTokenExpiration;
                        _refreshToken = result.RefreshToken;
                        // TODO: reload user data
                    }
                    return true;
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

        private class CertificateChecker : HttpClientHandler
        {
            public CertificateChecker(CertificateInfo config)
            {
                this.ClientCertificateOptions = ClientCertificateOption.Manual;
                this.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) =>
                {
                    if (config.IgnoreCertificate)
                        return true;
                    // TODO: Check certificate from config
                    return true;
                };
            }
        }

        private class EntryPoint : WebServer.IEndpoint
        {
            private OpenID _owner;

            public EntryPoint(OpenID owner)
            {
                _owner = owner;
            }

            public void Handle(HttpListenerContext request)
            {
                request.Response.Redirect(_owner._state.StartUrl);
                request.Response.Close();
            }
        }

        private class Callback : WebServer.IEndpoint
        {
            private OpenID _owner;

            public Callback(OpenID owner)
            {
                _owner = owner;
            }

            public void Handle(HttpListenerContext request)
            {
                var result = _owner._client.ProcessResponseAsync(request.Request.RawUrl, _owner._state).Result;
                var response = request.Response;
                if (result.IsError) {
                    var s = $"<html><head><title>Login error</title></head><body><h1>Error</h1>{result.Error}</body></html>";
                    response.ContentEncoding = System.Text.Encoding.UTF8;
                    response.ContentType = "text/html";
                    var data = response.ContentEncoding.GetBytes(s);
                    response.OutputStream.Write(data, 0, data.Length);
                } else {
                    _owner._manager.RegisterSession(response, new Session(_owner, result));
                    response.Redirect(_owner._manager.HomeLink);
                }
                response.Close();
            }
        }

        private SessionManager _manager;
        private string _friendlyName;
        private string _managementLink;
        private OidcClient _client;
        private AuthorizeState _state;

        private string LoginURL
        {
            get
            {
                return "/oid_login_" + _friendlyName;
            }
        }

        private string CallbackURL
        {
            get
            {
                return "/oid_callback_" + _friendlyName;
            }
        }

        public class CertificateInfo
        {
            public bool IgnoreCertificate;
            // TODO
        }

        public class OIDConfig
        {
            public string ManagementURL;
            public string FriendlyName;
            public string AuthorityBaseURL;
            public string ClientID;
            public string OurBaseURL;
            public CertificateInfo CertificateCheck;
        }

        public OpenID(SessionManager sessionManager, OIDConfig config)
        {
            _managementLink = config.ManagementURL;
            _manager = sessionManager;
            _friendlyName = config.FriendlyName;
            var options = new OidcClientOptions
            {
                Authority = config.AuthorityBaseURL,
                ClientId = config.ClientID,
                Scope = "openid profile roles",
                RedirectUri = config.OurBaseURL + CallbackURL,
            };
            if (config.CertificateCheck != null) {
                var test = new CertificateChecker(config.CertificateCheck);
                options.BackchannelHandler = test;
                options.RefreshTokenInnerHttpHandler = test;
            }
            _client = new OidcClient(options);
            _state = _client.PrepareLoginAsync().Result;
            _manager.Server.AddContent(LoginURL, new EntryPoint(this));
            _manager.Server.AddContent(CallbackURL, new Callback(this));
            _manager.RegisterProvider(this);
        }

        public string Name
        {
            get
            {
                return _friendlyName;
            }
        }

        public string DirectLink
        {
            get
            {
                return LoginURL;
            }
        }

        public string HTML
        {
            get
            {
                return null;
            }
        }

        public string ManagementLink
        {
            get
            {
                return _managementLink;
            }
        }
    }
}
