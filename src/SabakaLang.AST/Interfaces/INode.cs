using SabakaLang.AST.Structures;

namespace SabakaLang.AST.Interfaces;

public interface INode
{
    Span Span { get; }
}