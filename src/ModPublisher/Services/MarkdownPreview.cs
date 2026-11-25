using Markdig;

namespace ModPublisher.Services;

/// <summary>Aperçu HTML approché du rendu de chaque site (couleurs de leur thème sombre).</summary>
public static class MarkdownPreview
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseEmojiAndSmiley().Build();

    public static string Render(string markdown, bool curseForge)
    {
        var body = Markdown.ToHtml(markdown, Pipeline);
        var (bg, fg, accent, card) = curseForge
            ? ("#18181b", "#e4e4e7", "#f16436", "#27272a")
            : ("#16181c", "#b0bac5", "#1bd96a", "#26292f");
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            body { background:{{bg}}; color:{{fg}}; font-family:'Segoe UI',Inter,sans-serif; font-size:15px; line-height:1.6; max-width:860px; margin:24px auto; padding:0 20px; }
            h1,h2,h3,h4 { color:#fff; line-height:1.3; } h2 { border-bottom:1px solid #333; padding-bottom:4px; }
            a { color:{{accent}}; } img { max-width:100%; border-radius:6px; }
            code { background:{{card}}; padding:2px 5px; border-radius:4px; font-size:90%; }
            pre { background:{{card}}; padding:12px; border-radius:6px; overflow:auto; }
            table { border-collapse:collapse; } th,td { border:1px solid #3a3a40; padding:5px 10px; }
            details { background:{{card}}; padding:8px 14px; border-radius:6px; margin:10px 0; } summary { cursor:pointer; }
            blockquote { border-left:4px solid {{accent}}; margin-left:0; padding-left:14px; opacity:.85; }
            hr { border:none; border-top:1px solid #333; }
            </style></head><body>{{body}}</body></html>
            """;
    }
}
