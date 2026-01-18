using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class SpinnerOverlayViewModel : ObservableObject
    {
        [ObservableProperty]
        public string? _text;
    }
}
