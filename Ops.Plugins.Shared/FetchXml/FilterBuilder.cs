using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Ops.Plugins.Shared.FetchXml
{
    public class FilterBuilder
    {
        private string _type = "and";
        private readonly List<string> _conditions = new List<string>();
        private readonly List<FilterBuilder> _childFilters = new List<FilterBuilder>();

        public FilterBuilder(string type = "and")
        {
            _type = type;
        }

        public FilterBuilder WithCondition(string attribute, string conditionOperator, object value)
        {
            var builder = new StringBuilder($"<condition attribute='{attribute}' operator='{conditionOperator}'");

            if (conditionOperator == "in" || conditionOperator == "between")
            {
                builder.Append(">");
                foreach (var item in (IEnumerable)value)
                    builder.Append($"<value>{item}</value>");
                builder.Append("</condition>");
            }
            else
            {
                builder.Append($" value='{value}' />");
            }

            _conditions.Add(builder.ToString());
            return this;
        }

        public FilterBuilder WithCondition(string attribute, string conditionOperator)
        {
            _conditions.Add($"<condition attribute='{attribute}' operator='{conditionOperator}' />");
            return this;
        }

        public FilterBuilder WithFilter(FilterBuilder childFilter)
        {
            _childFilters.Add(childFilter);
            return this;
        }

        public string Build()
        {
            var builder = new StringBuilder($"<filter type='{_type}'>");

            foreach (var childFilter in _childFilters)
                builder.Append(childFilter.Build());

            foreach (var condition in _conditions)
                builder.Append(condition);

            builder.Append("</filter>");
            return builder.ToString();
        }
    }
}
