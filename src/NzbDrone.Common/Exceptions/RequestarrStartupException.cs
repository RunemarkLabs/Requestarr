using System;

namespace NzbDrone.Common.Exceptions
{
    public class RequestarrStartupException : NzbDroneException
    {
        public RequestarrStartupException(string message, params object[] args)
            : base("Requestarr failed to start: " + string.Format(message, args))
        {
        }

        public RequestarrStartupException(string message)
            : base("Requestarr failed to start: " + message)
        {
        }

        public RequestarrStartupException()
            : base("Requestarr failed to start")
        {
        }

        public RequestarrStartupException(Exception innerException, string message, params object[] args)
            : base("Requestarr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public RequestarrStartupException(Exception innerException, string message)
            : base("Requestarr failed to start: " + message, innerException)
        {
        }

        public RequestarrStartupException(Exception innerException)
            : base("Requestarr failed to start: " + innerException.Message)
        {
        }
    }
}
