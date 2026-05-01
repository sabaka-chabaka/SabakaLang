using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Lexing;
using SemanticTokensLegend = SabakaLang.LanguageServer.Services.SemanticTokensLegend;
using SymbolKind = SabakaLang.Compiler.Binding.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class SemanticTokensHandler(DocumentStore store) : SemanticTokensHandlerBase
{
    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = SabakaDocumentSelector.Instance,
            Legend = new()
            {
                TokenTypes  = new Container<SemanticTokenType>(SemanticTokensLegend.TokenTypes.Select(t => new SemanticTokenType(t))),
                TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokensLegend.TokenModifiers.Select(m => new SemanticTokenModifier(m)))
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = true
        };

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params, CancellationToken ct)
    {
        var legend = new OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokensLegend
        {
            TokenTypes = new Container<SemanticTokenType>(SemanticTokensLegend.TokenTypes.Select(t => new SemanticTokenType(t))),
            TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokensLegend.TokenModifiers.Select(m => new SemanticTokenModifier(m)))
        };
        return Task.FromResult(new SemanticTokensDocument(legend));
    }

    protected override Task Tokenize(
        SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams @params,
        CancellationToken ct)
    {
        var analysis = store.Get(@params.TextDocument.Uri.ToString());
        if (analysis is null) return Task.CompletedTask;

        foreach (var token in analysis.Lexer.Tokens)
        {
            if (token.Type == TokenType.Eof) break;

            var (tokenType, modifier) = ClassifyToken(token, analysis);
            if (tokenType < 0) continue;

            builder.Push(
                PositionHelper.ToLsp(token.Start).Line,
                PositionHelper.ToLsp(token.Start).Character,
                token.Value.Length > 0 ? token.Value.Length : 1,
                tokenType,
                modifier);
        }

        return Task.CompletedTask;
    }

    private static (int type, int modifier) ClassifyToken(Token token, DocumentAnalysis analysis)
    {
        switch (token.Type)
        {
            case TokenType.Comment:
                return (SemanticTokensLegend.Comment, 0);

            case TokenType.StringLiteral:
            case TokenType.InterpolatedStringLiteral:
                return (SemanticTokensLegend.StringType, 0);

            case TokenType.IntLiteral:
            case TokenType.FloatLiteral:
                return (SemanticTokensLegend.Number, 0);

            case TokenType.If:
            case TokenType.Else:
            case TokenType.While:
            case TokenType.For:
            case TokenType.Foreach:
            case TokenType.In:
            case TokenType.Return:
            case TokenType.Class:
            case TokenType.Interface:
            case TokenType.StructKeyword:
            case TokenType.Enum:
            case TokenType.New:
            case TokenType.Override:
            case TokenType.Super:
            case TokenType.Null:
            case TokenType.Is:
            case TokenType.Switch:
            case TokenType.Case:
            case TokenType.Default:
            case TokenType.Import:
            case TokenType.Public:
            case TokenType.Private:
            case TokenType.Protected:
            case TokenType.BoolKeyword:
            case TokenType.IntKeyword:
            case TokenType.FloatKeyword:
            case TokenType.StringKeyword:
            case TokenType.VoidKeyword:
            case TokenType.True:
            case TokenType.False:
                return (SemanticTokensLegend.Keyword, 0);

            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.Percent:
            case TokenType.PlusEqual:
            case TokenType.MinusEqual:
            case TokenType.StarEqual:
            case TokenType.PlusPlus:
            case TokenType.MinusMinus:
            case TokenType.Equal:
            case TokenType.EqualEqual:
            case TokenType.NotEqual:
            case TokenType.Greater:
            case TokenType.Less:
            case TokenType.GreaterEqual:
            case TokenType.LessEqual:
            case TokenType.AndAnd:
            case TokenType.OrOr:
            case TokenType.Bang:
            case TokenType.Question:
            case TokenType.QuestionQuestion:
            case TokenType.Dot:
            case TokenType.Colon:
            case TokenType.ColonColon:
                return (SemanticTokensLegend.Operator, 0);

            case TokenType.Identifier:
                return ClassifyIdentifier(token.Value, analysis);

            default:
                return (-1, 0);
        }
    }

    private static (int type, int modifier) ClassifyIdentifier(
        string name, DocumentAnalysis analysis)
    {
        var syms = analysis.Bind.Symbols.Lookup(name).ToList();
        if (syms.Count == 0) return (SemanticTokensLegend.Variable, 0);

        var sym = syms.First();
        var mod = sym.Kind == SymbolKind.BuiltIn ? SemanticTokensLegend.ModDefaultLib : 0;

        var tokenType = sym.Kind switch
        {
            SymbolKind.Class     => SemanticTokensLegend.Class,
            SymbolKind.Interface => SemanticTokensLegend.Interface,
            SymbolKind.Struct    => SemanticTokensLegend.Struct,
            SymbolKind.Enum      => SemanticTokensLegend.Enum,
            SymbolKind.EnumMember=> SemanticTokensLegend.EnumMember,
            SymbolKind.Function  => SemanticTokensLegend.Function,
            SymbolKind.Method    => SemanticTokensLegend.Method,
            SymbolKind.BuiltIn   => SemanticTokensLegend.Function,
            SymbolKind.Parameter => SemanticTokensLegend.Parameter,
            SymbolKind.Field     => SemanticTokensLegend.Property,
            SymbolKind.TypeParam => SemanticTokensLegend.TypeParameter,
            SymbolKind.Module    => SemanticTokensLegend.Namespace,
            _                    => SemanticTokensLegend.Variable
        };

        return (tokenType, mod);
    }
}
