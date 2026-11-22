using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
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

	public sealed override FixAllProvider GetFixAllProvider() => CreateMethodsFixAllProvider.Instance;

	public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var diagnostic = context.Diagnostics.First();
		if (TryGetMethod(diagnostic, out var methodName, out var preImageType, out var postImageType))
		{
			var signature = SyntaxFactoryHelper.BuildSignatureDescription(preImageType != null, postImageType != null);
			context.RegisterCodeFix(
				CodeAction.Create(
					$"Create method '{methodName}({signature})'",
					c => CreateMethodsAsync(context.Document.Project.Solution, ImmutableArray.Create(diagnostic), c),
					nameof(CreateHandlerMethodCodeFixProvider)),
				diagnostic);
		}
		return Task.CompletedTask;
	}

	private static bool TryGetMethod(Diagnostic diagnostic, out string methodName, out string preImageType, out string postImageType)
	{
		diagnostic.Properties.TryGetValue(Constants.PropertyMethodName, out methodName);
		diagnostic.Properties.TryGetValue(Constants.PropertyPreImageType, out preImageType);
		diagnostic.Properties.TryGetValue(Constants.PropertyPostImageType, out postImageType);
		diagnostic.Properties.TryGetValue(Constants.PropertyHasPreImage, out var hasPreImage);
		diagnostic.Properties.TryGetValue(Constants.PropertyHasPostImage, out var hasPostImage);

		// Do not create an incomplete signature when an expected image name could not be resolved.
		return !string.IsNullOrEmpty(methodName) &&
			(!(bool.TryParse(hasPreImage, out var pre) && pre) || !string.IsNullOrEmpty(preImageType)) &&
			(!(bool.TryParse(hasPostImage, out var post) && post) || !string.IsNullOrEmpty(postImageType));
	}

	private static async Task<Solution> CreateMethodsAsync(
		Solution solution,
		ImmutableArray<Diagnostic> diagnostics,
		CancellationToken cancellationToken)
	{
		// Resolve against the original solution, then append all methods for each interface in one edit.
		// BatchFixer would merge independent insertions at the same position and drop some methods.
		var edits = new Dictionary<DocumentId, Dictionary<TextSpan, List<MethodDeclarationSyntax>>>();
		foreach (var diagnostic in diagnostics)
		{
			var tree = diagnostic.Location.SourceTree;
			if (tree == null || !TryGetMethod(diagnostic, out var methodName, out var preImageType, out var postImageType))
				continue;

			var document = solution.GetDocument(tree);
			if (document == null)
				continue;

			var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
			if (semanticModel == null)
				continue;

			var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
			var serviceType = RegisterStepHelper.ResolveServiceType(root, semanticModel, diagnostic.Location.SourceSpan, cancellationToken);
			if (serviceType == null)
				continue;

			var declaration = await FindInterfaceDeclarationAsync(solution, serviceType, cancellationToken).ConfigureAwait(false);
			if (declaration == null)
				continue;

			var target = solution.GetDocumentId(declaration.SyntaxTree);
			if (!edits.TryGetValue(target, out var interfaces))
				edits.Add(target, interfaces = new());
			if (!interfaces.TryGetValue(declaration.Span, out var methods))
				interfaces.Add(declaration.Span, methods = new());

			var method = CreateMethodDeclaration(methodName, preImageType, postImageType);
			if (!methods.Any(existing => existing.IsEquivalentTo(method)))
				methods.Add(method);
		}

		foreach (var edit in edits)
		{
			var root = await solution.GetDocument(edit.Key).GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
			var declarations = edit.Value.Keys.Select(span => (InterfaceDeclarationSyntax)root.FindNode(span));
			var newRoot = root.ReplaceNodes(declarations,
				(original, rewritten) => rewritten.AddMembers(edit.Value[original.Span].ToArray()));
			solution = solution.WithDocumentSyntaxRoot(edit.Key, newRoot);
		}

		return solution;
	}

	private sealed class CreateMethodsFixAllProvider : FixAllProvider
	{
		public static readonly CreateMethodsFixAllProvider Instance = new();

		public override async Task<CodeAction> GetFixAsync(FixAllContext context)
		{
			ImmutableArray<Diagnostic> diagnostics;
			switch (context.Scope)
			{
				case FixAllScope.Document when context.Document != null:
					diagnostics = await context.GetDocumentDiagnosticsAsync(context.Document).ConfigureAwait(false);
					break;
				case FixAllScope.Project:
					diagnostics = await context.GetAllDiagnosticsAsync(context.Project).ConfigureAwait(false);
					break;
				case FixAllScope.Solution:
					var all = ImmutableArray.CreateBuilder<Diagnostic>();
					foreach (var project in context.Solution.Projects)
						all.AddRange(await context.GetAllDiagnosticsAsync(project).ConfigureAwait(false));
					diagnostics = all.ToImmutable();
					break;
				default:
					return null;
			}

			return diagnostics.IsEmpty ? null : CodeAction.Create(
				"Create all missing image handlers",
				c => CreateMethodsAsync(context.Solution, diagnostics, c),
				context.CodeActionEquivalenceKey);
		}
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
