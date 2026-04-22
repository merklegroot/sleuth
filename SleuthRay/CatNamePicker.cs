using System.Reflection;

namespace SleuthRay;

public interface ICatNamePicker
{
    string Pick(Assembly? assembly = null);

    /// <summary>
    /// Picks a name not present in <paramref name="exclude"/> when possible.
    /// If the pool is exhausted, falls back to any name (duplicates allowed).
    /// </summary>
    string PickExcluding(IReadOnlySet<string> exclude, Assembly? assembly = null);
}

public sealed class CatNamePicker(ICatNameRepo repo) : ICatNamePicker
{
    public string Pick(Assembly? assembly = null)
    {
        var names = repo.List(assembly);
        if (names.Length == 0)
        {
            return "Cat";
        }

        return names[Random.Shared.Next(names.Length)];
    }

    public string PickExcluding(IReadOnlySet<string> exclude, Assembly? assembly = null)
    {
        var names = repo.List(assembly);
        if (names.Length == 0)
        {
            return "Cat";
        }

        if (exclude.Count == 0)
        {
            return Pick(assembly);
        }

        int first = Random.Shared.Next(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            int idx = (first + i) % names.Length;
            string candidate = names[idx];
            if (!exclude.Contains(candidate))
            {
                return candidate;
            }
        }

        // Pool too small (or all names already used): allow duplicates.
        return Pick(assembly);
    }
}

