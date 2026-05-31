using System;

namespace K13A.TSMP
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class TransSyncAttribute : Attribute
    {
        public string Key { get; }
        public NetworkSyncDirection Direction { get; set; }
        public int Priority { get; set; }
        public bool SendOnChange { get; set; }
        public float MinSendInterval { get; set; }
        public string EnabledBy { get; set; }

        public TransSyncAttribute()
        {
            Direction = NetworkSyncDirection.SendReceive;
            SendOnChange = true;
        }

        public TransSyncAttribute(string key)
        {
            Key = key;
            Direction = NetworkSyncDirection.SendReceive;
            SendOnChange = true;
        }
    }
}
