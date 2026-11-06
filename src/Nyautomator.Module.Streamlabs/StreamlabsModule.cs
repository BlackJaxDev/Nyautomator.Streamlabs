using Microsoft.Extensions.DependencyInjection;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;

namespace Nyautomator.Modules.Streamlabs;

public sealed class StreamlabsModule : INyautomatorModule
{
    public NyautomatorModuleDescriptor Descriptor { get; } = new(
        StreamlabsModuleApiHandler.ModuleIdValue,
        "Streamlabs",
        "Streamlabs OAuth account linking and status.",
        "0.1.0");

    public void ConfigureRuntime(NyautomatorModuleRuntimeContext context)
    {
        var handler = new StreamlabsModuleApiHandler(context.Services.GetService<IModuleOptionsProvider>());
        context.ApiHandlers.Register(handler);
    }
}
