using System.Reflection;

namespace SleuthRay;

public enum WandererTalkKind
{
    Idle,
    Shoot,
    Hurt,
    Spawn,
    Death,
}

public interface IWandererTalkPicker
{
    string Pick(WandererTalkKind kind, Assembly? assembly = null);
}

public sealed class WandererTalkPicker(IWandererTalkRepo repo) : IWandererTalkPicker
{
    public string Pick(WandererTalkKind kind, Assembly? assembly = null)
    {
        var data = repo.Get(assembly);
        var lines = kind switch
        {
            WandererTalkKind.Idle => data.Idle,
            WandererTalkKind.Shoot => data.Shoot,
            WandererTalkKind.Hurt => data.Hurt,
            WandererTalkKind.Spawn => data.Spawn,
            WandererTalkKind.Death => data.Death,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown talk kind."),
        };

        if (lines.Length == 0)
        {
            return "";
        }

        return lines[Random.Shared.Next(lines.Length)];
    }
}

