using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using XrmPluginCore.SourceGenerator.Tests.Helpers;

namespace XrmPluginCore.SourceGenerator.Tests.DiagnosticTests;

public abstract class CodeFixTestBase
{
	protected static Document CreateDocument(string source)
	{
		var projectId = ProjectId.CreateNewId();
		var documentId = DocumentId.CreateNewId(projectId);

		var references = new[]
		{
			MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Plugin).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IPluginDefinition).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Microsoft.Xrm.Sdk.Entity).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly.Location),
			MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
			MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location),
			MetadataReference.CreateFromFile(typeof(System.ComponentModel.DescriptionAttribute).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(XrmPluginCore.Tests.Context.BusinessDomain.Account).Assembly.Location),
		};

		var solution = new AdhocWorkspace().CurrentSolution
			.AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
			.WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.CSharp11))
			.AddMetadataReferences(projectId, references)
			.AddDocument(documentId, "Test.cs", source);

		return solution.GetDocument(documentId)!;
	}

	protected static async Task<string> ApplyCodeFixAsync(
		string source,
		DiagnosticAnalyzer analyzer,
		CodeFixProvider codeFixProvider,
		params string[] diagnosticIds)
	{
		var document = CreateDocument(source);
		var compilation = (await document.Project.GetCompilationAsync())!;

		var compilationWithAnalyzers = compilation.WithAnalyzers(
			[analyzer]);

		var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
		var diagnostic = diagnostics.FirstOrDefault(d => diagnosticIds.Contains(d.Id));

		if (diagnostic == null)
		{
			return source;
		}

		var actions = new List<CodeAction>();

		var context = new CodeFixContext(
			document,
			diagnostic,
			(action, _) => actions.Add(action),
			CancellationToken.None);

		await codeFixProvider.RegisterCodeFixesAsync(context);

		if (actions.Count == 0)
		{
			return source;
		}

		var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
		var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
		var changedDocument = changedSolution.GetDocument(document.Id);
		var newText = await changedDocument!.GetTextAsync();

		return newText.ToString();
	}

	/// <summary>
	/// Applies the code fix provider's FixAll provider across every matching diagnostic in the
	/// document (Document scope), exercising the custom <c>FixAllProvider</c> rather than a single
	/// per-diagnostic fix.
	/// </summary>
	protected static async Task<string> ApplyFixAllAsync(
		string source,
		DiagnosticAnalyzer analyzer,
		CodeFixProvider codeFixProvider,
		string equivalenceKey,
		params string[] diagnosticIds)
	{
		var document = CreateDocument(source);

		// Compute diagnostics against the document's own compilation so their locations reference the
		// document's syntax tree (the FixAll provider maps locations back to documents).
		var compilation = await document.Project.GetCompilationAsync();
		var compilationWithAnalyzers = compilation!.WithAnalyzers([analyzer]);
		var analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
		var diagnostics = analyzerDiagnostics.Where(d => diagnosticIds.Contains(d.Id)).ToImmutableArray();

		if (diagnostics.IsEmpty)
		{
			return source;
		}

		var fixAllProvider = codeFixProvider.GetFixAllProvider()!;
		var fixAllContext = new FixAllContext(
			document,
			codeFixProvider,
			FixAllScope.Document,
			equivalenceKey,
			diagnosticIds,
			new FixAllDiagnosticProvider(diagnostics),
			CancellationToken.None);

		var codeAction = await fixAllProvider.GetFixAsync(fixAllContext);
		if (codeAction == null)
		{
			return source;
		}

		var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
		var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
		var changedDocument = changedSolution.GetDocument(document.Id);
		var newText = await changedDocument!.GetTextAsync();

		return newText.ToString();
	}

	private sealed class FixAllDiagnosticProvider : FixAllContext.DiagnosticProvider
	{
		private readonly ImmutableArray<Diagnostic> _diagnostics;

		public FixAllDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
		{
			_diagnostics = diagnostics;
		}

		public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
			=> Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);

		public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
			=> Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);

		public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
			=> Task.FromResult<IEnumerable<Diagnostic>>(Enumerable.Empty<Diagnostic>());
	}

	protected static async Task<List<CodeAction>> GetCodeActionsAsync(
		string source,
		DiagnosticAnalyzer analyzer,
		CodeFixProvider codeFixProvider,
		params string[] diagnosticIds)
	{
		var compilation = CompilationHelper.CreateCompilation(source);

		var compilationWithAnalyzers = compilation.WithAnalyzers(
			[analyzer]);

		var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
		var diagnostic = diagnostics.FirstOrDefault(d => diagnosticIds.Contains(d.Id));

		if (diagnostic == null)
		{
			return [];
		}

		var document = CreateDocument(source);
		var actions = new List<CodeAction>();

		var context = new CodeFixContext(
			document,
			diagnostic,
			(action, _) => actions.Add(action),
			CancellationToken.None);

		await codeFixProvider.RegisterCodeFixesAsync(context);

		return actions;
	}
}
