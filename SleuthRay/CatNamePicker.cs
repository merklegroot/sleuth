using System.Reflection;

namespace SleuthRay;

public interface ICatNamePicker
{
    string Pick(Assembly? assembly = null);
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
}

