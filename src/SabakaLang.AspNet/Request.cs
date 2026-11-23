using System.Globalization;
using Microsoft.AspNetCore.Http;
using SabakaLang.Extensions;

namespace SabakaLang.AspNet;

/// <summary>The incoming request, as scripts see it: <c>Response index(Request req) { ... }</c></summary>
[SabakaClass("Request", Module = "web", Description = "Incoming HTTP request")]
public sealed class Request
{
    private readonly HttpContext _http;
    private readonly string _filesRoot;

    internal Request(HttpContext http, string body, string filesRoot)
    {
        _http = http;
        _filesRoot = filesRoot;
        Body = body;
    }

    [SabakaProperty] public string Method => _http.Request.Method;
    [SabakaProperty("path")] public string UrlPath => _http.Request.Path.Value ?? "/";
    [SabakaProperty] public string Ip => _http.Connection.RemoteIpAddress?.ToString() ?? "";
    [SabakaProperty] public string ContentType => _http.Request.ContentType ?? "";

    /// <summary>Raw text body (empty for form posts - use <see cref="Form"/>).</summary>
    [SabakaProperty] public string Body { get; }

    [SabakaMethod("query")]
    public string? Query(string name) => _http.Request.Query.TryGetValue(name, out var v) ? v.ToString() : null;

    /// <summary>Route value: for <c>/users/{id}</c> use <c>req.param("id")</c>.</summary>
    [SabakaMethod("param")]
    public string? Param(string name) =>
        _http.Request.RouteValues.TryGetValue(name, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    [SabakaMethod("header")]
    public string? Header(string name) => _http.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null;

    [SabakaMethod("cookie")]
    public string? Cookie(string name) => _http.Request.Cookies.TryGetValue(name, out var v) ? v : null;

    /// <summary>Field of an application/x-www-form-urlencoded or multipart form.</summary>
    [SabakaMethod("form")]
    public string? Form(string name) =>
        _http.Request.HasFormContentType && _http.Request.Form.TryGetValue(name, out var v) ? v.ToString() : null;

    /// <summary>Original file name of an uploaded file field, or null.</summary>
    [SabakaMethod("uploadName")]
    public string? UploadName(string field) =>
        _http.Request.HasFormContentType ? _http.Request.Form.Files.GetFile(field)?.FileName : null;

    /// <summary>Saves an uploaded file under the files root (see SabakaWebOptions.FilesRoot). Returns false if there is no such upload or the path is not allowed.</summary>
    [SabakaMethod("saveUpload")]
    public bool SaveUpload(string field, string relativePath)
    {
        if (!_http.Request.HasFormContentType) return false;
        var file = _http.Request.Form.Files.GetFile(field);
        var full = PathGuard.Resolve(_filesRoot, relativePath);
        if (file is null || full is null) return false;

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        using var stream = File.Create(full);
        file.CopyTo(stream);
        return true;
    }
}
