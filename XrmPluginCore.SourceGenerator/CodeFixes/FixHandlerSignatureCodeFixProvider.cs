using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.CodeFixes;

/// <summary>
/// Code fix provider that fixes handler method signatures to match registered images. Image parameters
/// are emitted with their fully-qualified generated wrapper type names, so no using directive is needed.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FixHandlerSignatureCodeFixProvider)), Shared]
public class FixHandlerSignatureCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(
			DiagnosticDescriptors.HandlerSignatureMismatch.Id,
			DiagnosticDescriptors.HandlerSignatureMismatchError.Id);

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

		var hasPreImage = !string.IsNullOrEmpty(preImageType);
		var hasPostImage = !string.IsNullOrEmpty(postImageType);

		// Build the title showing expected signature
		var signatureDescription = SyntaxFactoryHelper.BuildSignatureDescription(hasPreImage, hasPostImage, includeParameterNames: true);
		var title = $"Fix signature to '{methodName}({signatureDescription})'";

		context.RegisterCodeFix(
			CodeAction.Create(
				title: title,
				createChangedSolution: c => FixSignatureAsync(context.Document, diagnostic, methodName!, preImageType, postImageType, c),
				equivalenceKey: nameof(FixHandlerSignatureCodeFixProvider)),
			diagnostic);
	}

	private static async Task<Solution> FixSignatureAsync(
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

		return await FixMethodDeclarationsAsync(solution, semanticModel.Compilation, serviceTypeSymbol, methodName, preImageType, postImageType, cancellationToken);
	}

	private static async Task<Solution> FixMethodDeclarationsAsync(
		Solution solution,
		Compilation compilation,
		INamedTypeSymbol serviceType,
		string methodName,
		string preImageType,
		string postImageType,
		CancellationToken cancellationToken)
	{
		// Collect ALL methods to fix (interface + implementations)
		var allMethods = new List<IMethodSymbol>(TypeHelper.GetAllMethodsIncludingInherited(serviceType, methodName));

		if (serviceType.TypeKind == TypeKind.Interface)
		{
			allMethods.AddRange(TypeHelper.FindImplementingMethods(compilation, serviceType, methodName));
		}

		// Group method locations by document (syntax tree)
		var locationsByTree = new Dictionary<SyntaxTree, List<Location>>();
		foreach (var method in allMethods)
		{
			foreach (var location in method.Locations)
			{
				if (!location.IsInSource || location.SourceTree == null)
				{
					continue;
				}

				if (!locationsByTree.TryGetValue(location.SourceTree, out var list))
				{
					list = new List<Location>();
					locationsByTree[location.SourceTree] = list;
				}

				list.Add(location);
			}
		}

		var newParameters = SyntaxFactoryHelper.CreateImageParameterList(preImageType, postImageType);

		foreach (var kvp in locationsByTree)
		{
			var tree = kvp.Key;

			var methodDocument = solution.GetDocument(tree);
			if (methodDocument == null)
			{
				continue;
			}

			var methodRoot = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

			// Find all target method declarations in this document
			var methodDeclarations = new List<MethodDeclarationSyntax>();
			foreach (var location in kvp.Value)
			{
				var methodDecl = methodRoot.FindNode(location.SourceSpan)
					.AncestorsAndSelf()
					.OfType<MethodDeclarationSyntax>()
					.FirstOrDefault();

				if (methodDecl != null && !methodDeclarations.Contains(methodDecl))
				{
					methodDeclarations.Add(methodDecl);
				}
			}

			if (methodDeclarations.Count == 0)
			{
				continue;
			}

			var newRoot = methodRoot.ReplaceNodes(
				methodDeclarations,
				(original, _) => original.WithParameterList(newParameters));

			solution = solution.WithDocumentSyntaxRoot(methodDocument.Id, newRoot);
		}

		return solution;
	}
}
