using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DockerBuildBoxSystem.App.Converters
{
    /// <summary>
    /// Converter that converts an integer value to a boolean (false if 0, true otherwise)
    /// </summary>
    internal class IntToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is int number)
            {
                return number > 0;
            }
            throw new ArgumentException($"Passed a non-int value to converter");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
