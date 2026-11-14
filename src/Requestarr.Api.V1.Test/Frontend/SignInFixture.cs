using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Requestarr.Http.Frontend;

namespace Requestarr.Api.V1.Test.Frontend
{
    // Signed out, a visit to any page of the web app goes to the login page. Since .NET 10 an endpoint marked as an API
    // answers 401 instead, which left a bare browser error after signing out (November 14, 2026).
    [TestFixture]
    public class SignInFixture
    {
        [Test]
        public void the_web_apps_pages_are_not_an_api_so_signed_out_visits_go_to_the_login_page()
        {
            typeof(StaticResourceController).GetCustomAttributes(true).OfType<ApiControllerAttribute>().Should().BeEmpty();
        }
    }
}
