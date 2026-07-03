using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Xrm.Sdk;
using XrmPluginCore.SourceGenerator.Tests.Helpers;
using Xunit;

namespace XrmPluginCore.SourceGenerator.Tests.IntegrationTests;

/// <summary>
/// Integration tests that verify generated code compiles and runs correctly.
/// </summary>
public class CompilationTests
{
	[Fact]
	public void Should_Compile_Generated_Code_Without_Errors()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.GetPluginWithPreImage());

		// Act
		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);

		// Assert
		result.Success.Should().BeTrue(
			because: $"compilation should succeed. Errors: {string.Join(", ", result.Errors ?? [])}");
		result.AssemblyBytes.Should().NotBeNull();
	}

	[Fact]
	public void Should_Instantiate_Generated_PreImage_Class_Via_Reflection()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.GetPluginWithPreImage());

		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);
		result.Success.Should().BeTrue();

		// Create test entity
		var accountId = Guid.NewGuid();
		var entity = new Entity("account", accountId)
		{
			["name"] = "Test Account",
			["revenue"] = new Money(100000)
		};

		// Act
		using var loadedAssembly = GeneratorTestHelper.LoadAssembly(result.AssemblyBytes!);
		var preImageType = loadedAssembly.Assembly.GetType(
			"TestNamespace.TestPluginAccountUpdatePostOperationPreImage");

		preImageType.Should().NotBeNull("PreImage class should be generated");

		var preImageInstance = Activator.CreateInstance(preImageType!, entity);

		// Assert
		preImageInstance.Should().NotBeNull();

		var nameProperty = preImageType!.GetProperty("Name");
		nameProperty.Should().NotBeNull();
		var nameValue = nameProperty!.GetValue(preImageInstance);
		nameValue.Should().Be("Test Account");

		var revenueProperty = preImageType.GetProperty("Revenue");
		revenueProperty.Should().NotBeNull();
		var revenueValue = revenueProperty!.GetValue(preImageInstance) as decimal?;
		revenueValue!.Should().Be(100000);

		// Id is always exposed (from the Entity base type)
		var idProperty = preImageType.GetProperty("Id");
		idProperty.Should().NotBeNull("Id should always be exposed on generated images");
		idProperty!.PropertyType.Should().Be(typeof(Guid));
		idProperty.GetValue(preImageInstance).Should().Be(accountId);

		// LogicalName is always exposed (from the Entity base type)
		var logicalNameProperty = preImageType.GetProperty("LogicalName");
		logicalNameProperty.Should().NotBeNull("LogicalName should always be exposed on generated images");
		logicalNameProperty!.PropertyType.Should().Be(typeof(string));
		logicalNameProperty.GetValue(preImageInstance).Should().Be("account");
	}

	[Fact]
	public void Should_Access_Properties_And_Verify_Values()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.PluginWithPostImage);

		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);
		result.Success.Should().BeTrue();

		var entity = new Entity("account")
		{
			["name"] = "Test Account",
			["accountnumber"] = "ACC-12345"
		};

		// Act
		using var loadedAssembly = GeneratorTestHelper.LoadAssembly(result.AssemblyBytes!);
		var postImageType = loadedAssembly.Assembly.GetType(
			"TestNamespace.TestPluginAccountUpdatePostOperationPostImage");

		var postImageInstance = Activator.CreateInstance(postImageType!, entity);

		// Assert
		var nameProperty = postImageType!.GetProperty("Name");
		nameProperty!.GetValue(postImageInstance).Should().Be("Test Account");

		var accountNumberProperty = postImageType.GetProperty("AccountNumber");
		accountNumberProperty!.GetValue(postImageInstance).Should().Be("ACC-12345");
	}

	[Fact]
	public void Should_Work_With_Both_PreImage_And_PostImage()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.PluginWithBothImages);

		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);
		result.Success.Should().BeTrue();

		var preEntity = new Entity("account")
		{
			["name"] = "Old Name",
			["revenue"] = new Money(50000)
		};

		var postEntity = new Entity("account")
		{
			["name"] = "New Name",
			["accountnumber"] = "ACC-12345"
		};

		// Act
		using var loadedAssembly = GeneratorTestHelper.LoadAssembly(result.AssemblyBytes!);
		const string baseNamespace = "TestNamespace";

		var preImageType = loadedAssembly.Assembly.GetType($"{baseNamespace}.TestPluginAccountUpdatePostOperationPreImage");
		var postImageType = loadedAssembly.Assembly.GetType($"{baseNamespace}.TestPluginAccountUpdatePostOperationPostImage");

		var preImageInstance = Activator.CreateInstance(preImageType!, preEntity);
		var postImageInstance = Activator.CreateInstance(postImageType!, postEntity);

		// Assert - PreImage
		preImageType!.GetProperty("Name")!.GetValue(preImageInstance).Should().Be("Old Name");
		var preRevenue = preImageType.GetProperty("Revenue")!.GetValue(preImageInstance) as decimal?;
		preRevenue!.Should().Be(50000);

		// Assert - PostImage
		postImageType!.GetProperty("Name")!.GetValue(postImageInstance).Should().Be("New Name");
		postImageType.GetProperty("AccountNumber")!.GetValue(postImageInstance).Should().Be("ACC-12345");
	}

	[Fact]
	public void Should_Handle_Null_Attribute_Values_Gracefully()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.GetPluginWithPreImage());

		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);
		result.Success.Should().BeTrue();

		// Entity with missing attributes
		var entity = new Entity("account");

		// Act
		using var loadedAssembly = GeneratorTestHelper.LoadAssembly(result.AssemblyBytes!);
		var preImageType = loadedAssembly.Assembly.GetType(
			"TestNamespace.TestPluginAccountUpdatePostOperationPreImage");

		var preImageInstance = Activator.CreateInstance(preImageType!, entity);

		// Assert - should return null for missing attributes, not throw
		var nameProperty = preImageType!.GetProperty("Name");
		var nameValue = nameProperty!.GetValue(preImageInstance);
		nameValue.Should().BeNull();

		var revenueProperty = preImageType.GetProperty("Revenue");
		var revenueValue = revenueProperty!.GetValue(preImageInstance);
		revenueValue.Should().BeNull();
	}

	[Fact]
	public void Should_Generate_Types_In_Plugin_Namespace()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.GetPluginWithPreImage());

		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);
		result.Success.Should().BeTrue();

		// Act
		using var loadedAssembly = GeneratorTestHelper.LoadAssembly(result.AssemblyBytes!);

		// Assert - generated types live in the plugin's own namespace (registration-derived type names)
		const string expectedNamespace = "TestNamespace";
		var preImageType = loadedAssembly.Assembly.GetType($"{expectedNamespace}.TestPluginAccountUpdatePostOperationPreImage");

		preImageType.Should().NotBeNull("PreImage should be in the expected namespace");
		preImageType!.Namespace.Should().Be(expectedNamespace);
	}

	[Fact]
	public void Should_Generate_ActionWrapper_Class()
	{
		// Arrange
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.GetPluginWithPreImage());

		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);
		result.Success.Should().BeTrue();

		// Act
		using var loadedAssembly = GeneratorTestHelper.LoadAssembly(result.AssemblyBytes!);
		const string expectedNamespace = "TestNamespace";
		var actionWrapperType = loadedAssembly.Assembly.GetType($"{expectedNamespace}.TestPluginAccountUpdatePostOperationActionWrapper");

		// Assert
		actionWrapperType.Should().NotBeNull("ActionWrapper should be generated");

		// Verify ActionWrapper implements IActionWrapper interface
		var iactionWrapperInterface = actionWrapperType!.GetInterface("IActionWrapper");
		iactionWrapperInterface.Should().NotBeNull("ActionWrapper should implement IActionWrapper interface");

		// Verify CreateAction method exists (now instance method, not static)
		var createActionMethod = actionWrapperType.GetMethod("CreateAction");
		createActionMethod.Should().NotBeNull("CreateAction method should exist");
		createActionMethod!.IsStatic.Should().BeFalse("CreateAction should be an instance method since ActionWrapper implements IActionWrapper");
	}

	[Fact]
	public void Should_Compile_When_Handler_Uses_Shared_Image_Interfaces()
	{
		// Arrange - the generated ActionWrapper must still compile when it passes the concrete
		// PreImage/PostImage to a handler that declares the shared image interfaces as parameters.
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
			        public void HandleUpdate(IPluginPreImage<Account> pre, IPluginPostImage<Account> post)
			        {
			            // Access via the type-safe interface
			            var before = pre.Entity.Name;
			            var after = post.Entity.Name;

			            // Access via the non-generic base interface (shared helper scenario)
			            IPluginImage shared = pre;
			            var raw = shared.Entity;
			        }
			    }
			}
			""";

		var source = TestFixtures.GetCompleteSource(pluginSource);

		// Act
		var result = GeneratorTestHelper.RunGeneratorAndCompile(source);

		// Assert
		result.Success.Should().BeTrue(
			because: $"compilation should succeed. Errors: {string.Join(", ", result.Errors ?? [])}");
	}

	[Fact]
	public void Should_Not_Emit_Obsolete_Warnings_From_Generated_Image_Code()
	{
		// Arrange - image explicitly registers deprecated ([Obsolete]) attributes
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.PluginWithObsoleteImageAttributes);

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - no CS0612/CS0618 may originate from the auto-generated image class. (The registration
		// lambdas `x => x.ctx_DeprecatedField` legitimately warn in the user's own code; those are not generated.)
		var generatedTrees = result.GeneratedTrees;
		var obsoleteInGenerated = result.OutputCompilation.GetDiagnostics()
			.Where(d => d.Id is "CS0612" or "CS0618")
			.Where(d => d.Location.SourceTree != null && generatedTrees.Contains(d.Location.SourceTree))
			.ToArray();

		obsoleteInGenerated.Should().BeEmpty(
			because: $"generated image code must not raise deprecation warnings. Found: {string.Join(", ", obsoleteInGenerated.Select(d => $"{d.Id} @ {d.Location.GetLineSpan()}"))}");
	}

	[Fact]
	public void Should_Not_Emit_Obsolete_Warnings_For_Implicit_Full_Entity_Image()
	{
		// Arrange - WithPreImage() with no arguments implicitly captures ALL entity attributes,
		// including deprecated ([Obsolete]) ones, without the user naming them anywhere.
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.PluginWithFullEntityPreImage);

		// Act
		var result = GeneratorTestHelper.RunGenerator(
			CompilationHelper.CreateCompilation(source));

		// Assert - nothing should warn: the user never referenced a deprecated member and the
		// generated accessors mirror the [Obsolete] attribute, so no warning leaks from generated code.
		var obsoleteDiagnostics = result.OutputCompilation.GetDiagnostics()
			.Where(d => d.Id is "CS0612" or "CS0618")
			.ToArray();

		obsoleteDiagnostics.Should().BeEmpty(
			because: $"implicitly captured deprecated attributes must not produce warnings. Found: {string.Join(", ", obsoleteDiagnostics.Select(d => $"{d.Id} @ {d.Location.GetLineSpan()}"))}");
	}

	[Fact]
	public void Should_Push_Obsolete_Warning_To_Calling_Code()
	{
		// Arrange - the handler reads a deprecated image attribute
		var source = TestFixtures.GetCompleteSource(
			TestFixtures.PluginAccessingObsoleteImageAttribute);

		var compilation = CompilationHelper.CreateCompilation(source);

		// Act
		var result = GeneratorTestHelper.RunGenerator(compilation);

		// Assert
		var generatedTrees = result.GeneratedTrees;
		var userTree = result.OutputCompilation.SyntaxTrees
			.Single(tree => !generatedTrees.Contains(tree));

		var obsoleteDiagnostics = result.OutputCompilation.GetDiagnostics()
			.Where(d => d.Id == "CS0612")
			.ToArray();

		// The warning exists...
		obsoleteDiagnostics.Should().NotBeEmpty("accessing a deprecated image member should raise CS0612");

		// ...and it points at the calling code, never the auto-generated image class.
		obsoleteDiagnostics.Should().OnlyContain(
			d => d.Location.SourceTree == userTree,
			"the deprecation warning must surface in the calling code, not the generated image class");
	}
}
