using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public class InvalidJsonControlsException: Exception
    {
        public InvalidJsonControlsException(string message, Exception inner)
            :base(message, inner) {}

    }
}
