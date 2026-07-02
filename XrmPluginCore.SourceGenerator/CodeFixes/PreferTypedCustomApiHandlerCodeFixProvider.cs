using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.CodeFixes;

/// <summary>
/// Converts an action-based Custom API handler (e.g. <c>s =&gt; s.Handle()</c>) to the type-safe
/// <c>nameof(TService.Handle)</c> form so the source generator emits strongly-typed request/response DTOs.
/// The follow-up signature adjustment is then guided by XPC4005/XPC4006 and their code fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PreferTypedCustomApiHandlerCodeFixProvider)), Shared]
public class PreferTypedCustomApiHandlerCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(DiagnosticDescriptors.PreferTypedCustomApiHandler.Id);

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

		// Only offer the fix when the analyzer could extract the handler method name from the lambda.
		if (!diagnostic.Properties.TryGetValue(Constants.PropertyServiceType, out var serviceType) ||
			!diagnostic.Properties.TryGetValue(Constants.PropertyMethodName, out var methodName) ||
			string.IsNullOrEmpty(methodName))
		{
			return;
		}

		var node = root.FindNode(diagnostic.Location.SourceSpan);
		var actionArgument = node as ExpressionSyntax
			?? node.DescendantNodesAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
		if (actionArgument == null)
		{
			return;
		}

		context.RegisterCodeFix(
			CodeAction.Create(
				title: $"Use nameof({serviceType}.{methodName})",
				createChangedDocument: c => ConvertToNameofAsync(context.Document, actionArgument, serviceType, methodName, c),
				equivalenceKey: nameof(PreferTypedCustomApiHandlerCodeFixProvider)),
			diagnostic);
	}

	private static async Task<Document> ConvertToNameofAsync(
		Document document,
		ExpressionSyntax actionArgument,
		string serviceType,
		string methodName,
		CancellationToken cancellationToken)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null)
		{
			return document;
		}

		var nameofExpression = SyntaxFactoryHelper.CreateNameofExpression(serviceType, methodName)
			.WithTriviaFrom(actionArgument);

		var newRoot = root.ReplaceNode(actionArgument, nameofExpression);
		return document.WithSyntaxRoot(newRoot);
	}
}
