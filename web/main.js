// Owner: integration. The only file that touches the browser: it starts the .NET WebAssembly engine and hands it
// to the Limen kernel. The engine decides everything; this transport relays JSON and nothing else.
import { BrowserKernel } from "@echelon-foundry/limen";
import { dotnet } from "./_framework/dotnet.js";

const createWasmTransport = () => {
  let exports;
  return {
    async start() {
      const runtime = await dotnet.create();
      exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    },
    async dispatch(message) {
      return JSON.parse(exports.Causa.Host.HostShim.Dispatch(JSON.stringify(message)));
    },
  };
};

const diagnostics = {
  report(event) {
    if (event.kind === "BridgeError") console.error(`causa bridge ${event.phase}: ${event.detail}`);
  },
};

const kernel = new BrowserKernel(createWasmTransport(), document, diagnostics);
await kernel.start();
document.getElementById("causa-loading")?.remove();
document.documentElement.dataset.causaReady = kernel.status;
