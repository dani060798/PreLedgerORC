using AngleSharp.Dom;
using Ganss.Xss;

namespace PreLedgerORC.Services;

public class HtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();

        // Minimal email-like formatting allowlist
        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "p", "br",
            "strong", "b",
            "em", "i",
            "u",
            "h1", "h2", "h3",
            "ul", "ol", "li",
            "blockquote",
            "a"
        })
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedAttributes.Add("rel");
        _sanitizer.AllowedAttributes.Add("target");

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");

        // Harden links: rel + target
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement el && el.NodeName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("rel", "noopener noreferrer");
                el.SetAttribute("target", "_blank");
            }
        };
    }

    public string Sanitize(string? html)
    {
        html ??= "";
        return _sanitizer.Sanitize(html);
    }
}
