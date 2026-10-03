using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Auth;

/// <summary>Plain HTML: the Blazor shell only starts once a session exists.</summary>
internal static class LoginPage
{
    internal static IResult Render(
        string title,
        AntiforgeryTokenSet tokens,
        string? returnUrl,
        string? message,
        int statusCode = StatusCodes.Status200OK
    )
    {
        var error = message is null ? "" : $"""<p class="error">{Escape(message)}</p>""";
        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{Escape(title)}} — sign in</title>
              <style>
                body { font-family: system-ui, sans-serif; display: flex; justify-content: center; padding-top: 12vh; background: #f4f5f7; margin: 0; }
                form { background: #fff; padding: 2rem; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.08); width: 20rem; display: grid; gap: .75rem; }
                h1 { font-size: 1.25rem; margin: 0 0 .5rem; }
                input { padding: .5rem; font-size: 1rem; border: 1px solid #ccc; border-radius: 4px; }
                button { padding: .6rem; font-size: 1rem; border: 0; border-radius: 4px; background: #1976d2; color: #fff; cursor: pointer; }
                .error { color: #b00020; margin: 0; }
              </style>
            </head>
            <body>
              <form method="post" action="{{AdminForgeAuth.LoginPath}}">
                <h1>{{Escape(title)}}</h1>
                {{error}}
                <input type="hidden" name="{{tokens.FormFieldName}}" value="{{tokens.RequestToken}}">
                <input type="hidden" name="returnUrl" value="{{Escape(returnUrl ?? "")}}">
                <input name="username" placeholder="Username" autocomplete="username" required autofocus>
                <input name="password" type="password" placeholder="Password" autocomplete="current-password" required>
                <button type="submit">Sign in</button>
              </form>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: statusCode);
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
