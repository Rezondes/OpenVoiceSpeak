using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 15 for methods: a method that returns a <c>Task</c> or <c>ValueTask</c> ends in <c>Async</c> (not a test method,
/// an override, an explicit interface member or <c>Main</c>), and a method name does not repeat its type's name (a warning).
/// </summary>
internal sealed class MethodNameRule : IRule
{
    /// <summary>The shortest type name the repetition check looks for (shorter names are common words).</summary>
    const int MinRepeatedNameLength = 4;

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var method in file.Syntax.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var name = method.Identifier.ValueText;
            if (ReturnsTask(method.ReturnType) && !name.EndsWith("Async", StringComparison.Ordinal) && !IsExempt(method))
            {
                yield return Finding.CreateAt(file, method.Identifier, "RL154", Severity.Error,
                    $"'{name}' returns a task; name it '{name}Async' (rule 15).", MemberNames.ReadEnclosing(method));
            }
            var typeName = method.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "";
            if (typeName.Length >= MinRepeatedNameLength && name.Contains(typeName, StringComparison.Ordinal))
            {
                yield return Finding.CreateAt(file, method.Identifier, "RL155", Severity.Warning,
                    $"'{name}' repeats the name of its type '{typeName}' (rule 15).", MemberNames.ReadEnclosing(method));
            }
        }
        foreach (var function in file.Syntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            var name = function.Identifier.ValueText;
            if (!ReturnsTask(function.ReturnType) || name.EndsWith("Async", StringComparison.Ordinal)) continue;
            yield return Finding.CreateAt(file, function.Identifier, "RL154", Severity.Error,
                $"'{name}' returns a task; name it '{name}Async' (rule 15).", MemberNames.ReadEnclosing(function) + "." + name);
        }
    }

    /// <summary>True for <c>Task</c>, <c>ValueTask</c> and their generic forms, qualified or not.</summary>
    public static bool ReturnsTask(TypeSyntax type)
    {
        var simple = type is QualifiedNameSyntax qualified ? qualified.Right : type;
        var name = simple switch
        {
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => "",
        };
        return name is "Task" or "ValueTask";
    }

    static bool IsExempt(MethodDeclarationSyntax method) =>
        method.Identifier.ValueText == "Main"
        || method.ExplicitInterfaceSpecifier is not null
        || method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.OverrideKeyword))
        || TestAttributes.IsTest(method);
}
