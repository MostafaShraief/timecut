namespace Timecut.Core.Models;

public class HtmlTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled Template";
    public string Content { get; set; } = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Template</title>
    <style>
        /* Your template styles here */
    </style>
</head>
<body>
    <!-- CONTENT -->
</body>
</html>";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The placeholder marker in the template where content HTML will be injected.
    /// </summary>
    public const string ContentPlaceholder = "<!-- CONTENT -->";
}
