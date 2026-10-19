using NzbDrone.Common.Exceptions;

namespace Requestarr.Core.Provisioning
{
    public class ProvisioningException : NzbDroneException
    {
        public ProvisioningException(string message)
            : base(message)
        {
        }
    }
}
