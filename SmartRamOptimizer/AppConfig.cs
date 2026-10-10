namespace SmartRamOptimizer;

/// <summary>Values the project's owner fills in before a release.</summary>
internal static class AppConfig
{
    private const string Placeholder = "YOUR_USERNAME";

    /// <summary>Where the "Buy us a coffee" button leads. Replace the placeholder with your own page.</summary>
    public const string CoffeeUrl = "https://buymeacoffee.com/" + Placeholder;

    /// <summary>
    /// False until the placeholder is replaced with a real https link. The button never opens
    /// the placeholder page, which anyone could register and collect donations on.
    /// </summary>
    public static bool IsCoffeeUrlConfigured { get; } =
        !CoffeeUrl.Contains(Placeholder, StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(CoffeeUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
