using System;

namespace Ops.Plugins.Registration
{
    public sealed class RunInUserContextReference
    {
        public string Label { get; set; }
        public Guid? SystemUserId { get; set; }
        public string FullName { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(FullName)) return $"{Label} ({FullName}, {FormatId()})";
                return $"{Label} ({FormatId()})";
            }
        }

        private string FormatId()
        {
            return SystemUserId.HasValue ? SystemUserId.Value.ToString("D") : "Calling User";
        }
    }
}
