using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SabakaLang.LanguageServer;

public static class SabakaDocumentSelector
{
    public static readonly TextDocumentSelector Instance =
        TextDocumentSelector.ForLanguage("sabaka");
}