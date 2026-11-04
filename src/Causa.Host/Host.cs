// Owner: integration. The .NET WebAssembly host shim: marshalling glue only, no application decision.
// It supplies the clock and a fresh identifier and relays the Limen protocol JSON to the F# engine.
using System;
using System.Runtime.InteropServices.JavaScript;

namespace Causa.Host;

public static partial class HostShim
{
    [JSExport]
    public static string Dispatch(string message) =>
        Causa.App.Content.Engine.dispatch(DateTimeOffset.UtcNow, Guid.NewGuid(), message);

    public static void Main() { }
}
