using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SleuthRay;
using SleuthRay.Registry;

var builder = Host.CreateApplicationBuilder(args);
new SleuthRayGameRegistrar().Register(builder.Services);

using var host = builder.Build();

var game = host.Services.GetRequiredService<ISleuthRayGame>();
game.Run();
