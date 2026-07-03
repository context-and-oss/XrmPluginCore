using FluentAssertions;
using XrmPluginCore.SourceGenerator.Analyzers;
using XrmPluginCore.SourceGenerator.CodeFixes;
using XrmPluginCore.SourceGenerator.Tests.Helpers;
using Xunit;

namespace XrmPluginCore.SourceGenerator.Tests.DiagnosticTests;

/// <summary>
/// Tests for CreateHandlerMethodCodeFixProvider that creates missing handler methods. Image parameters
/// are emitted with their fully-qualified generated wrapper type names (which live in the plugin's own
/// namespace), so no using directive is added.
/// </summary>
public class CreateHandlerMethodCodeFixProviderTests : CodeFixTestBase
{
	private const string Pre = "TestNamespace.TestPluginAccountUpdatePostOperationPreImage";
	private const string Post = "TestNamespace.TestPluginAccountUpdatePostOperationPostImage";

	[Fact]
	public async Task Should_Create_Method_For_PreImage()
	{
		var source = WrapPlugin("""
			                .AddFilteredAttributes(x => x.Name)
			                .WithPreImage(x => x.Name, x => x.Revenue);
			""");

		var fixedSource = await ApplyCodeFixAsync(source);

		fixedSource.Should().Contain($"void HandleUpdate({Pre} preImage)");
		fixedSource.Should().NotContain("using TestNamespace.PluginRegistrations");
	}

	[Fact]
	public async Task Should_Create_Method_For_Both_Images()
	{
		var source = WrapPlugin("""
			                .AddFilteredAttributes(x => x.Name)
			                .WithPreImage(x => x.Name, x => x.Revenue)
			                .WithPostImage(x => x.Name, x => x.AccountNumber);
			""");

		var fixedSource = await ApplyCodeFixAsync(source);

		fixedSource.Should().Contain($"void HandleUpdate({Pre} preImage, {Post} postImage)");
	}

	[Fact]
	public async Task Should_Create_Method_Without_Images()
	{
		var source = WrapPlugin("""
			                .AddFilteredAttributes(x => x.Name);
			""");

		var fixedSource = await ApplyCodeFixAsync(source);

		fixedSource.Should().Contain("void HandleUpdate()");
		fixedSource.Should().NotContain("using TestNamespace.PluginRegistrations");
	}

	[Fact]
	public async Task FixAll_Should_Create_Methods_Across_Services()
	{
		// Two registrations, each on its own service, both missing their handler method.
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
			            RegisterStep<Account, IUpdateService>(EventOperation.Update, ExecutionStage.PostOperation,
			                "HandleUpdate")
			                .WithPreImage(x => x.Name);

			            RegisterStep<Account, IDeleteService>(EventOperation.Delete, ExecutionStage.PostOperation,
			                "HandleDelete")
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<IUpdateService, UpdateService>().AddScoped<IDeleteService, DeleteService>();
			    }

			    public interface IUpdateService { }
			    public interface IDeleteService { }

			    public class UpdateService : IUpdateService { }
			    public class DeleteService : IDeleteService { }
			}
			""";

		var fixedSource = await ApplyFixAllAsync(source);

		fixedSource.Should().Contain("void HandleUpdate(TestNamespace.TestPluginAccountUpdatePostOperationPreImage preImage)");
		fixedSource.Should().Contain("void HandleDelete(TestNamespace.TestPluginAccountDeletePostOperationPreImage preImage)");
	}

	[Fact]
	public async Task Should_Add_Method_To_Correct_Interface_When_Names_Collide()
	{
		// A decoy interface with the SAME simple name lives in another namespace and is declared FIRST.
		// The registered service is TestNamespace.ITestService; the fix must target it, not the decoy.
		const string source = """
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace DecoyNamespace
			{
			    public interface ITestService
			    {
			        void SomethingElse();
			    }
			}

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                "HandleUpdate")
			                .WithPreImage(x => x.Name, x => x.Revenue);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<ITestService, TestService>();
			    }

			    public interface ITestService
			    {
			    }

			    public class TestService : ITestService
			    {
			    }
			}
			""";

		var fixedSource = await ApplyCodeFixAsync(source);

		var compilation = CompilationHelper.CreateCompilation(fixedSource);
		var registered = compilation.GetTypeByMetadataName("TestNamespace.ITestService");
		var decoy = compilation.GetTypeByMetadataName("DecoyNamespace.ITestService");

		registered.Should().NotBeNull();
		decoy.Should().NotBeNull();
		registered!.GetMembers("HandleUpdate").Should().HaveCount(1, "the method must be added to the registered interface");
		decoy!.GetMembers("HandleUpdate").Should().BeEmpty("the method must not be added to the same-named decoy interface");
	}

	private static string WrapPlugin(string registrationTail) =>
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
			                "HandleUpdate")
			{{registrationTail}}
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			            => services.AddScoped<ITestService, TestService>();
			    }

			    public interface ITestService
			    {
			    }

			    public class TestService : ITestService
			    {
			    }
			}
			""";

	private static Task<string> ApplyCodeFixAsync(string source)
		=> ApplyCodeFixAsync(source, new HandlerMethodNotFoundAnalyzer(), new CreateHandlerMethodCodeFixProvider(),
			DiagnosticDescriptors.HandlerMethodNotFound.Id);

	private static Task<string> ApplyFixAllAsync(string source)
		=> ApplyFixAllAsync(source, new HandlerMethodNotFoundAnalyzer(), new CreateHandlerMethodCodeFixProvider(),
			nameof(CreateHandlerMethodCodeFixProvider),
			DiagnosticDescriptors.HandlerMethodNotFound.Id);
}
