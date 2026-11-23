using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using SabakaLang.Extensions;
using SabakaLang.Runtime;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace SabakaLang.AspNet;

/// <summary>
/// What a controller function returns. Build it with the static factories and refine it with the "with..." methods:
/// <c>return Response.json("{}").withStatus(201).withHeader("X-Id", "7");</c>
/// </summary>
[SabakaClass("Response", Module = "web", Description = "HTTP response returned from a controller function")]
public sealed class Response
{
    internal enum Kind { Text, Bytes, File, Redirect, Empty }

    private static readonly FileExtensionContentTypeProvider Mime = new();

    private readonly Kind _kind;
    private int _status = 200;
    private string? _contentType;
    private string? _text;          // body text, or the redirect URL
    private byte[]? _data;
    private string? _path;          // relative path under the files root
    private string? _downloadName;
    private readonly List<(string Name, string Value)> _headers = [];
    private readonly List<(string Name, string Value, int? MaxAge)> _cookies = [];

    private Response(Kind kind) { _kind = kind; }

    internal static Response Plain(int status, string text) =>
        new(Kind.Text) { _status = status, _text = text, _contentType = "text/plain; charset=utf-8" };

    // ------------------------------------------------------------ factories

    [SabakaMethod("text")]
    public static Response FromText(string body) => new(Kind.Text) { _text = body, _contentType = "text/plain; charset=utf-8" };

    [SabakaMethod("html")]
    public static Response FromHtml(string body) => new(Kind.Text) { _text = body, _contentType = "text/html; charset=utf-8" };

    /// <summary>Raw JSON text.</summary>
    [SabakaMethod("json")]
    public static Response FromJson(string json) => new(Kind.Text) { _text = json, _contentType = "application/json; charset=utf-8" };

    /// <summary>Serializes a value (numbers, strings, arrays, script objects) to JSON.</summary>
    [SabakaMethod("jsonOf")]
    public static Response FromValue(Value value) => FromJson(Json.Stringify(value));

    [SabakaMethod("bytes")]
    public static Response FromBytes(int[] data, string contentType) =>
        new(Kind.Bytes) { _data = data.Select(b => checked((byte)b)).ToArray(), _contentType = contentType };

    [SabakaMethod("base64")]
    public static Response FromBase64(string base64, string contentType) =>
        new(Kind.Bytes) { _data = Convert.FromBase64String(base64), _contentType = contentType };

    /// <summary>Streams a file from the files root. Content type is guessed from the extension; Range requests are supported.</summary>
    [SabakaMethod("file")]
    public static Response FromFile(string relativePath) => new(Kind.File) { _path = relativePath };

    /// <summary>Like <c>file</c>, but the browser downloads it under <paramref name="fileName"/>.</summary>
    [SabakaMethod("download")]
    public static Response FromDownload(string relativePath, string fileName) =>
        new(Kind.File) { _path = relativePath, _downloadName = fileName };

    [SabakaMethod("redirect")]
    public static Response FromRedirect(string url) => new(Kind.Redirect) { _status = 302, _text = url };

    /// <summary>Status code only, no body: <c>Response.empty(404)</c>.</summary>
    [SabakaMethod("empty")]
    public static Response FromEmpty(int status) => new(Kind.Empty) { _status = status };

    // ------------------------------------------------------------ refinements

    [SabakaMethod("withStatus")] public Response WithStatus(int code) { _status = code; return this; }
    [SabakaMethod("withType")] public Response WithType(string contentType) { _contentType = contentType; return this; }
    [SabakaMethod("withHeader")] public Response WithHeader(string name, string value) { _headers.Add((name, value)); return this; }
    [SabakaMethod("withCookie")] public Response WithCookie(string name, string value) { _cookies.Add((name, value, null)); return this; }
    [SabakaMethod("withCookie")] public Response WithCookie(string name, string value, int maxAgeSeconds) { _cookies.Add((name, value, maxAgeSeconds)); return this; }

    // ------------------------------------------------------------ writing

    internal async Task WriteAsync(HttpContext http, SabakaWebRuntime runtime)
    {
        var res = http.Response;
        foreach (var (name, value) in _headers) res.Headers[name] = value;
        foreach (var (name, value, maxAge) in _cookies)
        {
            res.Cookies.Append(name, value, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                MaxAge = maxAge is { } s ? TimeSpan.FromSeconds(s) : (TimeSpan?)null
            });
        }

        switch (_kind)
        {
            case Kind.Text:
            case Kind.Bytes:
            {
                var type = _contentType ?? "application/octet-stream";
                var payload = _kind == Kind.Text ? EncodingFor(type).GetBytes(_text ?? "") : _data!;
                res.StatusCode = _status;
                res.ContentType = type;
                res.ContentLength = payload.Length;
                if (!HttpMethods.IsHead(http.Request.Method))
                    await res.Body.WriteAsync(payload, http.RequestAborted);
                break;
            }
            case Kind.Redirect:
                res.StatusCode = _status;
                res.Headers.Location = _text;
                break;
            case Kind.Empty:
                res.StatusCode = _status;
                break;
            case Kind.File:
            {
                var full = runtime.ResolveFile(_path ?? "");
                if (full is null) { res.StatusCode = StatusCodes.Status404NotFound; break; }
                if (!Mime.TryGetContentType(full, out var guessed)) guessed = "application/octet-stream";
                var result = Results.File(full, _contentType ?? guessed, _downloadName, enableRangeProcessing: true);
                await result.ExecuteAsync(http);
                break;
            }
        }
    }

    /// <summary>The charset in the Content-Type picks the encoding (e.g. "text/html; charset=windows-1251"); default UTF-8 without BOM.</summary>
    private static Encoding EncodingFor(string contentType)
    {
        try
        {
            if (MediaTypeHeaderValue.TryParse(contentType, out var mt) && mt.Charset.HasValue)
                return Encoding.GetEncoding(mt.Charset.Value!);
        }
        catch (ArgumentException) { /* unknown charset: fall back */ }
        return new UTF8Encoding(false);
    }
}
