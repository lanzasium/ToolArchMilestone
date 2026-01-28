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
            if (value is bool? nb)
            {
                return nb.GetValueOrDefault();
            }
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
