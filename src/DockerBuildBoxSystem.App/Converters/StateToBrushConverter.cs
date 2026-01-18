using DockerBuildBoxSystem.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;


namespace DockerBuildBoxSystem.App.Converters
{
    /// <summary>
    /// Converts <see cref="ContainerState"/> into a brush representation
    /// </summary>
    class StateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is not ContainerState)
            {
                return "";
            }

            var state = (ContainerState)value;

            Debug.WriteLine(state);

            object? color = state == ContainerState.Running ? 
                Application.Current.Resources["SuccessBrush"] : 
                Application.Current.Resources["ErrorBrush"];
            return color == null ? "" : color;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
