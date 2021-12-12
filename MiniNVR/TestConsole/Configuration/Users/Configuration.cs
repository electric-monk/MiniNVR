using System.IO;
using System.Xml.Serialization;

namespace TestConsole.Configuration.Users
{
    [XmlRoot]
    public class Configuration
    {
        private static readonly string DEFAULT_FILENAME = "users.xml";

        public PlainLogin.PLConfig PlainConfig;

        public OpenID.OIDConfig[] OpenIDConfigs;

        public static void Load(SessionManager sessionManager, string filename)
        {
            Configuration config;
            try
            {
                XmlSerializer serialiser = new XmlSerializer(typeof(Configuration));
                using (TextReader reader = new StreamReader(filename))
                    config = (Configuration)serialiser.Deserialize(reader);
            }
            catch (FileNotFoundException)
            {
                // Some defaults, for testing
                config = new Configuration(){
                    PlainConfig = new PlainLogin.PLConfig(){
                        Salt = sessionManager.AppName,
                        UserFilename = Database.GetSettingsPath(DEFAULT_FILENAME),
                    },
                };
            }
            if (config.PlainConfig != null)
                new PlainLogin(sessionManager, config.PlainConfig);
            if (config.OpenIDConfigs != null)
                foreach (var oid in config.OpenIDConfigs)
                    new OpenID(sessionManager, oid);
        }
    }
}
