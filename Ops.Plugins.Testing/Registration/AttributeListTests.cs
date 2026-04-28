using Ops.Plugins.Registration;
using Xunit;

namespace Ops.Plugins.Testing.Registration
{
    public class AttributeListTests
    {
        [Fact]
        public void Parse_NormalizesBlanksDuplicatesSpacesAndOrdering()
        {
            var list = AttributeList.Parse(" statuscode, actualclosedate,STATUSCODE ,, name ");

            Assert.Equal("actualclosedate,name,statuscode", list.ToString());
        }

        [Fact]
        public void SetEquals_IgnoresCaseAndOrder()
        {
            var left = AttributeList.Parse("statuscode,actualclosedate");
            var right = AttributeList.Parse("ActualCloseDate, StatusCode");

            Assert.True(left.SetEquals(right));
        }
    }
}
