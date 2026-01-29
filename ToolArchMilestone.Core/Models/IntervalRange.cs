using System;

namespace ToolArchMilestone.Core.Models
{
    public struct IntervalRange
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public override string ToString()
        {
            return $"{Start:dd/MM/yyyy HH:mm} -> {End:dd/MM/yyyy HH:mm}";
        }
    }
}
