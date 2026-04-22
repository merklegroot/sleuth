using System.Reflection;

namespace SleuthRay;

public interface IEmbeddedResourceReader
{
    string ReadText(string resourceName, Assembly? assembly = null);
}

internal sealed class EmbeddedResourceReader : IEmbeddedResourceReader
{
    public string ReadText(string resourceName, Assembly? assembly = null)
    {
        var effectiveAssembly = assembly ?? Assembly.GetExecutingAssembly();
        var names = effectiveAssembly.GetManifestResourceNames();

        // We might only know the leaf file name; allow suffix match against embedded resource names.
        var matchingName = names.FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.Ordinal));
        if (matchingName is null)
        {
            throw new InvalidOperationException($"Resource {resourceName} not found in assembly {effectiveAssembly.GetName().Name}");
        }

        using var stream = effectiveAssembly.GetManifestResourceStream(matchingName)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}