using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SleuthRay.Registry;

namespace SleuthRay;

static class Program
{
    static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.RegisterSleuthRay();

        using var host = builder.Build();

        var game = host.Services.GetRequiredService<ISleuthRayGame>();
        game.Run();
    }
}
