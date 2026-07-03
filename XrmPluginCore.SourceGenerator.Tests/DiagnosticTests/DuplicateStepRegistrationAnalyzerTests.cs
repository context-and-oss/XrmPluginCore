using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using XrmPluginCore.SourceGenerator.Analyzers;
using XrmPluginCore.SourceGenerator.Tests.Helpers;
using Xunit;

namespace XrmPluginCore.SourceGenerator.Tests.DiagnosticTests;

/// <summary>
/// Tests for XPC2002 (duplicate plugin step registration for the same entity/operation/stage).
/// </summary>
public class DuplicateStepRegistrationAnalyzerTests
{
	[Fact]
	public async Task Should_Report_XPC2002_For_Duplicate_Entity_Operation_Stage()
	{
		var source = WrapPlugin("""
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "XPC2002").Subject;
		diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
		diagnostic.GetMessage().Should().Contain("Account/Update/PostOperation");
	}

	[Fact]
	public async Task Should_Not_Report_When_Stage_Differs()
	{
		var source = WrapPlugin("""
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PreOperation,
			                nameof(ITestService.HandleA));
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().NotContain(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Not_Report_When_Operation_Differs()
	{
		var source = WrapPlugin("""
			            RegisterStep<Account, ITestService>(EventOperation.Create, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().NotContain(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Report_Once_Per_Extra_Duplicate()
	{
		// Three registrations of the same tuple -> two duplicates reported (2nd and 3rd).
		var source = WrapPlugin("""
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleC));
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Count(d => d.Id == "XPC2002").Should().Be(2);
	}

	[Fact]
	public async Task Should_Report_For_Duplicate_Custom_Message_String()
	{
		var source = WrapPlugin(""""
			            RegisterStep<Account>("custom_DoThing", ExecutionStage.PostOperation, s => { });
			            RegisterStep<Account>("custom_DoThing", ExecutionStage.PostOperation, s => { });
			"""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().ContainSingle(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Report_For_Duplicate_RegisterPluginStep()
	{
		var source = WrapPlugin("""
			            RegisterPluginStep<Account>(EventOperation.Update, ExecutionStage.PostOperation, ctx => { });
			            RegisterPluginStep<Account>(EventOperation.Update, ExecutionStage.PostOperation, ctx => { });
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().ContainSingle(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Report_For_Duplicate_Const_Message_Name()
	{
		var source = WrapPlugin("""
			            const string Msg = "custom_DoThing";
			            RegisterStep<Account>(Msg, ExecutionStage.PostOperation, s => { });
			            RegisterStep<Account>(Msg, ExecutionStage.PostOperation, s => { });
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().ContainSingle(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Report_For_Duplicate_Const_Enum_Operation()
	{
		var source = WrapPlugin("""
			            const EventOperation Op = EventOperation.Update;
			            RegisterStep<Account, ITestService>(Op, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			            RegisterStep<Account, ITestService>(Op, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().ContainSingle(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Not_Report_When_Const_Enum_Operations_Differ()
	{
		var source = WrapPlugin("""
			            const EventOperation Create = EventOperation.Create;
			            const EventOperation Update = EventOperation.Update;
			            RegisterStep<Account, ITestService>(Create, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			            RegisterStep<Account, ITestService>(Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			""");

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().NotContain(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Report_When_Duplicate_Split_Across_Partial_Declarations()
	{
		const string source = """
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace TestNamespace
			{
			    public partial class TestPlugin : Plugin
			    {
			        public TestPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			            RegisterExtra();
			        }
			    }

			    public partial class TestPlugin
			    {
			        private void RegisterExtra()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			        }
			    }

			    public interface ITestService { void HandleA(); void HandleB(); }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().ContainSingle(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Report_When_Derived_Re_Registers_Inherited_Tuple()
	{
		const string source = """
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace TestNamespace
			{
			    public class BasePlugin : Plugin
			    {
			        public BasePlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			        }
			    }

			    public class DerivedPlugin : BasePlugin
			    {
			        public DerivedPlugin()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleB));
			        }
			    }

			    public interface ITestService { void HandleA(); void HandleB(); }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source);

		var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "XPC2002").Subject;
		diagnostic.GetMessage().Should().Contain("DerivedPlugin");
	}

	[Fact]
	public async Task Should_Not_Report_For_Non_Plugin_Class_With_RegisterStep_Method()
	{
		// A class that is not a Plugin but happens to have a generic RegisterStep<T> method must not
		// be flagged.
		const string source = """
			using XrmPluginCore.Enums;

			namespace TestNamespace
			{
			    public class NotAPlugin
			    {
			        public NotAPlugin()
			        {
			            RegisterStep<object>(EventOperation.Update, ExecutionStage.PostOperation);
			            RegisterStep<object>(EventOperation.Update, ExecutionStage.PostOperation);
			        }

			        private void RegisterStep<T>(EventOperation op, ExecutionStage stage) { }
			    }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().NotContain(d => d.Id == "XPC2002");
	}

	[Fact]
	public async Task Should_Not_Report_Across_Different_Plugin_Classes()
	{
		const string source = """
			using System;
			using Microsoft.Xrm.Sdk;
			using XrmPluginCore;
			using XrmPluginCore.Enums;
			using Microsoft.Extensions.DependencyInjection;
			using XrmPluginCore.Tests.Context.BusinessDomain;

			namespace TestNamespace
			{
			    public class PluginA : Plugin
			    {
			        public PluginA()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			        }
			    }

			    public class PluginB : Plugin
			    {
			        public PluginB()
			        {
			            RegisterStep<Account, ITestService>(EventOperation.Update, ExecutionStage.PostOperation,
			                nameof(ITestService.HandleA));
			        }
			    }

			    public interface ITestService { void HandleA(); }
			}
			""";

		var diagnostics = await GetDiagnosticsAsync(source);

		diagnostics.Should().NotContain(d => d.Id == "XPC2002");
	}

	private static string WrapPlugin(string registrations) =>
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
			{{registrations}}
			        }
			    }

			    public interface ITestService
			    {
			        void HandleA();
			        void HandleB();
			        void HandleC();
			    }
			}
			""";

	private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
	{
		var compilation = CompilationHelper.CreateCompilation(source);
		var compilationWithAnalyzers = compilation.WithAnalyzers([new DuplicateStepRegistrationAnalyzer()]);
		return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
	}
}
