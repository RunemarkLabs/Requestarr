using NzbDrone.Core.Configuration;
using Requestarr.Http.REST;

namespace Requestarr.Api.V1.Config
{
    public class DownloadClientConfigResource : RestResource
    {
    }

    public static class DownloadClientConfigResourceMapper
    {
        public static DownloadClientConfigResource ToResource(IConfigService model)
        {
            return new DownloadClientConfigResource
            {
            };
        }
    }
}
