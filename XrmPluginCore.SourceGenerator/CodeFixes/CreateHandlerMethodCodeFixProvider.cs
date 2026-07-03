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
/// Code fix provider that creates a missing handler method on a service interface. Image parameters are
/// emitted with their fully-qualified generated wrapper type names, so no using directive is needed.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CreateHandlerMethodCodeFixProvider)), Shared]
public class CreateHandlerMethodCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(DiagnosticDescriptors.HandlerMethodNotFound.Id);

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

		// Get properties from diagnostic
		if (!diagnostic.Properties.TryGetValue(Constants.PropertyServiceType, out var serviceType) ||
			!diagnostic.Properties.TryGetValue(Constants.PropertyMethodName, out var methodName))
		{
			return;
		}

		diagnostic.Properties.TryGetValue(Constants.PropertyPreImageType, out var preImageType);
		diagnostic.Properties.TryGetValue(Constants.PropertyPostImageType, out var postImageType);

		// The analyzer reports HasPreImage/HasPostImage unconditionally but only supplies the generated
		// wrapper type name when it can resolve it. If an image is registered but its type name is
		// missing, we can't emit a correct signature — don't offer a fix that would create a
		// permanently mismatched, param-less method.
		diagnostic.Properties.TryGetValue(Constants.PropertyHasPreImage, out var expectsPreImageStr);
		diagnostic.Properties.TryGetValue(Constants.PropertyHasPostImage, out var expectsPostImageStr);
		var expectsPreImage = bool.TryParse(expectsPreImageStr, out var ep) && ep;
		var expectsPostImage = bool.TryParse(expectsPostImageStr, out var eq) && eq;
		if ((expectsPreImage && string.IsNullOrEmpty(preImageType)) ||
			(expectsPostImage && string.IsNullOrEmpty(postImageType)))
		{
			return;
		}

		var hasPreImage = !string.IsNullOrEmpty(preImageType);
		var hasPostImage = !string.IsNullOrEmpty(postImageType);

		// Build the title showing expected signature
		var signatureDescription = SyntaxFactoryHelper.BuildSignatureDescription(hasPreImage, hasPostImage);
		var title = $"Create method '{methodName}({signatureDescription})'";

		context.RegisterCodeFix(
			CodeAction.Create(
				title: title,
				createChangedSolution: c => CreateMethodAsync(context.Document, diagnostic, methodName!, preImageType, postImageType, c),
				equivalenceKey: nameof(CreateHandlerMethodCodeFixProvider)),
			diagnostic);
	}

	private static async Task<Solution> CreateMethodAsync(
		Document document,
		Diagnostic diagnostic,
		string methodName,
		string preImageType,
		string postImageType,
		CancellationToken cancellationToken)
	{
		var solution = document.Project.Solution;

		var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
		if (semanticModel == null)
		{
			return solution;
		}

		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null)
		{
			return solution;
		}

		var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);
		var registerStepInvocation = diagnosticNode.AncestorsAndSelf()
			.OfType<InvocationExpressionSyntax>()
			.FirstOrDefault(i => RegisterStepHelper.IsRegisterStepCall(i, out _));

		if (registerStepInvocation == null)
		{
			return solution;
		}

		var genericName = RegisterStepHelper.GetGenericName(registerStepInvocation);
		if (genericName == null || genericName.TypeArgumentList.Arguments.Count < 2)
		{
			return solution;
		}

		var serviceTypeSyntax = genericName.TypeArgumentList.Arguments[1];
		var typeInfo = semanticModel.GetTypeInfo(serviceTypeSyntax, cancellationToken);
		if (typeInfo.Type is not INamedTypeSymbol serviceTypeSymbol)
		{
			return solution;
		}

		var interfaceDeclaration = await FindInterfaceDeclarationAsync(solution, serviceTypeSymbol, cancellationToken);
		if (interfaceDeclaration == null)
		{
			return solution;
		}

		var interfaceDocument = solution.GetDocument(interfaceDeclaration.SyntaxTree);
		if (interfaceDocument == null)
		{
			return solution;
		}

		var interfaceRoot = await interfaceDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (interfaceRoot == null)
		{
			return solution;
		}

		var methodDeclaration = CreateMethodDeclaration(methodName, preImageType, postImageType);

		// The generated wrapper types are referenced by fully-qualified name, so the method can be added
		// directly with no using-directive juggling.
		var newRoot = interfaceRoot.ReplaceNode(interfaceDeclaration, interfaceDeclaration.AddMembers(methodDeclaration));

		return solution.WithDocumentSyntaxRoot(interfaceDocument.Id, newRoot);
	}

	internal static MethodDeclarationSyntax CreateMethodDeclaration(string methodName, string preImageType, string postImageType)
	{
		return SyntaxFactory.MethodDeclaration(
				SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
				SyntaxFactory.Identifier(methodName))
			.WithParameterList(SyntaxFactoryHelper.CreateImageParameterList(preImageType, postImageType))
			.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
			.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed, SyntaxFactory.ElasticTab)
			.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
	}

	internal static async Task<InterfaceDeclarationSyntax> FindInterfaceDeclarationAsync(
		Solution solution,
		INamedTypeSymbol typeSymbol,
		CancellationToken cancellationToken)
	{
		foreach (var location in typeSymbol.Locations)
		{
			if (!location.IsInSource)
			{
				continue;
			}

			var tree = location.SourceTree;
			if (tree == null)
			{
				continue;
			}

			var document = solution.GetDocument(tree);
			if (document == null)
			{
				continue;
			}

			var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
			var node = root.FindNode(location.SourceSpan);

			var interfaceDeclaration = node.AncestorsAndSelf()
				.OfType<InterfaceDeclarationSyntax>()
				.FirstOrDefault();

			if (interfaceDeclaration != null)
			{
				return interfaceDeclaration;
			}
		}

		return null;
	}
}
