using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;

namespace Requestarr.Core.Provisioning
{
    [CheckOn(typeof(ProvisioningCompletedEvent))]
    public class ProvisioningCheck : HealthCheckBase
    {
        private readonly IProvisioningService _provisioningService;

        public ProvisioningCheck(IProvisioningService provisioningService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _provisioningService = provisioningService;
        }

        public override HealthCheck Check()
        {
            var result = _provisioningService.LastResult;

            if (result.Outcome == ProvisioningOutcome.Failed)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.ProvisioningFailed,
                    $"Provisioning file could not be applied: {result.Error}",
                    "#provisioning-failed");
            }

            return new HealthCheck(GetType());
        }
    }
}
