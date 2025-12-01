using DockerBuildBoxSystem.Domain;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public class EnvironmentViewModel: INotifyPropertyChanged
    {
        private readonly EnvironmentService _envService;
        public EnvironmentViewModel()
        {
            _envService = new EnvironmentService();
        }
    }
}
