using System;
using System.Collections.Generic;
using System.Linq;

namespace Ops.Plugins.Registration
{
    public sealed class AttributeList
    {
        private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        private AttributeList(IReadOnlyCollection<string> values)
        {
            Values = values;
        }

        public IReadOnlyCollection<string> Values { get; }

        public static AttributeList Empty { get; } = new AttributeList(Array.Empty<string>());

        public static AttributeList From(IEnumerable<string> values)
        {
            if (values == null) return Empty;

            var normalized = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(Comparer)
                .OrderBy(v => v, Comparer)
                .ToArray();

            return normalized.Length == 0 ? Empty : new AttributeList(normalized);
        }

        public static AttributeList Parse(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? Empty
                : From(value.Split(','));
        }

        public bool SetEquals(AttributeList other)
        {
            other = other ?? Empty;
            return Values.Count == other.Values.Count && !Values.Except(other.Values, Comparer).Any();
        }

        public override string ToString()
        {
            return string.Join(",", Values);
        }
    }
}
