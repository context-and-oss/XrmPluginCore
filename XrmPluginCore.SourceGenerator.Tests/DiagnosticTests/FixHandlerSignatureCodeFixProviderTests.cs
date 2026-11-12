using FluentAssertions;
using XrmPluginCore.SourceGenerator.Analyzers;
using XrmPluginCore.SourceGenerator.CodeFixes;
using XrmPluginCore.SourceGenerator.Tests.Helpers;
using Xunit;

namespace XrmPluginCore.SourceGenerator.Tests.DiagnosticTests;

/// <summary>
/// Tests for FixHandlerSignatureCodeFixProvider that fixes handler method signatures to match
/// registered images. Image parameters are emitted with their fully-qualified generated wrapper type
/// names (which live in the plugin's own namespace), so no using directive is added.
/// </summary>
public class FixHandlerSignatureCodeFixProviderTests : CodeFixTestBase
{
	private const string Pre = "TestNamespace.TestPluginAccountUpdatePostOperationPreImage";
	private const string Post = "TestNamespace.TestPluginAccountUpdatePostOperationPostImage";

	[Fact]
	public async Task Should_Fix_Signature_For_PreImage()
	{
		var source = WrapPlugin("""
			                nameof(ITestService.HandleUpdate))
			                .AddFilteredAttributes(x => x.Name)
			                .WithPreImage(x => x.Name, x => x.Revenue);
			""", "void HandleUpdate();", "public void HandleUpdate() { }");

		var fixedSource = await ApplyCodeFixAsync(source);

		fixedSource.Should().Contain($"void HandleUpdate({Pre} preImage)");
		fixedSource.Should().NotContain("using TestNamespace.PluginRegistrations");
	}

	[Fact]
	public async Task Should_Fix_Signature_For_PostImage()
	{
		var source = WrapPlugin("""
			                nameof(ITestService.HandleUpdate))
			                .AddFilteredAttributes(x => x.Name)
			                .WithPostImage(x => x.Name, x => x.AccountNumber);
			""", "void HandleUpdate();", "public void HandleUpdate() { }");

		var fixedSource = await ApplyCodeFixAsync(source);

		fixedSource.Should().Contain($"void HandleUpdate({Post} postImage)");
	}

	[Fact]
	public async Task Should_Fix_Signature_For_Both_Images()
	{
		var source = WrapPlugin("""
			                nameof(ITestService.HandleUpdate))
			                .AddFilteredAttributes(x => x.Name)
			                .WithPreImage(x => x.Name, x => x.Revenue)
			                .WithPostImage(x => x.Name, x => x.AccountNumber);
			""", "void HandleUpdate();", "public void HandleUpdate() { }");

		var fixedSource = await ApplyCodeFixAsync(source);

		fixedSource.Should().Contain($"void HandleUpdate({Pre} preImage, {Post} postImage)");
	}

	[Fact]
	public async Task FixAll_Should_Fix_Multiple_Registrations()
	{
		// Two registrations on the same service, both with mismatched (empty) handler signatures.
		const string source = """
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleUpdate))
			                .WithPreImage(x => x.Name);

			            RegisterStep<Account, ITestService>(EventOperation.Delete, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleDelete))
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<ITestService, TestService>();
			    }

			    public interface ITestService
			    {
			        void HandleUpdate();
			        void HandleDelete();
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate() { }
			        public void HandleDelete() { }
			    }
			}
			""";

		var fixedSource = await ApplyFixAllAsync(source);

		// Both handlers fixed on interface + implementation, each with its own registration-derived type.
		CountOccurrences(fixedSource, "void HandleUpdate(TestNamespace.TestPluginAccountUpdatePostOperationPreImage preImage)").Should().Be(2);
		CountOccurrences(fixedSource, "void HandleDelete(TestNamespace.TestPluginAccountDeletePostOperationPreImage preImage)").Should().Be(2);
	}

	private static string WrapPlugin(string registrationTail, string interfaceMethod, string implMethod) =>
		$$"""
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			{{registrationTail}}
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<ITestService, TestService>();
			    }

			    public interface ITestService
			    {
			        {{interfaceMethod}}
			    }

			    public class TestService : ITestService
			    {
			        {{implMethod}}
			    }
			}
			""";

	private static int CountOccurrences(string source, string search)
	{
		var count = 0;
		var index = 0;
		while ((index = source.IndexOf(search, index, StringComparison.Ordinal)) != -1)
		{
			count++;
			index += search.Length;
		}
		return count;
	}

	private static Task<string> ApplyCodeFixAsync(string source)
		=> ApplyCodeFixAsync(source, new HandlerSignatureMismatchAnalyzer(), new FixHandlerSignatureCodeFixProvider(),
			DiagnosticDescriptors.HandlerSignatureMismatch.Id, DiagnosticDescriptors.HandlerSignatureMismatchError.Id);

	private static Task<string> ApplyFixAllAsync(string source)
		=> ApplyFixAllAsync(source, new HandlerSignatureMismatchAnalyzer(), new FixHandlerSignatureCodeFixProvider(),
			nameof(FixHandlerSignatureCodeFixProvider),
			DiagnosticDescriptors.HandlerSignatureMismatch.Id, DiagnosticDescriptors.HandlerSignatureMismatchError.Id);
}
