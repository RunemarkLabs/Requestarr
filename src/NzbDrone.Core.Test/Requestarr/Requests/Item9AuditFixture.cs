using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace Requestarr.Core.Test.Requests
{
    // Item 9: every requesting step is audited.
    [TestFixture]
    public class Item9AuditFixture : RequestScenario
    {
        [Test]
        public async Task requesting_steps_should_be_audited_in_order()
        {
            await RequestMovie(Alice, Dune);                                       // create
            await RequestMovie(Bob, Dune);                                         // subscribe
            await AdminButton("req-approve:1");                                    // approve + send
            await RequestMovie(Bob, DuneTwo);                                      // create
            await Harness.SendAsync(AdminSlash("deny").WithOption("id", 2L));      // deny
            await RequestTv(Alice, Severance, "s1");                               // create
            await Harness.SendAsync(UserCommand("request cancel", Alice).WithOption("id", 3L));
            await SetAccess(Bob, "tv", "off");
            await RequestTv(Bob, Severance, "s1");                                 // refused (access)
            await SetAccess(Alice, "movie", "auto");
            Radarr.FailAdds = true;
            Radarr.Catalog.Add(new Backends.MediaLookupResult { Title = "Heat", Year = 1995, TmdbId = 949 });
            await RequestMovie(Alice, 949);                                        // create + failed

            var actions = AuditTrail.Select(a => a.Action).Where(a => a.StartsWith("request.")).ToList();

            actions.Should().Equal(
                "request.create",
                "request.subscribe",
                "request.approve",
                "request.send",
                "request.create",
                "request.deny",
                "request.create",
                "request.cancel",
                "request.refused",
                "request.create",
                "request.failed");

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public async Task send_audit_should_record_instance_item_profile_and_root()
        {
            await SetAccess(Alice, "tv", "auto");
            await RequestTv(Alice, Severance, "all", "future");

            var send = AuditTrail.Single(a => a.Action == "request.send");

            send.Detail.Should().Be("Sonarr item 101, profile HD-1080p, root /data/media, type anime");
            send.Actor.Should().Be("system");
            AuditTrail.Single(a => a.Action == "request.create").Detail.Should().Contain("seasons 1, 2 + future seasons");
        }

        [Test]
        public async Task quota_refusals_are_audited()
        {
            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 1L));
            await RequestMovie(Alice, Dune);
            await RequestMovie(Alice, DuneTwo);

            AuditTrail.Last().Action.Should().Be("request.refused");
            AuditTrail.Last().Detail.Should().Be("quota 1/1");
        }
    }
}
