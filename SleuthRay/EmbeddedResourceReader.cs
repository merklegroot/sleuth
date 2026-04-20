using System.Reflection;
using System.Linq;

namespace SleuthRay;

public interface IEmbeddedResourceReader
{
    string ReadText(string resourceName, Assembly? assembly = null);
}

public class EmbeddedResourceReader : IEmbeddedResourceReader
{
    public string ReadText(string resourceName, Assembly? assembly = null)
    {
        var effectiveAssembly = assembly ?? Assembly.GetExecutingAssembly();
        var names = effectiveAssembly.GetManifestResourceNames();

        // find a resource name that matches. given that the we might just know the file name but it may or may not include the path.
        var matchingName = names.FirstOrDefault(name => name.EndsWith(resourceName));
        if (matchingName is null)
        {
            throw new InvalidOperationException($"Resource {resourceName} not found in assembly {effectiveAssembly.GetName().Name}");
        }

        // now read and return it.
        using var stream = effectiveAssembly.GetManifestResourceStream(matchingName)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}