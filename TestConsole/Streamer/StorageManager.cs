using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestConsole.Streamer
{
    public class StorageManager
    {
        private Dictionary<string, Recorder.DataFile> recorders = new Dictionary<string, Recorder.DataFile>();

        public StorageManager()
        {
            Configuration.Database.Instance.Storage.OnUpdated += OnStorage;
            foreach (var container in Configuration.Database.Instance.Storage.AllContainers)
                AddStorage(container);
        }

        private void OnStorage(object sender, Configuration.ConfigurableList<Configuration.Storage.Container>.UpdateEvent e)
        {
            if (e.AddedUpdated)
                AddStorage(e.ChangedItem);
            else
                RemoveStorage(e.ChangedItem.Identifier);
        }

        private void AddStorage(Configuration.Storage.Container container)
        {
            RemoveStorage(container.Identifier);
            recorders.Add(container.Identifier, new Recorder.DataFile(container));
        }

        private void RemoveStorage(string identifier)
        {
            if (recorders.ContainsKey(identifier))
                recorders[identifier].Stop();
        }

        public Recorder.DataFile GetStorage(string identifier)
        {
            Recorder.DataFile result;
            if (!recorders.TryGetValue(identifier, out result))
                result = null;
            return result;
        }
    }
}
