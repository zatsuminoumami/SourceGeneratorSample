﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GeneratorCs;

[Generator(LanguageNames.CSharp)]
public partial class SampleGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Generatorの初期設定
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // PostInitializationOutputでSource Generatorでしか使わない属性を出力
        context.RegisterPostInitializationOutput(static context =>
        {
            // C# 11のRaw String Literal便利
            // 現状出力されている「カスタムアトリビュート実装ソース」
            context.AddSource("SampleGeneratorAttribute.cs", """
namespace SourceGeneratorSample;

using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class GenerateToStringAttribute : Attribute
{
}
""");
        });

        var source = context.SyntaxProvider.ForAttributeWithMetadataName(
            "SourceGeneratorSample.GenerateToStringAttribute", // 引っ掛ける属性のフルネーム
            static (node, token) => true, // predicate, 属性で既に絞れてるので特別何かやりたいことがなければ基本true
            static (context, token) => context); // GeneratorAttributeSyntaxContextにはNode, SemanticModel(Compilation), Symbolが入ってて便利

        // 出力コード部分はちょっとごちゃつくので別メソッドに隔離
        context.RegisterSourceOutput(source, Emit);
    }
    
    /// <summary>
    /// 対象のクラスに対してToStringメソッドを生成
    /// </summary>
    static void Emit(SourceProductionContext context, GeneratorAttributeSyntaxContext source)
    {
        // classで引っ掛けてるのでTypeSymbol/Syntaxとして使えるように。
        // SemaintiModelが欲しい場合は source.SemanticModel
        // Compilationが欲しい場合は source.SemanticModel.Compilation から
        var typeSymbol = (INamedTypeSymbol)source.TargetSymbol;
        var typeNode = (TypeDeclarationSyntax)source.TargetNode;

        // ToStringがoverride済みならエラー出す
        if (typeSymbol.GetMembers("ToString").Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ExistsOverrideToString, typeNode.Identifier.GetLocation(), typeSymbol.Name));
            return;
        }

        // グローバルネームスペース対応漏れするとたまによく泣くので気をつける
        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : $"namespace {typeSymbol.ContainingNamespace};";

        // 出力ファイル名として使うので雑エスケープ
        var fullType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_");

        // Field/Propertyを抽出する
        var publicMembers = typeSymbol.GetMembers() // MethodがほしければOfType<IMethodSymbol>()などで絞る
            .Where(x => x is (IFieldSymbol or IPropertySymbol)
                         and { IsStatic: false, DeclaredAccessibility: Accessibility.Public, IsImplicitlyDeclared: false, CanBeReferencedByName: true })
            .Select(x => $"{x.Name}:{{{x.Name}}}"); // MyProperty:{MyProperty}

        var toString = string.Join(", ", publicMembers);

        // C# 11のRaw String Literalを使ってText Template的な置換(便利)
        // ファイルとして書き出される時対策として <auto-generated/> を入れたり
        // nullable enableしつつ、nullable系のwarningがウザいのでdisableして回ったりなどをテンプレコードとして入れておいたりする
        // 現状出力されてる「ToStringを実装したやつ」
        var code = $$"""
// <auto-generated/>
#nullable enable
#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604

{{ns}}

partial class {{typeSymbol.Name}}
{
    public override string ToString()
    {
        return $"{{toString}}";
    }
}
""";

        // AddSourceで出力
        context.AddSource($"{fullType}.SampleGenerator.g.cs", code);
    }
}

// DiagnosticDescriptorは大量に作るので一覧性のためにもまとめておいたほうが良い
public static class DiagnosticDescriptors
{
    const string Category = "SampleGenerator";

    public static readonly DiagnosticDescriptor ExistsOverrideToString = new(
        id: "SAMPLE001",
        title: "ToString override",
        messageFormat: "The GenerateToString class '{0}' has ToString override but it is not allowed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}