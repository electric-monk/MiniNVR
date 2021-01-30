using System;
using System.IO;
using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    [XmlRoot]
    public class Database
    {
        private static readonly string Filename = "config.xml";

        private static readonly Lazy<Database> lazy = new Lazy<Database>(() => Load());

        private int changing = 0;
        private int dirty = 0;

        public Users Users;
        public Cameras Cameras;
        public Storage Storage;

        public static Database Instance { get { return lazy.Value; } }

        private void Connect()
        {
            if (Users == null)
                Users = new Users();
            //Users.OnChange += (s, e) => Save();
            if (Cameras == null)
                Cameras = new Cameras();
            Cameras.OnCameraUpdated += (s, e) => Save();
        }

        private static Database Load()
        {
            Database result;
            try {
                XmlSerializer serialiser = new XmlSerializer(typeof(Database));
                using (TextReader writer = new StreamReader(Filename))
                    result = (Database)serialiser.Deserialize(writer);
            }
            catch (FileNotFoundException) {
                result = new Database();
            }
            catch (InvalidOperationException) {
                Console.WriteLine("Unable to parse config file - moving it out of the way and making a new one");
                File.Move(Filename, Filename + ".bak");
                result = new Database();
            }
            result.Connect();
            return result;
        }

        public void Changing()
        {
            lock (this) {
                if (changing == 0)
                    dirty = 0;
                changing++;
            }
        }

        public void Done()
        {
            lock (this) {
                changing--;
                if ((changing == 0) && (dirty != 0))
                    Save();
            }
        }

        public void Save()
        {
            lock (this) {
                if (changing != 0) {
                    dirty++;
                    return;
                }
                XmlSerializer serialiser = new XmlSerializer(typeof(Database));
                using (TextWriter writer = new StreamWriter(Filename))
                    serialiser.Serialize(writer, this);
            }
        }
    }
}
