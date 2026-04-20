using Microsoft.Extensions.DependencyInjection;

namespace SleuthRay.Registry;

public static class SleuthRayGameRegistry
{
    public static IServiceCollection RegisterSleuthRay(this IServiceCollection services) =>
        services
            .Configure<SleuthRayOptions>(_ => { })
            .AddSingleton<IEmbeddedResourceReader, EmbeddedResourceReader>()
            .AddSingleton<ICatNameRepo, CatNameRepo>()
            .AddSingleton<ICatNamePicker, CatNamePicker>()
            .AddSingleton<ISleuthRayGame, SleuthRayGame>();
}
