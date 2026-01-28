using Microsoft.UI.Xaml.Data;
using System;

namespace ToolArchMilestone.Converters
{
    public class NullableBooleanToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b;
            }
            // Check for boxed bool? which is just bool or null
            // We can just check for null if we assume the type is correct,
            // but 'value is bool' handles unboxed bools.
            // If value is a boxed Nullable<bool> that has a value, it is a bool.
            // If it is null, it is null.
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b;
            }
            return false;
        }
    }
}
