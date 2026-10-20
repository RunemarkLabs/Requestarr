using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestarr.Core.Test.Discord.Harness
{
    // One of the 16 admin slash commands from before task 3b ("user access", "approve", "queue", ...),
    // with its options. Sending it does the same thing the way it is done now: on the member card
    // (/admin user), the request card (/admin request), or the admin panel. Tests written against the
    // old commands keep reading the same, and prove that nothing the admin could do was lost.
    public class OldAdminCommand
    {
        private readonly Func<OldAdminCommand, Task<InteractionRun>> _run;

        public OldAdminCommand(string command, Func<OldAdminCommand, Task<InteractionRun>> run)
        {
            Command = command;
            _run = run;
        }

        public string Command { get; }

        public Dictionary<string, object> Options { get; } = new();

        public OldAdminCommand WithOption(string name, object value)
        {
            Options[name] = value;
            return this;
        }

        public T Get<T>(string name) => Options.TryGetValue(name, out var value) && value != null ? (T)Convert.ChangeType(value, typeof(T)) : default;

        public bool Has(string name) => Options.ContainsKey(name) && Options[name] != null;

        public Task<InteractionRun> RunAsync() => _run(this);
    }
}
