using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MapleForge.Analyzers;

/// <summary>
/// MF0001：禁止 Maple.Core / Maple.Application 中的 static 可變欄位或屬性。
/// 違規 = 非多實例友善（static 單例是多實例架構的頭號天敵）。
/// 豁免：const、readonly、第三方套件（不在受保護命名空間內）。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticMutableFieldAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MF0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "禁止在 Core/Application 使用 static 可變欄位或屬性",
        messageFormat: "'{0}' 是 static 可變成員，違反多實例架構規則（請改用 DI 注入或 readonly）",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Maple.Core 與 Maple.Application 必須零全域可變狀態，以支援多個 ServerInstance 同時運行。請改用建構子注入（DI）或 readonly 欄位。"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeField,    SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeField(SyntaxNodeAnalysisContext ctx)
    {
        var field = (FieldDeclarationSyntax)ctx.Node;

        // 只審查 static 欄位
        if (!HasModifier(field.Modifiers, SyntaxKind.StaticKeyword)) return;

        // const 和 readonly 豁免
        if (HasModifier(field.Modifiers, SyntaxKind.ConstKeyword))    return;
        if (HasModifier(field.Modifiers, SyntaxKind.ReadOnlyKeyword)) return;

        if (!IsInProtectedNamespace(field, ctx.SemanticModel)) return;

        foreach (var variable in field.Declaration.Variables)
        {
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable);
            if (symbol is null) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Rule, variable.GetLocation(), symbol.Name));
        }
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext ctx)
    {
        var prop = (PropertyDeclarationSyntax)ctx.Node;

        // 只審查 static 屬性
        if (!HasModifier(prop.Modifiers, SyntaxKind.StaticKeyword)) return;

        // get-only (無 setter) 豁免
        if (prop.AccessorList is not null)
        {
            bool hasSetter = false;
            foreach (var accessor in prop.AccessorList.Accessors)
                if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                    accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
                    hasSetter = true;
            if (!hasSetter) return;
        }
        else
        {
            // expression-body property (get only) → 豁免
            return;
        }

        if (!IsInProtectedNamespace(prop, ctx.SemanticModel)) return;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(prop);
        if (symbol is null) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Rule, prop.Identifier.GetLocation(), symbol.Name));
    }

    private static bool HasModifier(SyntaxTokenList modifiers, SyntaxKind kind)
    {
        foreach (var m in modifiers)
            if (m.IsKind(kind)) return true;
        return false;
    }

    private static bool IsInProtectedNamespace(SyntaxNode node, SemanticModel model)
    {
        // 往上找最近的型別宣告，取 namespace
        var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is null) return false;

        var symbol = model.GetDeclaredSymbol(typeDecl);
        if (symbol?.ContainingNamespace is null) return false;

        var ns = symbol.ContainingNamespace.ToDisplayString();
        return ns.StartsWith("Maple.Core", System.StringComparison.Ordinal)
            || ns.StartsWith("Maple.Application", System.StringComparison.Ordinal);
    }
}
