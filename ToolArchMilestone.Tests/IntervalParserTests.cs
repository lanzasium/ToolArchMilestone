using System;
using ToolArchMilestone.Core.Helpers;
using Xunit;

namespace ToolArchMilestone.Tests
{
    public class IntervalParserTests
    {
        [Fact]
        public void ParseContent_ShouldParseStandardItalianFormat()
        {
            var content = "01/10/2023 10:00 - 01/10/2023 11:00";
            var result = IntervalParser.ParseContent(content);

            Assert.Single(result);
            Assert.Equal(new DateTime(2023, 10, 1, 10, 0, 0), result[0].Start);
            Assert.Equal(new DateTime(2023, 10, 1, 11, 0, 0), result[0].End);
        }

        [Fact]
        public void ParseContent_ShouldParseMultipleLines()
        {
            var content = "01/10/2023 10:00 - 01/10/2023 11:00\n02/10/2023 12:00 -> 02/10/2023 13:00";
            var result = IntervalParser.ParseContent(content);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void ParseContent_ShouldIgnoreInvalidLines()
        {
            var content = "invalid line\n01/10/2023 10:00; 01/10/2023 11:00";
            var result = IntervalParser.ParseContent(content);

            Assert.Single(result);
        }
    }
}
