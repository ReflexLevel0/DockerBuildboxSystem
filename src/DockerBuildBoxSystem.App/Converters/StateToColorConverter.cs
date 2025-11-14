using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DockerBuildBoxSystem.App.Converters
{
    class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is ContainerState)
            {
                var state = (ContainerState)value;
                switch(state)
                {
                    case ContainerState.Running:
                        return "LimeGreen";
                    default:
                        return "Red";
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
