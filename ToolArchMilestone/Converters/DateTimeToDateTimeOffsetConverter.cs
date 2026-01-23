using Microsoft.UI.Xaml.Data;
using System;

namespace ToolArchMilestone.Converters
{
    public class DateTimeToDateTimeOffsetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dt)
            {
                return new DateTimeOffset(dt);
            }
            return DateTimeOffset.Now;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTimeOffset dto)
            {
                return dto.DateTime;
            }
            return DateTime.Now;
        }
    }
}
