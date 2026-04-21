using Microsoft.Extensions.DependencyInjection;
using SleuthRay;

namespace SleuthRay.Registry;

public sealed class SleuthRayGameRegistrar
{
    public void Register(IServiceCollection services)
    {
        services
            .Configure<SleuthRayOptions>(_ => { })
            .AddSingleton<IEmbeddedResourceReader, EmbeddedResourceReader>()
            .AddSingleton<ICatNameRepo, CatNameRepo>()
            .AddSingleton<ICatNamePicker, CatNamePicker>()
            .AddSingleton<IWandererTalkRepo, WandererTalkRepo>()
            .AddSingleton<IWandererTalkPicker, WandererTalkPicker>()
            .AddSingleton<IGameplay, Gameplay>()
            .AddSingleton<IEnemyBrain, EnemyBrain>()
            .AddSingleton<IGunshotAudio, GunshotAudio>()
            .AddSingleton<IGamepadMappings, GamepadMappings>()
            .AddSingleton<ISpeechBubbleUi, SpeechBubbleUi>()
            .AddSingleton<IInputReadbackOverlay, InputReadbackOverlay>()
            .AddSingleton<IPlayerStatsMenuUi, PlayerStatsMenuUi>()
            .AddSingleton<ISleuthRayGame, SleuthRayGame>();
    }
}
