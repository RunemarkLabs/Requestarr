using NzbDrone.Core.Configuration;
using Requestarr.Http;

namespace Requestarr.Api.V1.Config
{
    [V1ApiController("config/downloadclient")]
    public class DownloadClientConfigController : ConfigController<DownloadClientConfigResource>
    {
        public DownloadClientConfigController(IConfigService configService)
            : base(configService)
        {
        }

        protected override DownloadClientConfigResource ToResource(IConfigService model)
        {
            return DownloadClientConfigResourceMapper.ToResource(model);
        }
    }
}
