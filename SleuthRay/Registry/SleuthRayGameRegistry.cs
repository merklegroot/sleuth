using Microsoft.Extensions.DependencyInjection;
using SleuthRay;

namespace SleuthRay.Registry;

public static class SleuthRayGameRegistry
{
    public static IServiceCollection RegisterSleuthRay(this IServiceCollection services) =>
        services
            .Configure<SleuthRayOptions>(_ => { })
            .AddSingleton<ISleuthRayGame, SleuthRayGame>();
}
