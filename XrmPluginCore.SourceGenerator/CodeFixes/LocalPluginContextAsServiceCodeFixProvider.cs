using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.CodeFixes;

/// <summary>
/// Code fix provider that rewrites RegisterStep&lt;TEntity, LocalPluginContext&gt; to RegisterPluginStep&lt;TEntity&gt;.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalPluginContextAsServiceCodeFixProvider)), Shared]
public class LocalPluginContextAsServiceCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(DiagnosticDescriptors.LocalPluginContextAsService.Id);

	public sealed override FixAllProvider GetFixAllProvider() =>
		WellKnownFixAllProviders.BatchFixer;

	public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null)
		{
			return;
		}

		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;
		var invocationNode = root.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();

		if (invocationNode == null)
		{
			return;
		}

		if (!RegisterStepHelper.IsRegisterStepCall(invocationNode, out var genericName))
		{
			return;
		}

		var entityTypeName = genericName.TypeArgumentList.Arguments[0].ToString();

		context.RegisterCodeFix(
			CodeAction.Create(
				title: $"Use RegisterPluginStep<{entityTypeName}> instead",
				createChangedDocument: c => ReplaceWithRegisterPluginStepAsync(context.Document, invocationNode, c),
				equivalenceKey: nameof(LocalPluginContextAsServiceCodeFixProvider)),
			diagnostic);
	}

	private static async Task<Document> ReplaceWithRegisterPluginStepAsync(
		Document document,
		InvocationExpressionSyntax invocationNode,
		CancellationToken cancellationToken)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null)
		{
			return document;
		}

		if (!RegisterStepHelper.IsRegisterStepCall(invocationNode, out var genericName))
		{
			return document;
		}

		// Build new type argument list with only the first type arg (TEntity)
		var entityTypeArg = genericName.TypeArgumentList.Arguments[0]
			.WithoutTrivia();
		var newTypeArgList = SyntaxFactory.TypeArgumentList(
			SyntaxFactory.SingletonSeparatedList(entityTypeArg));

		// Build new generic name: RegisterPluginStep<TEntity>
		var newGenericName = SyntaxFactory.GenericName(
			SyntaxFactory.Identifier(Constants.RegisterPluginStepMethodName),
			newTypeArgList);

		ExpressionSyntax newExpression;
		if (invocationNode.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			newExpression = memberAccess.WithName(newGenericName);
		}
		else
		{
			newExpression = newGenericName;
		}

		var newInvocation = invocationNode.WithExpression(newExpression);
		var newRoot = root.ReplaceNode(invocationNode, newInvocation);
		return document.WithSyntaxRoot(newRoot);
	}
}
