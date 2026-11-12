using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using XrmPluginCore.SourceGenerator.Analyzers;
using XrmPluginCore.SourceGenerator.Tests.Helpers;
using Xunit;

namespace XrmPluginCore.SourceGenerator.Tests.DiagnosticTests;

/// <summary>
/// Tests for verifying diagnostic reporting from the source generator and analyzers.
/// </summary>
public class DiagnosticReportingTests
{
	[Fact]
	public void Should_Not_Report_XPC1000_Success_Diagnostic_On_Successful_Generation()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.GetPluginWithPreImage());

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - XPC1001 is no longer reported to avoid spamming the user
		var successDiagnostics = result.GeneratorDiagnostics
			.Where(d => d.Id == "XPC1001")
			.ToArray();

		successDiagnostics.Should().BeEmpty("XPC1001 success diagnostic should not be reported to avoid spam");
	}

	[Fact]
	public async Task Should_Report_XPC2001_When_Plugin_Has_No_Parameterless_Constructor()
	{
		// Arrange - plugin class with only a parameterized constructor (no parameterless)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;
			using TestNamespace.PluginRegistrations.TestPlugin.AccountUpdatePostOperation;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        // Only has a constructor WITH parameters - no parameterless constructor
			        public TestPlugin(string config)
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.Process)
			                .AddImage(ImageType.PreImage, x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService { void Process(PreImage preImage); }
			    public class TestService : ITestService { public void Process(PreImage preImage) { } }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new NoParameterlessConstructorAnalyzer());

		// Assert - should report XPC2001
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC2001")
			.ToArray();

		errorDiagnostics.Should().NotBeEmpty("XPC2001 should be reported when plugin class has no parameterless constructor");
		errorDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public void Should_Handle_XPC5000_Generation_Error_Gracefully()
	{
		// This test verifies that the generator doesn't crash on unexpected errors
		// We can't easily force an XPC5000 error, but we verify the compilation doesn't fail

		// Arrange - complex but valid source
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.PluginWithBothImages);

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - should not have critical errors
		var criticalErrors = result.GeneratorDiagnostics
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();

		criticalErrors.Should().BeEmpty("generator should not produce critical errors for valid source");

		// Verify generation succeeded
		result.GeneratedTrees.Should().NotBeEmpty("code should be generated");
	}

	[Fact]
	public async Task Should_Report_XPC4001_When_Handler_Method_Not_Found()
	{
		// Arrange - method reference points to NonExistentMethod but service has Process
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.NonExistentMethod)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process();
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerMethodNotFoundAnalyzer());

		// Assert
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC4001")
			.ToArray();

		errorDiagnostics.Should().NotBeEmpty("XPC4001 should be reported when handler method is not found");
		errorDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Error);
	}

	[Fact]
	public async Task Should_Not_Report_XPC4001_When_Handler_Method_Is_Inherited_From_Base_Interface()
	{
		// Arrange - the handler method is declared on a grandparent interface in a
		// multi-level inheritance chain (ITestService : IMidService : IBaseService).
		// The method must still be detected via the transitive AllInterfaces walk.
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.Process)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface IBaseService
			    {
			        void Process();
			    }

			    public interface IMidService : IBaseService
			    {
			    }

			    public interface ITestService : IMidService
			    {
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerMethodNotFoundAnalyzer());

		// Assert
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC4001")
			.ToArray();

		errorDiagnostics.Should().BeEmpty("XPC4001 should not be reported when the handler method is inherited from a base interface");
	}

	[Fact]
	public async Task Should_Report_XPC4002_When_Handler_Missing_PreImage_Parameter()
	{
		// Arrange - WithPreImage is registered but handler takes no parameters
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.Process)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process();  // No PreImage parameter!
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		// Assert
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC4002")
			.ToArray();

		errorDiagnostics.Should().NotBeEmpty("XPC4002 should be reported when handler is missing PreImage parameter");
		// XPC4002 is Warning when generated types don't exist yet (allows initial build to succeed)
		errorDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Report_XPC4002_When_Handler_Missing_PostImage_Parameter()
	{
		// Arrange - WithPostImage is registered but handler takes no parameters
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.Process)
			                .WithPostImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process();  // No PostImage parameter!
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		// Assert
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC4002")
			.ToArray();

		errorDiagnostics.Should().NotBeEmpty("XPC4002 should be reported when handler is missing PostImage parameter");
		// XPC4002 is Warning when generated types don't exist yet (allows initial build to succeed)
		errorDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Report_XPC4002_When_Handler_Missing_Both_Image_Parameters()
	{
		// Arrange - Both WithPreImage and WithPostImage but handler takes no parameters
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.Process)
			                .WithPreImage(x => x.Name)
			                .WithPostImage(x => x.AccountNumber);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process();  // No parameters!
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		// Assert
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC4002")
			.ToArray();

		errorDiagnostics.Should().NotBeEmpty("XPC4002 should be reported when handler is missing both image parameters");
		// XPC4002 is Warning when generated types don't exist yet (allows initial build to succeed)
		errorDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Report_XPC4002_When_Handler_Has_Wrong_Parameter_Order()
	{
		// Arrange - WithPreImage and WithPostImage but handler has parameters in wrong order
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;
			using TestNamespace.PluginRegistrations.TestPlugin.AccountUpdatePostOperation;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.Process)
			                .WithPreImage(x => x.Name)
			                .WithPostImage(x => x.AccountNumber);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process(PostImage post, PreImage pre);  // Wrong order! Should be PreImage, PostImage
			    }

			    public class TestService : ITestService
			    {
			        public void Process(PostImage post, PreImage pre) { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		// Assert
		var errorDiagnostics = diagnostics
			.Where(d => d.Id == "XPC4002")
			.ToArray();

		errorDiagnostics.Should().NotBeEmpty("XPC4002 should be reported when handler has wrong parameter order");
		// XPC4002 is Warning when generated types don't exist yet (allows initial build to succeed)
		errorDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Report_XPC4002_When_Image_Parameter_Is_Same_Named_Type_From_Different_Namespace()
	{
		// The handler's PreImage shares the generated wrapper's short name but lives in a different
		// namespace, so it is NOT the generated wrapper and must be reported as a mismatch (matched by
		// namespace + name, not name alone).
		const string source = """
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;
			using WrongNamespace;

			namespace WrongNamespace
			{
			    public sealed class PreImage { }
			}

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.Process))
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process(PreImage pre);
			    }

			    public class TestService : ITestService
			    {
			        public void Process(PreImage pre) { }
			    }
			}
			""";

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		diagnostics.Should().Contain(d => d.Id == "XPC4002" || d.Id == "XPC4003");
	}

	[Fact]
	public async Task Should_Report_XPC3003_When_WithPreImage_Used_With_Invocation_Syntax()
	{
		// Arrange - WithPreImage used with s => s.DoSomething() (invocation) instead of s => s.DoSomething (method reference)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.DoSomething())  // Invocation syntax - NOT method reference
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert
		var warningDiagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		warningDiagnostics.Should().NotBeEmpty("XPC3003 should be reported when WithPreImage is used with invocation syntax");
		warningDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Report_XPC3003_When_WithPostImage_Used_With_Invocation_Syntax()
	{
		// Arrange - WithPostImage used with s => s.DoSomething() (invocation) instead of s => s.DoSomething (method reference)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.DoSomething())  // Invocation syntax - NOT method reference
			                .WithPostImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert
		var warningDiagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		warningDiagnostics.Should().NotBeEmpty("XPC3003 should be reported when WithPostImage is used with invocation syntax");
		warningDiagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Not_Report_XPC3003_When_Using_Method_Reference_Syntax()
	{
		// Arrange - Method reference syntax (correct usage)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;
			using TestNamespace.PluginRegistrations.TestPlugin.AccountUpdatePostOperation;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.HandleUpdate)  // Method reference - correct syntax
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate(PreImage preImage);
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate(PreImage preImage) { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert
		var warningDiagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		warningDiagnostics.Should().BeEmpty("XPC3003 should NOT be reported when using method reference syntax");
	}

	[Fact]
	public async Task Should_Not_Report_XPC3003_When_Old_Api_Used_Without_Images()
	{
		// Arrange - Invocation syntax but without WithPreImage/WithPostImage (no images registered)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.DoSomething())  // Invocation syntax - but no images
			                .AddFilteredAttributes(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert
		var warningDiagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		warningDiagnostics.Should().BeEmpty("XPC3003 should NOT be reported when old API is used without images");
	}

	[Fact]
	public async Task Should_Report_XPC3002_When_AddImage_Used_With_Invocation_Syntax()
	{
		// Arrange - AddImage (legacy API) used with s => s.DoSomething() (invocation)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.DoSomething())  // Invocation syntax with legacy AddImage
			                .AddImage(ImageType.PreImage, x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert - Should report XPC3002 (Info) NOT XPC3003 (Warning)
		var xpc3002Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3002")
			.ToArray();

		var xpc3003Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		xpc3002Diagnostics.Should().NotBeEmpty("XPC3002 should be reported when AddImage is used with invocation syntax");
		xpc3002Diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Info);
		xpc3003Diagnostics.Should().BeEmpty("XPC3003 should NOT be reported for legacy AddImage API");
	}

	[Fact]
	public async Task Should_Report_Both_XPC3002_And_XPC3003_When_AddImage_And_WithPreImage_Used_With_Invocation()
	{
		// Arrange - Both WithPreImage (modern) and AddImage (legacy) used with lambda invocation
		// Should report both: XPC3002 for AddImage, XPC3003 for lambda invocation with modern API
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.DoSomething())
			                .AddImage(ImageType.PostImage, x => x.AccountNumber)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert - Should report both diagnostics
		var xpc3003Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		var xpc3002Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3002")
			.ToArray();

		xpc3003Diagnostics.Should().NotBeEmpty("XPC3003 should be reported when modern API is used with lambda invocation");
		xpc3002Diagnostics.Should().NotBeEmpty("XPC3002 should be reported for AddImage usage");
	}

	[Fact]
	public async Task Should_Report_XPC3002_When_AddImage_Used_With_Nameof()
	{
		// Arrange - AddImage used with nameof() - should still suggest migration to modern API
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.DoSomething))
			                .AddImage(ImageType.PreImage, x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert - Should report XPC3002 for AddImage, but NOT XPC3003 (no lambda invocation)
		var xpc3002Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3002")
			.ToArray();

		var xpc3003Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		xpc3002Diagnostics.Should().NotBeEmpty("XPC3002 should be reported when AddImage is used, even with nameof()");
		xpc3002Diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Info);
		xpc3003Diagnostics.Should().BeEmpty("XPC3003 should NOT be reported when using nameof()");
	}

	[Fact]
	public async Task Should_Report_XPC3002_When_AddImage_Used_With_MethodReference()
	{
		// Arrange - AddImage used with method reference syntax (s => s.DoSomething without parentheses)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                s => s.DoSomething)
			                .AddImage(ImageType.PreImage, x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void DoSomething();
			    }

			    public class TestService : ITestService
			    {
			        public void DoSomething() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act - Run analyzer instead of generator
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new ImageWithoutMethodReferenceAnalyzer());

		// Assert - Should report XPC3002 for AddImage, but NOT XPC3003 (method reference, not invocation)
		var xpc3002Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3002")
			.ToArray();

		var xpc3003Diagnostics = diagnostics
			.Where(d => d.Id == "XPC3003")
			.ToArray();

		xpc3002Diagnostics.Should().NotBeEmpty("XPC3002 should be reported when AddImage is used, even with method reference");
		xpc3002Diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Info);
		xpc3003Diagnostics.Should().BeEmpty("XPC3003 should NOT be reported when using method reference syntax");
	}

	[Fact]
	public void Should_Generate_Types_Even_When_Handler_Method_Not_Found()
	{
		// Arrange - method reference points to NonExistentMethod but types should still be generated
		// This enables a better DX where developers can create the method with correct signature
		// using the generated PreImage/PostImage types
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.NonExistentMethod)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process();  // Different method, NonExistentMethod doesn't exist
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - Types should be generated even though handler method doesn't exist
		result.GeneratedTrees.Should().NotBeEmpty(
			"PreImage/PostImage types should be generated even when handler method doesn't exist");

		// Verify PreImage class is generated
		var generatedSource = result.GeneratedTrees.First().ToString();
		generatedSource.Should().Contain("public sealed class TestPluginAccountUpdatePostOperationPreImage",
			"PreImage class should be generated to allow developers to create the handler method with correct signature");
	}

	[Fact]
	public void Should_Generate_Types_Even_When_Handler_Method_Wrong_Signature()
	{
		// Arrange - method reference points to MethodWithoutImage but types should still be generated
		// This enables a better DX where developers can create the method with correct signature
		// using the generated PreImage/PostImage types
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.MethodWithoutImage)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void Process();  // Different method
					void MethodWithoutImage(); // Method exists but wrong signature (missing PreImage parameter)
			    }

			    public class TestService : ITestService
			    {
			        public void Process() { }
					public void MethodWithoutImage() { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - Types should be generated even though handler method doesn't exist
		result.GeneratedTrees.Should().NotBeEmpty(
			"PreImage/PostImage types should be generated even when handler method doesn't exist");

		// Verify PreImage class is generated
		var generatedSource = result.GeneratedTrees.First().ToString();
		generatedSource.Should().Contain("public sealed class TestPluginAccountUpdatePostOperationPreImage",
			"PreImage class should be generated to allow developers to create the handler method with correct signature");

		generatedSource.Should().Contain("namespace TestNamespace",
			"Generated types should be in the correct namespace");
	}

	[Fact]
	public void Should_Generate_Unique_Files_For_Same_Named_Plugins_In_Different_Namespaces()
	{
		// Arrange - Two plugins with the same class name but in different namespaces
		// Both register the same entity/operation/stage combination
		// Previously this would cause a hint name collision
		// Note: We don't use GetCompleteSource here because it strips namespaces
		const string source = """
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace Namespace1
			{
			    public class AccountPlugin : Plugin
			    {
			        public AccountPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.HandleUpdate)
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate();
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate() { }
			    }
			}

			namespace Namespace2
			{
			    public class AccountPlugin : Plugin
			    {
			        public AccountPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                service => service.HandleUpdate)
			                .WithPreImage(x => x.AccountNumber);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate();
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate() { }
			    }
			}
			""";

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - Both plugins should generate separate files with unique hint names
		result.GeneratedSources.Should().HaveCount(2,
			"both plugins should generate code without hint name collision");

		// Index sources by hint name for precise verification
		var sourcesByHintName = result.GeneratedSources.ToDictionary(gs => gs.HintName, gs => gs.SourceText);

		// Find the hint names for each namespace
		var namespace1HintName = sourcesByHintName.Keys.Single(h => h.Contains("Namespace1_"));
		var namespace2HintName = sourcesByHintName.Keys.Single(h => h.Contains("Namespace2_"));

		// Verify Namespace1 source: correct namespace AND correct property (Name)
		var namespace1Source = sourcesByHintName[namespace1HintName];
		namespace1Source.Should().Contain("namespace Namespace1",
			"Namespace1 hint name should map to Namespace1 generated namespace");
		namespace1Source.Should().Contain("public string? Name =>",
			"Namespace1 plugin registered Name attribute");

		// Verify Namespace2 source: correct namespace AND correct property (AccountNumber)
		var namespace2Source = sourcesByHintName[namespace2HintName];
		namespace2Source.Should().Contain("namespace Namespace2",
			"Namespace2 hint name should map to Namespace2 generated namespace");
		namespace2Source.Should().Contain("public string? AccountNumber =>",
			"Namespace2 plugin registered AccountNumber attribute");

		// Verify each source only contains its own namespace (not cross-contaminated)
		namespace1Source.Should().NotContain("namespace Namespace2",
			"Namespace1 source should not contain Namespace2 namespace declaration");
		namespace2Source.Should().NotContain("namespace Namespace1",
			"Namespace2 source should not contain Namespace1 namespace declaration");
	}

	[Fact]
	public async Task Should_Report_XPC3005_When_WithPreImage_Has_No_Arguments()
	{
		var source = TestFixtures.GetCompleteSource(TestFixtures.PluginWithFullEntityPreImage);

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new FullEntityImageAnalyzer());

		var warnings = diagnostics.Where(d => d.Id == "XPC3005").ToArray();

		warnings.Should().NotBeEmpty("XPC3005 should be reported when WithPreImage() is called with no arguments");
		warnings.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Report_XPC3005_When_WithPostImage_Has_No_Arguments()
	{
		var source = TestFixtures.GetCompleteSource(TestFixtures.PluginWithFullEntityPostImage);

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new FullEntityImageAnalyzer());

		var warnings = diagnostics.Where(d => d.Id == "XPC3005").ToArray();

		warnings.Should().NotBeEmpty("XPC3005 should be reported when WithPostImage() is called with no arguments");
		warnings.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
	}

	[Fact]
	public async Task Should_Not_Report_XPC3005_When_WithPreImage_Has_Arguments()
	{
		var source = TestFixtures.GetCompleteSource(TestFixtures.GetPluginWithPreImage());

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new FullEntityImageAnalyzer());

		var warnings = diagnostics.Where(d => d.Id == "XPC3005").ToArray();

		warnings.Should().BeEmpty("XPC3005 should NOT be reported when WithPreImage() is called with specific attributes");
	}

	[Fact]
	public async Task Should_Not_Report_XPC3005_When_WithPostImage_Has_Arguments()
	{
		var source = TestFixtures.GetCompleteSource(TestFixtures.PluginWithPostImage);

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new FullEntityImageAnalyzer());

		var warnings = diagnostics.Where(d => d.Id == "XPC3005").ToArray();

		warnings.Should().BeEmpty("XPC3005 should NOT be reported when WithPostImage() is called with specific attributes");
	}

	[Theory]
	[InlineData("IPluginPreImage<Account> preImage")]
	[InlineData("IPluginPreImage preImage")]
	[InlineData("IPluginImage<Account> preImage")]
	[InlineData("IPluginImage preImage")]
	public async Task Should_Not_Report_Signature_Mismatch_When_Handler_Uses_Shared_PreImage_Interface(string parameter)
	{
		// Arrange - handler accepts a shared image interface instead of the concrete PreImage
		var pluginSource = $$"""

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;
			using TestNamespace.PluginRegistrations.TestPlugin.AccountUpdatePostOperation;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleUpdate))
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate({{parameter}});
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate({{parameter}}) { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act
		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		// Assert - no signature mismatch should be reported for valid shared interfaces
		diagnostics
			.Where(d => d.Id == "XPC4002" || d.Id == "XPC4003")
			.Should().BeEmpty($"a handler parameter of type '{parameter}' should be accepted");
	}

	[Fact]
	public async Task Should_Not_Report_Signature_Mismatch_When_Both_Images_Use_Shared_Interfaces()
	{
		// Arrange - both images registered, handler uses the typed Pre/Post interfaces
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleUpdate))
			                .WithPreImage(x => x.Name)
			                .WithPostImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate(IPluginPreImage<Account> pre, IPluginPostImage<Account> post);
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate(IPluginPreImage<Account> pre, IPluginPostImage<Account> post) { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		diagnostics
			.Where(d => d.Id == "XPC4002" || d.Id == "XPC4003")
			.Should().BeEmpty("typed Pre/Post image interfaces should be accepted for both images");
	}

	[Fact]
	public async Task Should_Report_Signature_Mismatch_When_Generic_Image_Interface_Has_Wrong_Entity()
	{
		// Arrange - generic interface with an entity type that does not match the registered TEntity
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleUpdate))
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate(IPluginPreImage<Contact> preImage);
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate(IPluginPreImage<Contact> preImage) { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		diagnostics
			.Where(d => d.Id == "XPC4002" || d.Id == "XPC4003")
			.Should().NotBeEmpty("a generic image interface with the wrong entity type should be rejected");
	}

	[Fact]
	public async Task Should_Report_Signature_Mismatch_When_PostImage_Interface_Used_For_PreImage()
	{
		// Arrange - PreImage registered but handler asks for IPluginPostImage (wrong kind)
		const string pluginSource = """

			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using TestNamespace;

			namespace TestNamespace
			{
			    public class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleUpdate))
			                .WithPreImage(x => x.Name);
			        }

			        protected override IServiceCollection OnBeforeBuildServiceProvider(IServiceCollection services)
			        {
			            return services.AddScoped<ITestService, TestService>();
			        }
			    }

			    public interface ITestService
			    {
			        void HandleUpdate(IPluginPostImage<Account> postImage);
			    }

			    public class TestService : ITestService
			    {
			        public void HandleUpdate(IPluginPostImage<Account> postImage) { }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		var diagnostics = await GetAnalyzerDiagnosticsAsync(source, new HandlerSignatureMismatchAnalyzer());

		diagnostics
			.Where(d => d.Id == "XPC4002" || d.Id == "XPC4003")
			.Should().NotBeEmpty("the post-image interface should not satisfy a registered pre-image");
	}

	private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
	{
		var compilation = CompilationHelper.CreateCompilation(source);

		var compilationWithAnalyzers = compilation.WithAnalyzers(
			[analyzer]);

		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
