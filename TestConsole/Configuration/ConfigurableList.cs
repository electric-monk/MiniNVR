using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml.Serialization;

namespace TestConsole.Configuration
{
    public abstract class ConfigurableList<Type> where Type : ConfigurableList<Type>.BaseType, new()
    {
        public abstract class BaseType : IEquatable<BaseType>
        {
            [XmlAttribute]
            public string Identifier;

            public virtual bool Equals(BaseType other)
            {
                return Identifier == other.Identifier;
            }
        }

        private readonly ReaderWriterLockSlim locker = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private List<Type> items = new List<Type>();

        [XmlIgnore]
        public EventHandler<UpdateEvent> OnUpdated;

        protected Type[] AllItems
        {
            get {
                locker.EnterReadLock();
                try {
                    return items.ToArray();
                }
                finally {
                    locker.ExitReadLock();
                }
            }
            set {
                locker.EnterWriteLock();
                try {
                    items = new List<Type>(value);
                }
                finally {
                    locker.ExitWriteLock();
                }
            }
        }

        public void Add(Type item)
        {
            locker.EnterWriteLock();
            try {
                var matches = items.IndexOf(item);
                if (matches == -1)
                    items.Add(item);
                else
                    items[matches] = item;
                AsyncUpdate(item, true);
            }
            finally {
                locker.ExitWriteLock();
            }
        }

        public void Remove(string identifier)
        {
            Type found = null;
            locker.EnterWriteLock();
            try {
                var matches = items.IndexOf(new Type { Identifier = identifier });
                if (matches != -1) {
                    found = items[matches];
                    items.RemoveAt(matches);
                }
                if (found != null)
                    AsyncUpdate(found, false);
            }
            finally {
                locker.ExitWriteLock();
            }
        }

        public class UpdateEvent : EventArgs
        {
            public Type ChangedItem { get; set; }
            public bool AddedUpdated { get; set; }
        }

        private void AsyncUpdate(Type camera, bool addedUpdated)
        {
            UpdateEvent ev = new UpdateEvent { ChangedItem = camera, AddedUpdated = addedUpdated };
            OnUpdated?.Invoke(this, ev);
        }
    }
}
