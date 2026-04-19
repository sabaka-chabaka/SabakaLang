using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SymbolKind = SabakaLang.Compiler.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class SignatureHelpHandler(DocumentStore store) : SignatureHelpHandlerBase
{
    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = SabakaDocumentSelector.Instance,
            TriggerCharacters = new Container<string>("(", ",")
        };

    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<SignatureHelp?>(null);

        var source = analysis.Source;
        var offset = PositionHelper.ToOffset(source, request.Position);

        if (!TryFindCallContext(source, offset, out var funcName, out var argIndex))
            return Task.FromResult<SignatureHelp?>(null);

        var symbols = analysis.Bind.Symbols.Lookup(funcName).ToList();
        if (symbols.Count == 0) return Task.FromResult<SignatureHelp?>(null);

        var signatures = symbols
            .Where(s => s.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn)
            .Select(s => BuildSignature(s))
            .ToList();

        if (signatures.Count == 0) return Task.FromResult<SignatureHelp?>(null);

        return Task.FromResult<SignatureHelp?>(new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signatures),
            ActiveSignature = 0,
            ActiveParameter = argIndex
        });
    }

    private static SignatureInformation BuildSignature(Symbol sym)
    {
        var rawParams = sym.Parameters ?? "";
        var paramParts = rawParams.Length > 0
            ? rawParams.Split(',', StringSplitOptions.TrimEntries)
            : [];

        var parameters = paramParts.Select(p => new ParameterInformation
        {
            Label = new ParameterInformationLabel(p),
            Documentation = new StringOrMarkupContent(p)
        }).ToList();

        var label = $"{sym.Name}({string.Join(", ", paramParts)}) : {sym.Type}";

        return new SignatureInformation
        {
            Label = label,
            Parameters = new Container<ParameterInformation>(parameters),
            Documentation = new StringOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = $"**{sym.Kind}** `{sym.Name}`\n\nReturns: `{sym.Type}`"
            })
        };
    }

    private static bool TryFindCallContext(string source, int offset,
        out string funcName, out int argIndex)
    {
        funcName = "";
        argIndex = 0;

        var depth = 0;
        var commas = 0;
        var pos = offset - 1;

        while (pos >= 0)
        {
            var c = source[pos];
            switch (c)
            {
                case ')':
                case ']':
                    depth++;
                    break;
                case '(':
                    if (depth == 0)
                    {
                        argIndex = commas;
                        var nameEnd = pos - 1;
                        while (nameEnd >= 0 && char.IsWhiteSpace(source[nameEnd])) nameEnd--;
                        var nameStart = nameEnd;
                        while (nameStart > 0 && (char.IsLetterOrDigit(source[nameStart - 1]) || source[nameStart - 1] == '_'))
                            nameStart--;
                        if (nameStart <= nameEnd)
                        {
                            funcName = source[nameStart..(nameEnd + 1)];
                            return funcName.Length > 0;
                        }
                        return false;
                    }
                    depth--;
                    break;
                case '[':
                    depth--;
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
            pos--;
        }

        return false;
    }
}
