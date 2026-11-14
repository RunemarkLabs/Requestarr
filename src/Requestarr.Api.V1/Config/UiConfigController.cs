using System.Linq;
using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Http.REST.Attributes;
using Requestarr.Core.Provisioning;
using Requestarr.Http;

namespace Requestarr.Api.V1.Config
{
    [V1ApiController("config/ui")]
    public class UiConfigController : ConfigController<UiConfigResource>
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IProvisioningService _provisioning;

        public UiConfigController(IConfigFileProvider configFileProvider, IConfigService configService, ILocalizationService localizationService, IProvisioningService provisioning)
            : base(configService)
        {
            _configFileProvider = configFileProvider;
            _provisioning = provisioning;

            SharedValidator.RuleFor(c => c.UILanguage)
                           .NotEmpty()
                           .WithMessage("The UI Language value cannot be empty");

            SharedValidator.RuleFor(c => c.UILanguage).Custom((value, context) =>
            {
                if (!localizationService.GetLocalizationOptions().Any(o => o.Value == value))
                {
                    context.AddFailure("Invalid UI Language value");
                }
            });
        }

        [RestPutById]
        [Consumes("application/json")]
        [Produces("application/json")]
        public override ActionResult<UiConfigResource> SaveConfig([FromBody] UiConfigResource resource)
        {
            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            return Accepted(resource.Id);
        }

        protected override UiConfigResource ToResource(IConfigService model)
        {
            var resource = UiConfigResourceMapper.ToResource(_configFileProvider, model);
            resource.Provisioned = _provisioning.ProvisionedSettings;

            return resource;
        }
    }
}
